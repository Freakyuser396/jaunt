using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.API.Common.Entities;
using Jaunt.Entities;

namespace Jaunt;

[HarmonyPatch(typeof(GuiDialogCreatureContents), "IsInRangeOfBlock")]
public static class HarmonyGuiDialogCreatureContentsPatch
{
    public static bool Prefix(ref bool __result, Entity ___owningEntity, ICoreClientAPI ___capi)
    {
        if (___owningEntity is not EntityFlyingAgent largeEntity )
        {
            return true;
        }

        var playerEntity = ___capi?.World?.Player?.Entity;
        if (playerEntity == null)
        {
            __result = true;
            return false;
        }

        float range = ___capi.World.Player.WorldData?.PickingRange ?? 5f;
        Vec3d eyePos = playerEntity.Pos.XYZ.Add(playerEntity.LocalEyePos);

        __result = largeEntity.InRangeOf(eyePos, range * range, range);
        return false;
    }
}
