using HarmonyLib;
using Jaunt.Behaviors;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace Jaunt
{
[HarmonyPatch(typeof(EntityAgent))]
[HarmonyPatch("GetWalkSpeedMultiplier")]
    public static class WalkSpeedMultiplierPatch
    {
        public static void Postfix(EntityAgent __instance, ref double __result)
        {
            var ebw = __instance.GetBehavior<EntityBehaviorJauntWalkspeed>();

            if (ebw == null) return;

            __result *= ebw.ExtraSpeedModifier;
        }
    }
}