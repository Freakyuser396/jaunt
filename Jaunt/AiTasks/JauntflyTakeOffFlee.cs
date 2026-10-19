using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#nullable disable

namespace Jaunt.AiTasks
{
	//Emergency launch away from a threat
	//The normal takeoff still does all the real launch work
	public class AiTaskJauntTakeOffFlee : AiTaskJauntTakeOff
	{
		//Threat lookup settings from JSON
		float seekingRangeHor;
		float seekingRangeVer;
		bool requireLineOfSight;

		public AiTaskJauntTakeOffFlee(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(32f);
			seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(24f);
			requireLineOfSight = taskConfig["requireLineOfSight"].AsBool(true);
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;

			//This keeps the normal takeoff from starting when there is nothing to flee
			targetEntity = FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
			if (targetEntity == null) return false;

			//Let the base task handle health, clearance, timeout and animations
			return base.ShouldExecute();
		}

		public override void StartExecute()
		{
			if (targetEntity != null)
			{
				double awayX = entity.Pos.X - targetEntity.Pos.X;
				double awayZ = entity.Pos.Z - targetEntity.Pos.Z;

				//Setting the yaw to run away the base takeoff keeps this direction
				if (awayX * awayX + awayZ * awayZ > 0.000001)
				{
					entity.Pos.Yaw = (float)Math.Atan2(awayX, awayZ);
				}
			}

			base.StartExecute();
		}
	}
}
