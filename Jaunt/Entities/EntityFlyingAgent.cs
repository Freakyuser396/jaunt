using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
}