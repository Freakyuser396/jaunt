using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Server;
using System.Text;

namespace Jaunt.Behaviors
{
    public class CollectibleBehaviorJauntAttachments : CollectibleBehavior,IAttachedInteractions, IAttachedListener
    {
        public ICoreAPI _api;
        public static ILogger Logger { get; private set; }
        public CollectibleBehaviorJauntAttachments(CollectibleObject collObj) : base(collObj)
            {

            }
        
        private AssetLocation interactSound;
        string armorerror;
        string blanketerror;   
        public override void OnLoaded(ICoreAPI api)
            {
                base.OnLoaded(api);
                _api = api;
                
                var sound = collObj.Attributes?["useSound"]?.AsString();
                    if (!string.IsNullOrEmpty(sound))
                    {
                        interactSound = new AssetLocation(sound);
                    }
                    else interactSound = new AssetLocation("game", "sounds/effect/writing1");

                
                armorerror = Lang.Get("jaunt:armorerror");
                blanketerror = Lang.Get("jaunt:blanketerror");    

            }
        public bool OnTryAttach(ItemSlot itemslot, int slotIndex, Entity toEntity)
		{
            var inv = toEntity.GetBehavior<EntityBehaviorRideableAccessories>();
                foreach (var slot in inv.Inventory)
                {
                if (!slot.Empty)
                    {
                    if (slot.Itemstack.Collectible.Attributes["isMountArmor"].AsBool() && itemslot.Itemstack.Collectible.Attributes["isBlanket"].AsBool())
                        {
                        (_api as ICoreClientAPI)?.TriggerIngameError("itemslot", "jaunt", blanketerror);
                            return false;
                        }
                        else if (slot.Itemstack.Collectible.Attributes["isBlanket"].AsBool() && itemslot.Itemstack.Collectible.Attributes["isMountArmor"].AsBool())
                        {
                        (_api as ICoreClientAPI)?.TriggerIngameError("itemslot", "jaunt", armorerror);
                            return false;
                        }
                    }
                                           
                }
                return true;
		}
        public void OnAttached(ItemSlot itemslot, int slotIndex, Entity toEntity, EntityAgent byEntity)
        {
            if (itemslot.Itemstack.ItemAttributes["useSound"]?.Exists == true )
            {

          if (_api.Side == EnumAppSide.Server)
            {
                byEntity.World.PlaySoundAt(new AssetLocation(interactSound), byEntity);
            }
            }
        }
        public void OnDetached(ItemSlot bagSlot, int slotIndex, Entity fromEntity, EntityAgent byEntity)
        {
            if (bagSlot.Itemstack.ItemAttributes["useSound"]?.Exists == true )
            {

          if (_api.Side == EnumAppSide.Server)
            {
                byEntity.World.PlaySoundAt(new AssetLocation(interactSound), byEntity);
            }
            }
        }
    public void OnReceivedClientPacket(ItemSlot bagSlot, int slotIndex, Entity onEntity, IServerPlayer player, int packetid, byte[] data, ref EnumHandling handled, Action onRequireSave)
        {
        }
    public bool OnTryDetach(ItemSlot itemslot, int slotIndex, Entity fromEntity)
        {
            return true;
        }
    public void OnEntityDeath(ItemSlot itemslot, int slotIndex, Entity onEntity, DamageSource damageSourceForDeath)
        {
        }
    public void OnEntityDespawn(ItemSlot itemslot, int slotIndex, Entity onEntity, EntityDespawnData despawn)
		{
		}
    public virtual void OnInteract(ItemSlot bagSlot, int slotIndex, Entity onEntity, EntityAgent byEntity, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled, Action onRequireSave)
        {
        }
    public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            float damageReduction = inSlot.Itemstack.Collectible.Attributes["damageReduction"].AsFloat(0);
                if (damageReduction > 0)
                {           
                    float durabilityLeft = inSlot.Itemstack.Collectible.GetRemainingDurability(inSlot.Itemstack);
                    float maxDurability = inSlot.Itemstack.Collectible.GetMaxDurability(inSlot.Itemstack);
                    float durabilityPercent = maxDurability > 0 ? durabilityLeft / maxDurability : 0f;
                    float effectiveReduction = damageReduction * durabilityPercent;
                    int damageReductionInPercent = (int)Math.Round(effectiveReduction * 100);
                    dsc.AppendLine(Lang.Get("jaunt-percent-protection", damageReductionInPercent));
                }

            float speedmodifier = inSlot.Itemstack.Collectible.Attributes["speedmodifier"].AsFloat(0);
                if (speedmodifier > 0)
                {
                    int effectiveSpeedModifier = (int)Math.Round((speedmodifier - 1) * 100);
                    dsc.AppendLine(Lang.Get("jaunt-walkspeed", effectiveSpeedModifier));
                }
        }
    }
}