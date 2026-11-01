using Jaunt.Entities;
using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace Jaunt.AiTasks
{
	//Direct dive attack
	//The aim point locks at close range so the target does not pull us around
	public class AiTaskJauntFlyDive : AiTaskJauntFlightTargetable
	{
		//Close range lock, this is when the dive commits
		const double FinalApproachDistance = 8.0;

		//Braking uses total 3D distance so horizontal and vertical slow together
		const double BrakeDistance = 5.0;
		const double MinApproachSpeedFactor = 0.2;

		//Handoff to landing
		const double HandoffDistance = 1.5;
		const double LowAltitudeHandoffDistance = 2.5;
		const double HandoffHeight = 1.0;
		const double ImpactHeightAboveTerrain = 1.0;

		//A small lead is enough, a big lead makes moving targets harder to hit
		const double MaxTargetLead = 2.5;
		const float TurnResponse = 2.5f;

		//Stuck and timeout checks
		const double StuckTimeoutMs = 1000;
		const double StuckMovementThreshold = 0.05;
		const double MaxDiveDurationMs = 20000;

		//Dive tuning from JSON
		float seekingRangeHor;
		float seekingRangeVer;
		float moveSpeed;
		float diveChance;
		long failedDiveCooldownMs;

		bool requireLineOfSight;

		//Final approach state
		bool finalApproach;
		double closestApproachDistance;
		long diveStartMs;
		long lastProgressMs;
		Vec3d lastProgressPos = new Vec3d();

		string diveAnimation;
		string liftAnimation;
		string hoverAnimation;
		string diveSound;
		double extraHeightAboveTarget;

		Vec3d targetPos;
		Vec3d finalTargetPos = new Vec3d();

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntFlyDive(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(25f);
			seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(25f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["moveSpeed"].AsFloat(0.06f));
			diveChance = GameMath.Clamp(taskConfig["diveChance"].AsFloat(0.5f), 0f, 1f);
			failedDiveCooldownMs = taskConfig["failedDiveCooldownMs"].AsInt(5000);
			requireLineOfSight = taskConfig["requireLineOfSight"].AsBool(true);
			diveAnimation = taskConfig["diveAnimation"].AsString("fly-down");
			liftAnimation = taskConfig["liftAnimation"].AsString("fly-up");
			hoverAnimation = taskConfig["hoverAnimation"].AsString("hover");
			diveSound = taskConfig["diveSound"].AsString("game:sounds/creature/erel/dive");
			extraHeightAboveTarget = taskConfig["extraHeightAboveTarget"].AsDouble(1);
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (Flyer.LandingRequested) return false;

			//Do not dive right after a landing site was rejected
			//This gives other tasks time to move us somewhere better
			if (entity.World.ElapsedMilliseconds < Flyer.LandingRejectedUntilMs) return false;
			if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

			targetEntity = FindTargetEntity();

			if (targetEntity == null) return false;

			//A failed roll gives the target a short break
			//This keeps the bird from diving at every single target instantly
			if (world.Rand.NextDouble() > diveChance)
			{
				cooldownUntilMs = entity.World.ElapsedMilliseconds + failedDiveCooldownMs;
				DebugLog($"[FlightDebug] Dive: Detected {targetEntity.Code} (id {targetEntity.EntityId}) but failed the {diveChance:0.##} dive chance.");
				return false;
			}

			return true;
		}

		public override void StartExecute()
		{
			base.StartExecute();

			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;

			//Start with the target where it is now
			targetPos = targetEntity != null
				? targetEntity.Pos.XYZ.AddCopy(0, 0, 0)
				: entity.Pos.XYZ.AddCopy(0, 0, 0);

			//Reset the dive state
			finalApproach = false;
			closestApproachDistance = double.MaxValue;
			diveStartMs = entity.World.ElapsedMilliseconds;
			lastProgressMs = diveStartMs;
			lastProgressPos.Set(entity.Pos.XYZ);

			if (targetEntity.Pos.Y > entity.Pos.Y)
				{
				diveAnimation = liftAnimation;
				}

			Flyer.SetAiFlightAnimation(diveAnimation);

			PlaySound(diveSound);

			DebugLog($"[FlightDebug] Dive: Diving toward {targetEntity?.Code} (id {targetEntity?.EntityId}).");
		}

		public override bool ContinueExecute(float dt)
		{
			if (!entity.Alive) return false;

			UpdateTargetPos();

			//Lock the aim point when we get close
			//After this the target does not drag the dive around
			if (!finalApproach)
			{
				double approachDist = HorizontalDistanceTo(targetPos.X, targetPos.Z);
				if (approachDist <= FinalApproachDistance)
				{
					BeginFinalApproach(approachDist);
				}
			}

			Vec3d aimPos = finalApproach ? finalTargetPos : targetPos;

			double impactY = 0;
			if (targetEntity != null)
			{
				impactY = targetEntity.Pos.Y + ImpactHeightAboveTerrain;
			}
			else 
			{
				int groundAtAim = entity.World.BlockAccessor.GetTerrainMapheightAt(aimPos.AsBlockPos);
				impactY = groundAtAim + ImpactHeightAboveTerrain;
			}

			double dx = aimPos.X - entity.Pos.X;
			double dz = aimPos.Z - entity.Pos.Z;
			double horizontalDist = Math.Sqrt(dx * dx + dz * dz);
			double dy = impactY - entity.Pos.Y;

			if (finalApproach && horizontalDist < closestApproachDistance)
			{
				closestApproachDistance = horizontalDist;
			}

			//Near the handoff switch to the hover clip
			bool nearHandoff = horizontalDist <= HandoffDistance + 1.0 && Math.Abs(dy) <= HandoffHeight + 1.0;
			Flyer.SetAiFlightAnimation(nearHandoff ? hoverAnimation : diveAnimation);

			bool lowEnough = entity.Pos.Y <= impactY + HandoffHeight;

			//Low and close, let Landing finish the drop
			if (lowEnough && horizontalDist <= LowAltitudeHandoffDistance)
			{
				return ForceLanding($"reached low altitude at {horizontalDist:0.0} blocks from the aim point.");
			}

			//A real collision means the dive line is bad
			//Force a landing instead of trying the same thing again
			if (entity.Collided)
			{
				return ForceLanding("collided during the dive.");
			}

			//Do not turn around and fly backwards if we pass the aim point
			if (finalApproach && closestApproachDistance <= HandoffDistance * 1.5 && horizontalDist > closestApproachDistance + 0.1)
			{
				return ForceLanding($"passed the final approach point at {horizontalDist:0.0} blocks.");
			}

			long now = entity.World.ElapsedMilliseconds;
			double movedX = entity.Pos.X - lastProgressPos.X;
			double movedY = entity.Pos.Y - lastProgressPos.Y;
			double movedZ = entity.Pos.Z - lastProgressPos.Z;
			double movedSq = movedX * movedX + movedY * movedY + movedZ * movedZ;

			//Stuck check uses real movement, not distance to the target
			//A moving target can hide a creature that is not moving at all
			if (movedSq > StuckMovementThreshold * StuckMovementThreshold)
			{
				lastProgressPos.Set(entity.Pos.XYZ);
				lastProgressMs = now;
			}
			else if (now - lastProgressMs > StuckTimeoutMs)
			{
				return ForceLanding($"made no positional progress for {(now - lastProgressMs) / 1000.0:0.0}s.");
			}

			//Hard cap so a bad dive always ends
			if (now - diveStartMs > MaxDiveDurationMs)
			{
				return ForceLanding($"timed out after {MaxDiveDurationMs / 1000.0:0.0}s.");
			}

			//Normalize the full 3D direction
			//Horizontal and vertical then slow down together near the aim point
			double distanceToAim = Math.Sqrt(dx * dx + dy * dy + dz * dz);
			double nx = 0;
			double ny = 0;
			double nz = 0;

			if (distanceToAim > 0.001)
			{
				nx = dx / distanceToAim;
				ny = dy / distanceToAim;
				nz = dz / distanceToAim;
			}

			if (horizontalDist > 0.001)
			{
				float targetYaw = (float)Math.Atan2(dx, dz);
				entity.Pos.Yaw += GameMath.AngleRadDistance(entity.Pos.Yaw, targetYaw) * Math.Min(1f, TurnResponse * dt);
			}

			//Brake over the last part of the 3D distance
			//This avoids the old problem where only horizontal speed slowed down
			double brakeT = GameMath.Clamp((distanceToAim - HandoffDistance) / BrakeDistance, 0.0, 1.0);
			double speedFactor = MinApproachSpeedFactor + (1.0 - MinApproachSpeedFactor) * brakeT * brakeT;

			entity.Controls.WalkVector.Set(nx * speedFactor, ny * speedFactor, nz * speedFactor);
			entity.Controls.WalkVector.Mul(moveSpeed);

			//Shallow water should not trap the dive
			if (entity.Swimming)
			{
				entity.Controls.WalkVector.Y = 2 * moveSpeed;
				entity.Controls.FlyVector.Y = 2 * moveSpeed;
			}

			double speed = entity.Pos.Motion.Length();
			if (speed > 0.01)
			{
				entity.Pos.Roll = (float)Math.Asin(GameMath.Clamp(-entity.Pos.Motion.Y / speed, -1, 1));
			}

			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			//Cancelled dives clear movement so the next task starts clean
			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();
				entity.Controls.WalkVector.Set(0, 0, 0);
				entity.Controls.FlyVector.Set(0, 0, 0);
			}

			base.FinishExecute(cancelled);
		}

		//Lead a moving target a little then freeze the aim point
		private void BeginFinalApproach(double horizontalDist)
		{
			finalApproach = true;
			closestApproachDistance = horizontalDist;

			double entitySpeed = HorizontalSpeed(entity.Pos.Motion);
			double ticksToTarget = GameMath.Clamp(horizontalDist / Math.Max(entitySpeed, 0.05), 0, 20);
			double leadX = 0;
			double leadZ = 0;

			if (targetEntity != null)
			{
				leadX = targetEntity.Pos.Motion.X * ticksToTarget * 0.5;
				leadZ = targetEntity.Pos.Motion.Z * ticksToTarget * 0.5;

				double leadLength = Math.Sqrt(leadX * leadX + leadZ * leadZ);
				if (leadLength > MaxTargetLead)
				{
					double scale = MaxTargetLead / leadLength;
					leadX *= scale;
					leadZ *= scale;
				}
			}

			finalTargetPos.Set(targetPos.X + leadX, targetPos.Y, targetPos.Z + leadZ);
			lastProgressMs = entity.World.ElapsedMilliseconds;
			lastProgressPos.Set(entity.Pos.XYZ);

			DebugLog($"[FlightDebug] Dive: Final approach locked at {horizontalDist:0.0} blocks (lead {leadX:0.0}, {leadZ:0.0}).");
		}

		//Every failure path goes here
		//Request landing and stop horizontal drift
		private bool ForceLanding(string reason)
		{
			DebugLog($"[FlightDebug] Dive: {reason} Forcing landing.");
			Flyer.RequestLanding();
			entity.Controls.WalkVector.Set(0, 0, 0);
			entity.Controls.FlyVector.Set(0, 0, 0);
			entity.Pos.Motion.X = 0;
			entity.Pos.Motion.Z = 0;
			return false;
		}

		//Small math helper for the final approach
		private double HorizontalDistanceTo(double x, double z)
		{
			double dx = x - entity.Pos.X;
			double dz = z - entity.Pos.Z;
			return Math.Sqrt(dx * dx + dz * dz);
		}

		private static double HorizontalSpeed(Vec3d motion)
		{
			return Math.Sqrt(motion.X * motion.X + motion.Z * motion.Z);
		}

		//Pick the nearest valid prey
		private Entity FindTargetEntity()
		{
			return FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
		}

		//Keep the live target position updated
		//The final aim point is separate and stays locked
		private void UpdateTargetPos()
		{
			if (targetEntity == null) return;

			if (!targetEntity.Alive || targetEntity.Pos.Dimension != entity.Pos.Dimension)
			{
				targetEntity = null;
				return;
			}

			targetPos.Set(targetEntity.Pos.X, targetEntity.Pos.Y + extraHeightAboveTarget, targetEntity.Pos.Z);
		}

		private void PlaySound(string location)
		{
			if (string.IsNullOrEmpty(location)) return;
			entity.World.PlaySoundAt(new AssetLocation(location), entity);
		}

		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}
	}
}
