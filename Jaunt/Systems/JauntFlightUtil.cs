using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace Jaunt
{
	public static class JauntFlightUtil
	{
		//Health gates are a ratio of max health from 0 to 1, could probably find a better place for this but its handy...
		public static bool IsHealthWithinGate(EntityAgent entity, float minHealth, float maxHealth)
		{
			if (minHealth <= 0f && maxHealth >= 1f) return true;

			EntityBehaviorHealth health = entity.GetBehavior<EntityBehaviorHealth>();
			if (health == null || health.MaxHealth <= 0f) return true;

			float healthRatio = health.Health / health.MaxHealth;
			return healthRatio >= minHealth && healthRatio <= maxHealth;
		}

		//Keep the target height above any liquid surface
		public static double GetDesiredFlyHeight(EntityAgent entity, double heightAboveGround)
		{
			int terrainYPos = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);
			int tries = 10;
			while (tries-- > 0)
			{
				Block block = entity.World.BlockAccessor.GetBlockRaw((int)entity.Pos.X, terrainYPos, (int)entity.Pos.Z, BlockLayersAccess.Fluid);
				if (block.IsLiquid())
				{
					terrainYPos++;
				}
				else
				{
					break;
				}
			}

			return terrainYPos + heightAboveGround;
		}
	}
}
