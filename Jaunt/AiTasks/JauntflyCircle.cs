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
	//Fly in circles around a target or the current spot
	public class AiTaskJauntFlyCircle : AiTaskJauntFlightTargetable
	{
		float seekingRangeHor;
		float seekingRangeVer;
		float minRadius;
		float maxRadius;
		float height;
		float moveSpeed;
		float direction;
		float radialCorrectionStrength;
		int minCircles;
		int maxCircles;
		bool requireLineOfSight;
		bool circleCurrentPositionWhenNoTarget;
		string flyAnimation;

		Vec3d centerPos;

		//Current orbit state
		double desiredRadius;
		double desiredYPos;
		double lastAngle;
		double angleAccum;
		int targetCircles;
		int circlesDone;

		float directionChangeCooldown = 0f;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntFlyCircle(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(25f);
			seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(25f);
			minRadius = taskConfig["minRadius"].AsFloat(10f);
			maxRadius = taskConfig["maxRadius"].AsFloat(20f);
			height = taskConfig["height"].AsFloat(10f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["moveSpeed"].AsFloat(0.04f));
			direction = taskConfig["direction"].AsString("left") == "left" ? 1f : -1f;
			radialCorrectionStrength = taskConfig["radialCorrectionStrength"].AsFloat(0.2f);
			minCircles = taskConfig["minCircles"].AsInt(1);
			maxCircles = taskConfig["maxCircles"].AsInt(3);
			requireLineOfSight = taskConfig["requireLineOfSight"].AsBool(true);
			circleCurrentPositionWhenNoTarget = taskConfig["circleCurrentPositionWhenNoTarget"].AsBool(true);
			flyAnimation = taskConfig["flyAnimation"].AsString("fly");

			if (maxCircles < minCircles)
			{
				int temp = minCircles;
				minCircles = maxCircles;
				maxCircles = temp;
			}
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (Flyer.LandingRequested) return false;
			if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

			//No target is fine if the task is set to circle its current spot
			targetEntity = FindTargetEntity();

			if (targetEntity == null && !circleCurrentPositionWhenNoTarget) return false;

			return true;
		}

		public override void StartExecute()
		{
			base.StartExecute();

			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;

			Flyer.SetAiFlightAnimation(flyAnimation);

			if (targetEntity == null) targetEntity = FindTargetEntity();

			centerPos = targetEntity != null
				? targetEntity.Pos.XYZ.AddCopy(0, 0, 0)
				: entity.Pos.XYZ.AddCopy(0, 0, 0);

			//Randomize radius and circle count so repeats look less robotic
			desiredRadius = minRadius + world.Rand.NextDouble() * (maxRadius - minRadius);
			targetCircles = world.Rand.Next(minCircles, maxCircles + 1);
			circlesDone = 0;
			angleAccum = 0;
			lastAngle = GetAngleAroundCenter();
			directionChangeCooldown = 0f;

			if (targetEntity != null)
			{
				DebugLog($"[FlightDebug] Circle: Starting {targetCircles} circle(s) around entity {targetEntity.Code} (id {targetEntity.EntityId}) at {centerPos} with radius {desiredRadius:0.0}.");
			}
			else
			{
				DebugLog($"[FlightDebug] Circle: No whitelisted entity in range. Starting {targetCircles} circle(s) around current position {centerPos} with radius {desiredRadius:0.0}.");
			}

			ReadjustFlyHeight();
		}

		public override bool ContinueExecute(float dt)
		{
			if (!entity.Alive) return false;

			//Track the target every tick so the circle follows it
			UpdateCenter();

			if (targetEntity == null && !circleCurrentPositionWhenNoTarget)
			{
				DebugLog("[FlightDebug] Circle: Target lost and current-position fallback is disabled. Ending circle.");
				return false;
			}

			UpdateTargetCircles();

			//Finished the requested orbits
			//This counts toward the landing budget
			if (circlesDone >= targetCircles)
			{
				DebugLog($"[FlightDebug] Circle: Completed {circlesDone}/{targetCircles} circle(s). Handing off to other flight tasks.");
				Flyer.RegisterAirborneActivityCompleted();
				return false;
			}

			if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
			{
				ReadjustFlyHeight();
			}

			double dx = entity.Pos.X - centerPos.X;
			double dz = entity.Pos.Z - centerPos.Z;
			double currentRadius = Math.Sqrt(dx * dx + dz * dz);

			if (currentRadius < 0.001)
			{
				float randomYaw = (float)world.Rand.NextDouble() * GameMath.TWOPI;
				dx = Math.Sin(randomYaw);
				dz = Math.Cos(randomYaw);
				currentRadius = 0.001;
			}

			double nx = dx / currentRadius;
			double nz = dz / currentRadius;

			//Tangent gives the circle direction
			//Radial correction pulls us back toward the wanted radius
			double tangentX = nz * direction;
			double tangentZ = -nx * direction;

			double radialOffset = desiredRadius - currentRadius;
			double radialCorrection = GameMath.Clamp(radialOffset * radialCorrectionStrength, -0.75, 0.75);
			double walkX = tangentX + nx * radialCorrection;
			double walkZ = tangentZ + nz * radialCorrection;

			double walkLength = Math.Sqrt(walkX * walkX + walkZ * walkZ);
			if (walkLength < 0.000001)
			{
				walkX = nx;
				walkZ = nz;
				walkLength = 1.0;
			}

			walkX /= walkLength;
			walkZ /= walkLength;

			float targetYaw = (float)Math.Atan2(-walkZ, walkX) + GameMath.PIHALF + 0.1f * direction;
			entity.Pos.Yaw += GameMath.AngleRadDistance(entity.Pos.Yaw, targetYaw) * dt;

			double dy = GameMath.Clamp(desiredYPos - entity.Pos.Y, -1, 1);
			entity.Controls.WalkVector.Set(walkX, dy, walkZ);
			entity.Controls.WalkVector.Mul(moveSpeed);

			if (dy < 0) entity.Controls.WalkVector.Mul(0.5);

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

			//Collision while blind to the target means the orbit is likely blocked
			//Abort and count the effort so another task can roll
			directionChangeCooldown = Math.Max(0f, directionChangeCooldown - dt);
			if (entity.Collided && directionChangeCooldown <= 0f)
			{
				if (requireLineOfSight && !HasLineOfSightToTarget())
				{
					DebugLog("[FlightDebug] Circle: Collision detected and target is no longer visible. Aborting circle.");
					Flyer.RegisterAirborneActivityCompleted();
					return false;
				}

				//Otherwise flip direction and try the other way around
				directionChangeCooldown = 2f;
				direction *= -1f;
			}

			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			//Cancelled circles should not leave animation or movement running
			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();

				entity.Controls.WalkVector.Set(0, 0, 0);
				entity.Controls.FlyVector.Set(0, 0, 0);
			}

			entity.Pos.Roll = 0;

			base.FinishExecute(cancelled);
		}

		//Same LOS check used when the target was first picked
		//Dead or missing targets count as blocked
		private bool HasLineOfSightToTarget()
		{
			if (targetEntity == null || !targetEntity.Alive || targetEntity.Pos.Dimension != entity.Pos.Dimension) return false;
			return hasDirectContact(targetEntity, seekingRangeHor, seekingRangeVer);
		}

		private Entity FindTargetEntity()
		{
			return FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
		}

		//If the target dies or leaves the dimension keep the last center
		//The fallback flag decides if that is allowed
		private void UpdateCenter()
		{
			if (targetEntity == null) return;

			if (!targetEntity.Alive || targetEntity.Pos.Dimension != entity.Pos.Dimension)
			{
				DebugLog($"[FlightDebug] Circle: Target entity {targetEntity.Code} (id {targetEntity.EntityId}) lost. Continuing around last position {centerPos}.");
				targetEntity = null;
				return;
			}

			centerPos.Set(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z);
		}

		//Accumulate angle instead of counting positions
		//This handles circles in both directions
		private void UpdateTargetCircles()
		{
			double currentAngle = GetAngleAroundCenter();
			double angleDelta = GameMath.AngleRadDistance((float)lastAngle, (float)currentAngle);

			if (Math.Abs(angleDelta) < GameMath.PI)
			{
				angleAccum += Math.Abs(angleDelta);
			}

			lastAngle = currentAngle;

			while (angleAccum >= GameMath.TWOPI)
			{
				angleAccum -= GameMath.TWOPI;
				circlesDone++;
				DebugLog($"[FlightDebug] Circle: Completed circle {circlesDone}/{targetCircles} around {(targetEntity != null ? targetEntity.Code.ToString() : "position " + centerPos)}.");
			}
		}

		private double GetAngleAroundCenter()
		{
			return Math.Atan2(entity.Pos.Z - centerPos.Z, entity.Pos.X - centerPos.X);
		}

		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}

		//Circle height is above the local ground
		private void ReadjustFlyHeight()
		{
			desiredYPos = JauntFlightUtil.GetDesiredFlyHeight(entity, height);
		}
	}
}
