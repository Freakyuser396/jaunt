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
        float desiredflyHeightAboveGround = 10f;
        protected double desiredYPos;
        float wanderDuration;

        //movementspeed
        float flySpeed = 0.01f;
        float glideSpeed = 0.005f;
        float hoverSpeed = 0.00001f;




        public AiTaskJauntflyWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
        {
            
        }


        public override bool ShouldExecute()
        {
            var flyer = entity as EntityFlyingAgent;
            if (PreconditionsSatisfied() & flyer.IsInAiFlight == false & entity.OnGround)
            {
                return true;
            }
            return false;
        }


        public override void StartExecute()
        {
            base.StartExecute();
            wanderDuration = 5f;
            var flyer = entity as EntityFlyingAgent;
            flyer.IsInAiFlight = true;
            ReadjustFlyHeight();
            ModSystem.Logger.Notification("started");

        }


        public override bool ContinueExecute(float dt)
        {
            if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
            {
                ReadjustFlyHeight();
            }

            wanderDuration -= dt;
            ModSystem.Logger.Notification("wander duration is: " + wanderDuration);

            

            double dy = desiredYPos - entity.Pos.Y;
            double yMot = GameMath.Clamp(dy, -1, 1);


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
                return false;
            }

            if (entity.CollidedHorizontally)
            {
                return false;
            }

            return true;
        }
        public override void FinishExecute(bool cancelled)
        {
            ModSystem.Logger.Notification("finishing task");
            var flyer = entity as EntityFlyingAgent;
            flyer.IsInAiFlight = false;
            base.FinishExecute(cancelled);

        }

        protected void ReadjustFlyHeight()
        {
            ModSystem.Logger.Notification("readjusting height");
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