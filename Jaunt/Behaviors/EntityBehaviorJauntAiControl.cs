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

namespace Jaunt.Behaviors
{

    public class EntityBehaviorJauntAiControl : EntityBehavior, IMountableListener
    {
 
        public EntityBehaviorJauntAiControl(Entity entity) : base(entity) { }
        public override string PropertyName() => "jaunt:aicontrol";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            hitchingEntityCode = attributes["hitchingEntityCode"].AsString() ?? "hitchingpost";
            canWanderIndoors = attributes["wanderIndoors"].AsBool();
            eatDelayAfterDismount = attributes["eatDelayAfterDismount"].AsInt(4);
            wanderDelayAfterDismount = attributes["wanderDelayAfterDismount"].AsInt(24);
            base.Initialize(properties, attributes);
        }
        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
                if (this.entity.World.Side == EnumAppSide.Server)
                    {
                        var aiTaskManager = this.entity.GetBehavior<EntityBehaviorTaskAI>().TaskManager;
                        aiTaskManager.OnShouldExecuteTask += TaskManager_OnShouldExecuteTask;
                    }
        }
        bool canWanderIndoors = false;

        public bool hitchingPostNear = true;
        public bool canseesky = true;
        public float checkPostTimer;
        public float checkSkyTimer;
        string hitchingEntityCode;
        int eatDelayAfterDismount;
        int wanderDelayAfterDismount;
        public enum JauntAiState
        {
            Active = 0,
            Follow = 1,
            Stay = 2,
        }

        public JauntAiState CurrentState
        {
            get => (JauntAiState)entity.WatchedAttributes.GetInt("jauntAiState");
            private set => entity.WatchedAttributes.SetInt("jauntAiState", (int)value);
        }
        public void SetState(JauntAiState state)
            {
                if (entity.World.Side != EnumAppSide.Server) return;

                CurrentState = state;

                if (state != JauntAiState.Follow)
                {
                    StopAiTasks();
                }
            }
            public void CycleState()
            {
                SetState((JauntAiState)(((int)CurrentState + 1) % 3));
            }
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
            canseesky = this.entity.Pos.Y >= this.entity.World.BlockAccessor.GetRainMapHeightAt((int)this.entity.Pos.X,(int)this.entity.Pos.Z);
        }
        bool isnear;
        void CheckPost()
        {   
            bool wasnear = isnear;
            hitchingPostNear = this.entity.World.GetEntitiesAround(this.entity.Pos.XYZ, 10f, 10f).Any(e => e.Code?.FirstCodePart() == hitchingEntityCode);
            isnear = hitchingPostNear;
        }


        public void MountEasterEgg(EntityAgent entityAgent)
        {
            var tool = entityAgent.RightHandItemSlot?.Itemstack?.Collectible?.Code?.FirstCodePart();
            if (tool == null) return;

            switch (tool)
                {
                    case "joustinglance": this.entity.StartAnimation("jump"); break;
                    case "lantern": this.entity.StartAnimation("jump"); break;
                    case "flute": this.entity.StartAnimation("Eat"); break;      
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
        if (t is AiTaskComeToOwner)
            {
                if (CurrentState == JauntAiState.Follow) return true;
            }

        if (CurrentState != JauntAiState.Active) return false;

        if (t is AiTaskWander)
            {
                if (hitchingPostNear) return false;
                if (!canseesky && !canWanderIndoors) return false;
                if (RecentlyDismounted(wanderDelayAfterDismount)) return false;
            }
        if (t is AiTaskSeekFoodAndEat)
            {
                if (hitchingPostNear) return false;
                if (RecentlyDismounted(eatDelayAfterDismount)) return false;
            }
            return true;
        }
        public bool RecentlyDismounted(int hours)
        {
            return this.entity.World.Calendar.TotalHours - this.entity.WatchedAttributes.GetDouble("lastDismountTotalHours") < hours;
        }
        public void StopAiTasks()
        {
            AiTaskManager tm = this.entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;    
            if (tm == null) return;
            tm.StopTasks();
        }        
        
        public override void GetInfoText(StringBuilder infotext)
        {
            if (entity.Api is not ICoreClientAPI) return;

            // infotext.AppendLine("jaunt:infotext-ai-state: " + CurrentState);

            double timeSinceDismount = entity.World.Calendar.TotalHours - entity.WatchedAttributes.GetDouble("lastDismountTotalHours");

            if (timeSinceDismount < eatDelayAfterDismount)
                {
                double remaining = eatDelayAfterDismount - timeSinceDismount;
                infotext.AppendLine("jaunt:infotext-foodseektimer:" + " " + remaining.ToString("0.0") + " hours");   
                }
                
            if (timeSinceDismount < wanderDelayAfterDismount)
                {
                double remaining = wanderDelayAfterDismount - timeSinceDismount;
                infotext.AppendLine("jaunt:infotext-wandertimer:" + " " + remaining.ToString("0.0") + " hours");   
                }
        }
    }
}