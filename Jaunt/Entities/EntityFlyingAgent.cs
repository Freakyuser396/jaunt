using Vintagestory.API.Common;

namespace Jaunt.Entities;

public class EntityFlyingAgent : EntityJauntAgent
{
    public override bool CanStepPitch => false;
    public override bool CanSwivel => true;
    public override bool CanSwivelNow => true;
}