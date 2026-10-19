using Jaunt.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

#nullable disable

namespace Jaunt.AiTasks
{
	//Shared base for flight tasks that pick a target using vanilla base targetable
	public abstract class AiTaskJauntFlightTargetable : AiTaskBaseTargetable, IJauntFlightTask
	{
		protected AiTaskJauntFlightTargetable(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
		}

		//Nearest valid target from the vanilla target filters. Line of sight is optional
		protected Entity FindNearestTarget(float horRange, float verRange, bool requireLineOfSight)
		{
			Entity nearest = null;
			double nearestDistSq = (double)horRange * horRange;

			world.GetEntitiesAround(entity.Pos.XYZ, horRange, verRange, (Entity potential) =>
			{
				if (potential == null || potential.Code == null) return false;
				if (!IsTargetableEntity(potential, horRange)) return false;
				if (requireLineOfSight && !hasDirectContact(potential, horRange, verRange)) return false;

				double distSq = entity.Pos.SquareDistanceTo(potential.Pos);
				if (distSq < nearestDistSq)
				{
					nearest = potential;
					nearestDistSq = distSq;
				}

				return false;
			});

			return nearest;
		}
	}
}
