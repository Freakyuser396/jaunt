using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Entities;

public class EntityFlyingAgent : EntityJauntAgent
{
    public override bool CanStepPitch => false;
    public override bool CanSwivel => true;
    public override bool CanSwivelNow => true;
    public override double FrustumSphereRadius => base.FrustumSphereRadius * 5;
    public override bool InRangeOf(Vec3d position, float horRangeSq, float vertRange)
    {

        return base.InRangeOf(position, horRangeSq + 128, vertRange);

    }

    public bool IsInAiFlight = false;
        public override bool ApplyGravity
        {
            get
            {
                return !IsInAiFlight;
            }
        }

        static EntityFlyingAgent() {
            AiTaskRegistry.Register<AiTaskJauntflyWander>("jaunt:flywander");
        }


}