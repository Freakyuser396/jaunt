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
    public class EntityBehaviorJauntArmor : EntityBehavior
    {
        public EntityBehaviorJauntArmor(Entity entity) : base(entity) { }
        public override string PropertyName() => "jaunt:armor";

        private AssetLocation plateSound;

         public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            
            plateSound = new AssetLocation("game", "sounds/wearable/plate1");

            base.Initialize(properties, attributes);
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {

        }

        private void onAttachmentToggled()
        {
            entity.MarkShapeModified();
            if (entity.World.Side == EnumAppSide.Server)
                {
                    entity.World.PlaySoundAt(plateSound, entity, null, true, 16);
                }
            entity.WatchedAttributes.MarkAllDirty();
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            ITreeAttribute tree = entity.WatchedAttributes.GetTreeAttribute("ownedby");
            if (tree != null)
                {        
                string OwnerId = tree.GetString("uid");
                var aggressor = damageSource.CauseEntity ?? damageSource.SourceEntity;
                if (aggressor is EntityPlayer player && (player.PlayerUID == OwnerId))
                    {
                        damage = 0;
                        damageSource.CauseEntity = null;
                        damageSource.SourceEntity = null;
                    }
                }
            var behaviorHealth = entity.GetBehavior<EntityBehaviorHealth>();
            if (behaviorHealth?.Health < 0)
            {
                behaviorHealth.Health=GameMath.Clamp(behaviorHealth.Health, 0, behaviorHealth.MaxHealth);
            }
            var ebr = entity?.GetBehavior<EntityBehaviorRideableAccessories>();
            if (damageSource.SourceEntity != null && damageSource.Type != EnumDamageType.Heal && ebr != null)
            {
            int index = 0;
                foreach (var slot in ebr.Inventory)
                {
                    if (!slot.Empty)
                    {
                            if (slot.Itemstack.Collectible.Attributes["isMountArmor"].AsBool())
                                {          
                                    float durabilityLeft = slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack);
                                    int durabilityLeftInt = slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack);
                                    float maxDurability = slot.Itemstack.Collectible.GetMaxDurability(slot.Itemstack);
                                    float damageReduction = slot.Itemstack.Collectible.Attributes["damageReduction"].AsFloat(0);

                                    float durabilityPercent = maxDurability > 0 ? durabilityLeft / maxDurability : 0f;
                                    float effectiveReduction = damageReduction * durabilityPercent;
                                    

                                    int rawdamageInt = (int)Math.Ceiling(damage);
                                    damage *= 1 - effectiveReduction;
                                    int damagetakenInt = (int)Math.Ceiling(damage);
                                    int damageInt = rawdamageInt - damagetakenInt;
                                    int newdurability = durabilityLeftInt - damageInt;

                                    slot.Itemstack.Collectible.SetDurability(slot.Itemstack, newdurability);
                                    onAttachmentToggled();
                                    slot.MarkDirty();
                                    ebr.Inventory.MarkSlotDirty(index);
                                }
                    }
                     
                    index++;                         
                }
            }
        }
    }
}