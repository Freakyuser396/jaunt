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


#nullable disable

namespace Jaunt
{
    public class AiTaskJauntflyWander : AiTaskBase
    {
        ICoreAPI api;
        ICoreClientAPI capi;
        public static JauntModSystem ModSystem => JauntModSystem.Instance;

        //animations
        string hoverAnimation = "hover";
        float hoverAnimationSpeed = 1f;
        string glideAnimation = "glide";
        float glideAnimationSpeed = 1f;
        string flyAnimation = "fly";
        float flyAnimationSpeed = 1f;

        //height and distance
        float desiredflyHeightAboveGround;
        protected double desiredYPos;
        float wanderDuration;

        //movementspeed
        float flySpeed = 0.01f;
        float glideSpeed = 0.005f;
        float hoverSpeed = 0.00001f;

        bool landingAnimationStarted;
        bool isDescending;
        bool isAscending;
        bool flyDownAnimationStarted;

        bool flyAnimationStarted;
        bool flyUpAnimationStarted;
        




        public AiTaskJauntflyWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            
        }


        public override bool ShouldExecute()
        {
            var flyer = entity as EntityFlyingAgent;
            if (PreconditionsSatisfied() && flyer.IsInAiFlight == false && entity.OnGround)
            {
                return true;
            }
            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();
            desiredflyHeightAboveGround =10f;
            wanderDuration =10f;
            var flyer = entity as EntityFlyingAgent;
            flyer.IsInAiFlight = true;
            ReadjustFlyHeight();
            landingAnimationStarted = false;
            isDescending = false;
            isAscending = true;
            flyDownAnimationStarted = false;
            flyAnimationStarted = false;
            flyUpAnimationStarted = false;

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
            int groundY = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);
            double heightAboveGround = entity.Pos.Y - groundY;



            if (entity.World.Rand.NextDouble() < 0.001)
            {
                entity.Pos.Motion.Y = 0.02f;
            }
            

            double cosYaw = Math.Cos(entity.Pos.Yaw);
            double sinYaw = Math.Sin(entity.Pos.Yaw);
            entity.Controls.WalkVector.Set(sinYaw, yMot, cosYaw);
            entity.Controls.WalkVector.Mul(flySpeed);
            if (yMot < 0) entity.Controls.WalkVector.Mul(0.75);

            if (entity.Swimming)
            {
                entity.Controls.WalkVector.Y = 2 * flySpeed;
                entity.Controls.FlyVector.Y = 2 * flySpeed;
            }



            if (wanderDuration <= 0 )
            {
                desiredflyHeightAboveGround = 0f;
                isDescending = true;

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

            if (isDescending && !flyDownAnimationStarted)
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
                entity.StopAnimation ("fly-up");
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
            var flyer = entity as EntityFlyingAgent;
            flyer.IsInAiFlight = false;
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