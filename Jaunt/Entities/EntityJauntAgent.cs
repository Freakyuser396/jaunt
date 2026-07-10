using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using System.IO;
using System.Linq;
using Vintagestory.API.Config;
using Vintagestory.API.Util;
using Vintagestory.API.Server;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.InteropServices.Marshalling;
using Vintagestory.API;
using System.Threading.Tasks;
using System.Text;
using System.Globalization;
using System.ComponentModel;
using System.Reflection.Metadata.Ecma335;


#nullable disable

namespace Jaunt.Entities
{

    public class EntityJauntAgent : EntityAgent, IMountableListener
{
    public override double GetWalkSpeedMultiplier(double groundDragFactor = 0.3)
    {
        int y1 = (int)(Pos.InternalY - 0.05f);
        int y2 = (int)(Pos.InternalY + 0.01f);

        Block belowBlock = World.BlockAccessor.GetBlockRaw((int)Pos.X, y1, (int)Pos.Z);

        insidePos.Set((int)Pos.X, y2, (int)Pos.Z);
        insidePos.SetDimension(Pos.Dimension);
        insideBlock = World.BlockAccessor.GetBlock(insidePos);

        var ebc = GetBehavior<Behaviors.EntityBehaviorJauntWalkspeed>();
        double multiplier = (servercontrols.Sneak ? GlobalConstants.SneakSpeedMultiplier : 1.0) * (servercontrols.Sprint ? GlobalConstants.SprintSpeedMultiplier : 1.0);

        if (FeetInLiquid){multiplier /= 2.5;}
        multiplier *= belowBlock.WalkSpeedMultiplier * ebc?.ExtraSpeedModifier ?? 1.0 * (y1 == y2 ? 1 : insideBlock.WalkSpeedMultiplier);

        return multiplier;
    }

    public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
                if (Api.Side == EnumAppSide.Server)
                    {
                        var aiTaskManager = GetBehavior<EntityBehaviorTaskAI>().TaskManager;
                        aiTaskManager.OnShouldExecuteTask += TaskManager_OnShouldExecuteTask;
                    }
            hitchingEntityCode = Properties.Attributes["hitchingEntityCode"].AsString();
        }

    public bool hitchingPostNear = true;
    public bool canseesky = true;
    public float checkPostTimer;
    public float checkSkyTimer;
    string hitchingEntityCode = "hitchingpost";

    public override void OnGameTick(float dt)
        {
            checkSkyTimer += dt;
            float cssi = canseesky ? 5f : 30f;
            if (checkSkyTimer >= cssi)
                {
                    checkSkyTimer -= cssi;
                    CheckSky();
                }  
            checkPostTimer += dt;
            float hpnt = hitchingPostNear ? 30f : 5f;
            if (checkPostTimer >= hpnt)
                {
                    checkPostTimer -= hpnt;
                    CheckPost();
                }  
            base.OnGameTick(dt);
        }
    void CheckSky()
        {
            canseesky = Pos.Y >= World.BlockAccessor.GetRainMapHeightAt((int)Pos.X,(int)Pos.Z);
        }
    bool isnear;
    void CheckPost()
        {   
            bool wasnear = isnear;
            hitchingPostNear = World.GetEntitiesAround(Pos.XYZ, 10f, 10f).Any(e => e.Code?.FirstCodePart() == hitchingEntityCode);
            isnear = hitchingPostNear;
        }


    public void MountEasterEgg(EntityAgent entityAgent)
        {
            var tool = entityAgent.RightHandItemSlot?.Itemstack?.Collectible?.Code?.FirstCodePart();
            if (tool == null) return;

            switch (tool)
                {
                    case "joustinglance": StartAnimation("jump"); break;
                    case "lantern": StartAnimation("jump"); break;
                    case "flute": StartAnimation("Eat"); break;      
                }
        }
            
    void IMountableListener.DidMount(EntityAgent entityAgent)
        {
            StopAiTasks();
        //    MountEasterEgg(entityAgent);
        }
    void IMountableListener.DidUnmount(EntityAgent entityAgent)
        {
            CheckSky();  
            CheckPost();
            StopAiTasks();
        }
    private bool TaskManager_OnShouldExecuteTask(IAiTask t)
        {
        if (t is AiTaskStayInRange)
            {
                return hitchingPostNear; 
            }
        if (t is AiTaskWander)
            {
                if (hitchingPostNear) return false;
                if (!canseesky) return false;
                if (RecentlyDismounted(24)) return false;
            }
        if (t is AiTaskSeekFoodAndEat)
            {
                if (hitchingPostNear) return false;
                if (RecentlyDismounted(4)) return false;
            }
            return true;
        }
    public bool RecentlyDismounted(int hours)
        {
            return Api.World.Calendar.TotalHours - WatchedAttributes.GetDouble("lastDismountTotalHours") < hours;
        }
    void StopAiTasks()
        {
            AiTaskManager tm = GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;    
            if (tm == null) return;
            tm.StopTasks();
        }
    }
}