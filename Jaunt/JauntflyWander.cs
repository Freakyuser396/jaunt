using Jaunt.Entities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.GameContent;
using System.Collections;
using System.ComponentModel.Design;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using HarmonyLib;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;
using Vintagestory.ServerMods.WorldEdit;
using Jaunt.Behaviors;
using Jaunt.Config;
using Jaunt.Hud;
using Jaunt.Systems;
using Jaunt.Items;
using System.Diagnostics.Metrics;


#nullable disable

namespace Jaunt
{
    public class AiTaskJauntflyWander : AiTaskBase
    {
        ICoreAPI api;
        ICoreClientAPI capi;
        public static JauntModSystem ModSystem => JauntModSystem.Instance;


        //height and distance
        float desiredflyHeightAboveGround;
        float wanderDuration;

        bool landingAnimationStarted;
        bool isDescending;
        bool isAscending;
        bool flyDownAnimationStarted;

        bool flyAnimationStarted;
        bool flyUpAnimationStarted;

        bool stayNearSpawn;
        float radius;
        float minDistance;
        protected double desiredYPos;
        protected float moveSpeed = 0.02f;
        public Vec3d SpawnPos;

        public Vec3d targetPos;

        float targetTolerangeRange;

        private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;
        




        public AiTaskJauntflyWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            stayNearSpawn = taskConfig["stayNearSpawn"].AsBool(false);
            radius = taskConfig["radius"].AsFloat(50f);
            minDistance = taskConfig["minDistance"].AsFloat(50f);
            moveSpeed = taskConfig["moveSpeed"].AsFloat(0.04f);
            targetTolerangeRange = taskConfig["targetTolerangeRange"].AsFloat(15f);
            
        }

        public override void OnEntityLoaded()
        {
            loadOrCreateSpawnPos();
        }

        public override void OnEntitySpawn()
        {
            loadOrCreateSpawnPos();
        }

        public void loadOrCreateSpawnPos()
        {
            if (entity.WatchedAttributes.HasAttribute("spawnPosX"))
            {
                SpawnPos = new Vec3d(
                    entity.WatchedAttributes.GetDouble("spawnPosX"),
                    entity.WatchedAttributes.GetDouble("spawnPosY"),
                    entity.WatchedAttributes.GetDouble("spawnPosZ")
                );
            }
            else
            {
                SpawnPos = entity.Pos.XYZ;
                entity.WatchedAttributes.SetDouble("spawnPosX", SpawnPos.X);
                entity.WatchedAttributes.SetDouble("spawnPosY", SpawnPos.Y);
                entity.WatchedAttributes.SetDouble("spawnPosZ", SpawnPos.Z);
            }
        }

        public override bool ShouldExecute()
        {
            if (PreconditionsSatisfied() && Flyer.IsInAiFlight == false && entity.OnGround && cooldownUntilMs > entity.World.ElapsedMilliseconds)
            {
                return true;
            }
            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();
            moveSpeed = 0.04f;
            desiredflyHeightAboveGround =10f;
            wanderDuration =50f;
            Flyer.IsInAiFlight = true;
            ReadjustFlyHeight();
            landingAnimationStarted = false;
            isDescending = false;
            isAscending = true;
            flyDownAnimationStarted = false;
            flyAnimationStarted = false;
            flyUpAnimationStarted = false;

            var fromPos = stayNearSpawn ? SpawnPos : entity.Pos.XYZ;

            double rndx=0;
            double rndz=0;
            for (int i = 0; i < 10; i++)
            {
                float yaw = (float)world.Rand.NextDouble() * GameMath.TWOPI;
                rndx = radius * Math.Sin(yaw);
                rndz = radius * Math.Cos(yaw);

                if (fromPos.AddCopy(rndx, 0, rndz).HorizontalSquareDistanceTo(entity.Pos.XYZ) > minDistance) break;
            }

            targetPos = fromPos.AddCopy(rndx, 0, rndz);

        }


        public override bool ContinueExecute(float dt)
        {
            if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
            {
                ReadjustFlyHeight();
            }

            wanderDuration -= dt;

            

            double dy = desiredYPos - entity.Pos.Y;
            double yMot = GameMath.Clamp(dy, -1, 1);
            if (isDescending){yMot *= 5;};
            int groundY = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);



            double dx = targetPos.X - entity.Pos.X;
            double dz = targetPos.Z - entity.Pos.Z;

            float targetYaw = (float)Math.Atan2(dx, dz);

            entity.Pos.Yaw += GameMath.AngleRadDistance(entity.Pos.Yaw, targetYaw) * dt/5;

            double cosYaw = Math.Cos(entity.Pos.Yaw);
            double sinYaw = Math.Sin(entity.Pos.Yaw);

            entity.Controls.WalkVector.Set(sinYaw, yMot, cosYaw);
            entity.Controls.WalkVector.Mul(moveSpeed);

            if (yMot < 0) entity.Controls.WalkVector.Mul(0.5);

            if (entity.Swimming)
            {
                entity.Controls.WalkVector.Y = 2 * moveSpeed;
                entity.Controls.FlyVector.Y = 2 * moveSpeed;
            }



            if (wanderDuration <= 0 )
            {
                desiredflyHeightAboveGround = 0f;
                isDescending = true;
                moveSpeed = 0.005f;

            }

            if (isAscending && !flyAnimationStarted && desiredflyHeightAboveGround > 0f && dy <= 0.1)
            {
                entity.StopAnimation("fly-up");
                entity.StartAnimation("fly");
                isAscending = false;
                flyAnimationStarted = true;
            }

            if (isAscending && !flyUpAnimationStarted)
            {
                entity.StartAnimation("fly-up");
                flyUpAnimationStarted = true;
            }

            

            if (isDescending && !flyDownAnimationStarted && entity.Pos.Motion.Y <= -0.01)
            {
                    entity.StopAnimation("fly");
                    entity.StopAnimation ("fly-up");
                    entity.StartAnimation("fly-down");
                    flyDownAnimationStarted = true;
            }

            if (wanderDuration <= 0 && entity.Pos.Y - groundY <= 3 && !landingAnimationStarted)
            {
                isDescending = false;
                entity.StopAnimation ("fly-down");
                entity.StartAnimation("land"); // Use the animation's code from the entity JSON.
                entity.Controls.WalkVector.Set(0, 0, 0);
                entity.Controls.FlyVector.Set(0, 0, 0);
                entity.Pos.Motion.X = 0;
                entity.Pos.Motion.Y = 0;
                entity.Pos.Motion.Z = 0;
                landingAnimationStarted = true;
                return false;
            }

            return true;
        }
        public override void FinishExecute(bool cancelled)
        {
            Flyer.IsInAiFlight = false;
            base.FinishExecute(cancelled);

        }

        protected void ReadjustFlyHeight()
        {
            int terrainYPos = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);
            int tries = 10;
            while (tries-- > 0)
            {
                Block block = entity.World.BlockAccessor.GetBlockRaw((int)entity.Pos.X, terrainYPos, (int)entity.Pos.Z, BlockLayersAccess.Fluid);
                if (block.IsLiquid())
                {
                    terrainYPos++;
                } else
                {
                    break;
                }
            }

            desiredYPos = terrainYPos + desiredflyHeightAboveGround;
        }

    }
}