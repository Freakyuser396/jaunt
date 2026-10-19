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
	//Simple airborne flee
	//Pick a spot away from the threat and keep moving until safe
	public class AiTaskJauntFlyFlee : AiTaskJauntFlightTargetable
	{
		//Refresh the flee point now and then as the threat moves
		const float FleeRefreshInterval = 0.5f;

		//Flee tuning from JSON
		float seekingRangeHor;
		float seekingRangeVer;
		float fleeDistance;
		float safeDistance;
		float safeTime;
		float moveSpeed;
		float height;
		bool requireLineOfSight;

		string flyAnimation;

		Vec3d fleePos = new Vec3d();
		double desiredYPos;

		//Flee state and collision climb
		float fleeRefreshTimer;
		float safeTimer;
		float collisionCooldown;
		float collisionClearTimer;
		double collisionClimbBoost;
		int collisionTurnSign = 1;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntFlyFlee(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(32f);
			seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(24f);
			fleeDistance = taskConfig["fleeDistance"].AsFloat(40f);
			safeDistance = taskConfig["safeDistance"].AsFloat(30f);
			safeTime = taskConfig["safeTime"].AsFloat(2f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["moveSpeed"].AsFloat(0.08f));
			height = taskConfig["height"].AsFloat(0f);
			requireLineOfSight = taskConfig["requireLineOfSight"].AsBool(true);
			flyAnimation = taskConfig["flyAnimation"].AsString("fly");
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;

			//Emotion gate
			if (!PreconditionsSatisfied()) return false;

			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (Flyer.LandingRequested) return false;
			if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

			targetEntity = FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
			return targetEntity != null;
		}

		public override void StartExecute()
		{
			base.StartExecute();

			//Pick the flee point immediately so the first tick already moves away
			targetEntity = FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);

			Flyer.SetAiFlightAnimation(flyAnimation);
			ReadjustFlyHeight();

			fleeRefreshTimer = 0f;
			safeTimer = 0f;
			collisionCooldown = 0f;
			collisionClearTimer = 0f;
			collisionClimbBoost = 0;
			collisionTurnSign = world.Rand.NextDouble() < 0.5 ? -1 : 1;

			ChooseFleePoint(false);

			DebugLog($"[FlightDebug] Flee: Fleeing from {targetEntity?.Code} (id {targetEntity?.EntityId}) toward {fleePos}.");
		}

		public override bool ContinueExecute(float dt)
		{
			if (!entity.Alive) return false;

			//If another task requested landing let it take over
			if (Flyer.LandingRequested) return false;

			if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
			{
				ReadjustFlyHeight();
			}

			//Look for a replacement threat if the current one is gone
			UpdateThreat();

			if (targetEntity == null)
			{
				Flyer.RegisterAirborneActivityCompleted();
				DebugLog("[FlightDebug] Flee: No threat remains. Ending flee.");
				return false;
			}

			double threatDx = entity.Pos.X - targetEntity.Pos.X;
			double threatDz = entity.Pos.Z - targetEntity.Pos.Z;
			double threatDist = Math.Sqrt(threatDx * threatDx + threatDz * threatDz);

			//Staying outside safe distance for a while counts as escaped
			if (threatDist > safeDistance)
			{
				safeTimer += dt;
				if (safeTimer >= safeTime)
				{
					Flyer.RegisterAirborneActivityCompleted();
					DebugLog($"[FlightDebug] Flee: Reached safe distance ({threatDist:0.0}). Ending flee.");
					return false;
				}
			}
			else
			{
				safeTimer = 0f;
			}

			//Refresh the flee point as the threat moves
			fleeRefreshTimer -= dt;
			if (fleeRefreshTimer <= 0f)
			{
				ChooseFleePoint(false);
				fleeRefreshTimer = FleeRefreshInterval;
			}

			collisionCooldown = Math.Max(0f, collisionCooldown - dt);

			//The extra climb clears after a few seconds without a collision
			if (collisionClearTimer > 0f)
			{
				collisionClearTimer -= dt;
				if (collisionClearTimer <= 0f)
				{
					collisionClimbBoost = 0;
				}
			}

			//Same collision idea as Wander
			//Climb a little more each hit and turn the flee point away
			if (collisionCooldown <= 0f && entity.CollidedHorizontally)
			{
				collisionCooldown = 2f;
				collisionClearTimer = 3f;
				collisionClimbBoost = Math.Min(24.0, collisionClimbBoost + 6.0);
				collisionTurnSign *= -1;

				ChooseFleePoint(true);
				fleeRefreshTimer = FleeRefreshInterval;

				DebugLog($"[FlightDebug] Flee: Collision detected; climbing {collisionClimbBoost:0.#} blocks and turning away.");
			}

			double dy = desiredYPos + collisionClimbBoost - entity.Pos.Y;
			double yMot = GameMath.Clamp(dy, -1, 1);

			//Move toward the flee point, not the threat
			double dx = fleePos.X - entity.Pos.X;
			double dz = fleePos.Z - entity.Pos.Z;

			float fleeYaw = (float)Math.Atan2(dx, dz);
			entity.Pos.Yaw += GameMath.AngleRadDistance(entity.Pos.Yaw, fleeYaw) * Math.Min(1f, 2.5f * dt);

			double cosYaw = Math.Cos(entity.Pos.Yaw);
			double sinYaw = Math.Sin(entity.Pos.Yaw);

			entity.Controls.WalkVector.Set(sinYaw, yMot, cosYaw);
			entity.Controls.WalkVector.Mul(moveSpeed);

			if (yMot < 0) entity.Controls.WalkVector.Mul(0.5);

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
			//Cancelled flee should not leave a flight clip running
			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();
				entity.Controls.WalkVector.Set(0, 0, 0);
				entity.Controls.FlyVector.Set(0, 0, 0);
			}

			entity.Pos.Roll = 0;

			base.FinishExecute(cancelled);
		}

		//Only look again when the current threat is gone
		//This avoids flip flopping between targets every tick
		private void UpdateThreat()
		{
			if (targetEntity == null || !targetEntity.Alive || targetEntity.Pos.Dimension != entity.Pos.Dimension)
			{
				targetEntity = FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
			}
		}

		//Away vector runs from the threat to us
		//Collision adds a turn so we slide around obstacles instead of into them
		private void ChooseFleePoint(bool collisionTurn)
		{
			double awayX;
			double awayZ;

			if (targetEntity != null)
			{
				awayX = entity.Pos.X - targetEntity.Pos.X;
				awayZ = entity.Pos.Z - targetEntity.Pos.Z;
			}
			else
			{
				awayX = Math.Sin(entity.Pos.Yaw);
				awayZ = Math.Cos(entity.Pos.Yaw);
			}

			double length = Math.Sqrt(awayX * awayX + awayZ * awayZ);
			double angle = length > 0.001
				? Math.Atan2(awayX, awayZ)
				: world.Rand.NextDouble() * GameMath.TWOPI;

			if (collisionTurn)
			{
				angle += collisionTurnSign * GameMath.PI / 3;
			}

			double fleeX = Math.Sin(angle) * fleeDistance;
			double fleeZ = Math.Cos(angle) * fleeDistance;

			fleePos.Set(entity.Pos.X + fleeX, entity.Pos.Y, entity.Pos.Z + fleeZ);
		}

		//Stay above the local ground, not above the threat
		private void ReadjustFlyHeight()
		{
			double flyHeight = height > 0f ? height : Flyer.DesiredFlyHeight;
			if (flyHeight <= 0) return;
			desiredYPos = JauntFlightUtil.GetDesiredFlyHeight(entity, flyHeight);
		}

		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}
	}
}
