
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using System.Threading.Tasks;

#nullable disable

namespace Jaunt.Behaviors
{

    public class EntityBehaviorJauntWalkspeed : EntityBehavior
    {

        public float ExtraSpeedModifier = 1f;
        public float speedmodifier = 0f;


        public EntityBehaviorJauntWalkspeed(Entity entity) : base(entity) { }
        public override string PropertyName() => "equusdestrier:walkspeed";

         public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode, ref EnumHandling handled)
        {
                    base.OnInteract(byEntity, itemslot, hitPosition, mode, ref handled);
            _ = CheckAttachments();
        }

        private async Task CheckAttachments()
        {
            var ebr = entity.GetBehavior<EntityBehaviorRideableAccessories>();
            await Task.Delay(100);
            ExtraSpeedModifier = 1f;
            float totalSpeedBonus = 0f;
            foreach (var slot in ebr.Inventory)
                {
                    if (!slot.Empty && slot.Itemstack.Collectible.Attributes["speedmodifier"].AsFloat(0) != 0)
                    {
                        float smod = slot.Itemstack.Collectible.Attributes["speedmodifier"].AsFloat(0);
                        totalSpeedBonus += smod -1;
                    }
                }
            ExtraSpeedModifier += totalSpeedBonus;
        }
    }
}