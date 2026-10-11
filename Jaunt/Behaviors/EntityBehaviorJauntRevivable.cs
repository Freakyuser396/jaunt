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


#nullable disable

namespace Jaunt.Behaviors
{
    public class EntityBehaviorJauntRevivable : EntityBehavior
    {
        public EntityBehaviorJauntRevivable(Entity entity) : base(entity) { }
        public override string PropertyName() => "jaunt:revivable";

        ItemStack reviveStack;
        public float reviveHealth = 1f;
        public string reviveSound = null;

         public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            var jstack = attributes["reviveStack"].AsObject<JsonItemStack>();
            reviveHealth = attributes["reviveHealth"].AsFloat(0.1f);
            reviveSound = attributes["reviveSound"].AsString("sounds/effect/portal");
                if (jstack != null)
                    {
                    jstack.Resolve(entity.Api.World, string.Format("entity {0} jaunt revive stack", entity.Code), true);
                    reviveStack = jstack.ResolvedItemstack; 
                    }
            base.Initialize(properties, attributes);
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
            if (!byEntity.Controls.ShiftKey) return;
            if (itemslot.Empty && reviveStack != null ) return;
            if (!itemslot.Empty && reviveStack == null ) return;

            var ebh = entity.GetBehavior<EntityBehaviorHealth>();
            var ebm = entity?.GetBehavior<EntityBehaviorMortallyWoundable>();

            bool healable = ebh != null && ebh.Health < ebh.MaxHealth;

            if (healable && (reviveStack == null || reviveStack != null && itemslot.Itemstack.Satisfies(reviveStack)))
                {
                if (entity.World.Side == EnumAppSide.Server)
                    {
                    entity.World.PlaySoundAt(new AssetLocation(reviveSound), entity, null, true, 16);
                    }
                if (reviveStack != null)
                    {
                        itemslot.TakeOut(1);
                        itemslot.MarkDirty();
                    }
                if (!entity.Alive) entity.Revive();
                entity.AnimManager?.StopAnimation("dead");
                if (ebm != null) recover();
                ebh.Health = ebh.MaxHealth * Math.Clamp(reviveHealth,0f,1f);
                }
        }
        private void recover()
        {
            var ebm = entity?.GetBehavior<EntityBehaviorMortallyWoundable>();
            ebm.HealthState = EnumEntityHealthState.Recovering;
            entity.WatchedAttributes.SetFloat("regenSpeed", 1);
            entity.AnimManager?.StopAnimation("wounded-idle");
            entity.AnimManager?.StopAnimation("wounded-resthead");
            entity.AnimManager?.StartAnimation("wounded-stand");
            entity.AnimManager?.StartAnimation("idle");
            var tasks = entity.GetBehavior<EntityBehaviorTaskAI>()?.TaskManager?.AllTasks;
            if (tasks != null)
            {
                foreach (var task in tasks)
                {
                    (task as AiTaskBaseTargetable)?.ClearAttacker();
                }
            }
            entity.GetBehavior<EntityBehaviorEmotionStates>()?.ClearStates();
        }
        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player, ref EnumHandling handled)
        {
            if (entity.Alive) return null;

            if (!entity.Alive && reviveStack != null) return [
                new WorldInteraction() {
                    ActionLangCode = "entityinteraction-revive",
                    Itemstacks = [reviveStack],
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                }
            ];
            return [
                new WorldInteraction() {
                    ActionLangCode = "entityinteraction-revive",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right
                }
            ];
        }
    }
}