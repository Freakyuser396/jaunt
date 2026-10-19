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
	//General airborne roaming
	//Handles waypoints, gliding and the home leash
	public class AiTaskJauntflyWander : AiTaskBase, IJauntFlightTask
	{
		//Home and waypoint settings from JSON
		bool stayNearHome;
		float radius;
		float homeRange;
		float homeMaxRange;
		float minDistance;
		protected float moveSpeed;
		float currentMoveSpeed;
		bool returningHome;

		public Vec3d locationPos;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		protected double desiredYPos;
		float locationTimer;
		int locationCount;
		float locationCountAvg;
		float locationCountVar;
		NatFloat maxLocationCount;
		int locationsVisited;

		bool isGliding;

		//Collision climb state
		//This keeps us flying instead of landing on a hill
		float collisionCooldown;
		float collisionClearTimer;
		double collisionClimbBoost;

		//Stuck escape state
		//Repeated collisions with no movement mean the waypoint is behind a wall
		Vec3d lastCollisionPos = new Vec3d();
		bool hasLastCollisionPos;
		int stuckCollisionCount;
		float homeDetourTimer;

		float minlocationInterval;
		float maxlocationInterval;

		float height;
		string flyAnimation;
		string glideAnimation;

		float locationInterval;

		public AiTaskJauntflyWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			stayNearHome = taskConfig["stayNearHome"].AsBool(taskConfig["stayNearSpawn"].AsBool(false));
			radius = taskConfig["radius"].AsFloat(50f);
			homeRange = taskConfig["homeRange"].AsFloat(radius);
			homeMaxRange = taskConfig["homeMaxRange"].AsFloat(homeRange * 2f);
			minDistance = taskConfig["minDistance"].AsFloat(50f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["currentMoveSpeed"].AsFloat(0.04f));
			minlocationInterval = taskConfig["minlocationInterval"].AsFloat(8f);
			maxlocationInterval = taskConfig["maxlocationInterval"].AsFloat(15f);
			height = taskConfig["height"].AsFloat(0f);
			flyAnimation = taskConfig["flyAnimation"].AsString("fly");
			glideAnimation = taskConfig["glideAnimation"].AsString("glide");
			locationCountAvg = taskConfig["locationCountAvg"].AsFloat(4.5f);
			locationCountVar = taskConfig["locationCountVar"].AsFloat(1.5f);

			if (homeRange <= 0f) homeRange = radius;
			if (homeMaxRange > 0f && homeMaxRange < homeRange) homeMaxRange = homeRange;
		}

		//Wander only runs after takeoff has finished
		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (Flyer.LandingRequested) return false;
			if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

			return true;
		}

		public override void StartExecute()
		{
			base.StartExecute();
			//Fresh wander cycle
			currentMoveSpeed = moveSpeed;
			isGliding = false;
			collisionCooldown = 0f;
			collisionClearTimer = 0f;
			collisionClimbBoost = 0;
			lastCollisionPos.Set(entity.Pos.XYZ);
			hasLastCollisionPos = false;
			stuckCollisionCount = 0;
			homeDetourTimer = 0f;
			entity.Pos.Roll = 0;

			Flyer.SetAiFlightAnimation(flyAnimation);

			if (Flyer.DesiredFlyHeight <= 0)
			{
				Flyer.DesiredFlyHeight = 20.0;
			}

			ReadjustFlyHeight();

			maxLocationCount = NatFloat.create(EnumDistribution.UNIFORM, locationCountAvg, locationCountVar);
			locationCount = (int)maxLocationCount.nextFloat();
			locationsVisited = 0;
			locationInterval = RandomlocationInterval();
			locationTimer = locationInterval;
			returningHome = stayNearHome && Flyer.IsBeyondSpawn(homeMaxRange);
			locationPos = returningHome ? Flyer.GetSpawnPosCopy() : Chooselocation();
		}

		public override bool ContinueExecute(float dt)
		{
			if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
			{
				ReadjustFlyHeight();
			}

			homeDetourTimer = Math.Max(0f, homeDetourTimer - dt);
			UpdateHomeReturn();

			/*if (entity.CollidedHorizontally)
			{
				Flyer.RequestLanding();
				entity.Controls.WalkVector.Set(0, 0, 0);
				entity.Controls.FlyVector.Set(0, 0, 0);
				return false;
			}*/

			collisionCooldown = Math.Max(0f, collisionCooldown - dt);

			//The extra climb is temporary it clears after a few seconds without a collision
			if (collisionClearTimer > 0f)
			{
				collisionClearTimer -= dt;
				if (collisionClearTimer <= 0f)
				{
					collisionClimbBoost = 0;
				}
			}

			//Collision handling climbs a little more each hit and pick a new spot
			if (collisionCooldown <= 0f && entity.CollidedHorizontally)
			{
				collisionCooldown = 2f;
				collisionClearTimer = 3f;
				collisionClimbBoost = Math.Min(24.0, collisionClimbBoost + 6.0);

				isGliding = false;
				currentMoveSpeed = moveSpeed;
				Flyer.SetAiFlightAnimation(flyAnimation);

				//Repeated collisions with almost no movement mean we are stuck
				double movedX = entity.Pos.X - lastCollisionPos.X;
				double movedZ = entity.Pos.Z - lastCollisionPos.Z;
				double movedSq = movedX * movedX + movedZ * movedZ;

				if (hasLastCollisionPos && movedSq < 1.0)
				{
					stuckCollisionCount++;
				}
				else
				{
					stuckCollisionCount = 0;
				}

				lastCollisionPos.Set(entity.Pos.XYZ);
				hasLastCollisionPos = true;

				if (stuckCollisionCount >= 2)
				{
					//Flip around and make a waypoint behind us
					//The normal random reroute can keep choosing the same wall
					collisionCooldown = 3f;
					stuckCollisionCount = 0;
					entity.Pos.Yaw += GameMath.PI;
					ChooseEscapeWaypoint();
					homeDetourTimer = 4f;

					DebugLog($"[FlightDebug] Wander: Repeated collisions with no progress; flipping heading and climbing {collisionClimbBoost:0.#} blocks.");
				}
				else
				{
					//Only reroute when not heading home
					if (!returningHome)
					{
						locationPos = Chooselocation();
						locationInterval = RandomlocationInterval();
						locationTimer = locationInterval;
					}

					DebugLog($"[FlightDebug] Wander: Collision detected; climbing {collisionClimbBoost:0.#} blocks and choosing a new waypoint.");
				}
			}

			double dy = desiredYPos + collisionClimbBoost - entity.Pos.Y;
			double yMot = GameMath.Clamp(dy, -1, 1);

			//Turn toward the waypoint and move forward
			double dx = locationPos.X - entity.Pos.X;
			double dz = locationPos.Z - entity.Pos.Z;

			float locationYaw = (float)Math.Atan2(dx, dz);

			entity.Pos.Yaw += GameMath.AngleRadDistance(entity.Pos.Yaw, locationYaw) * dt / 7;

			double cosYaw = Math.Cos(entity.Pos.Yaw);
			double sinYaw = Math.Sin(entity.Pos.Yaw);

			entity.Controls.WalkVector.Set(sinYaw, yMot, cosYaw);
			entity.Controls.WalkVector.Mul(currentMoveSpeed);

			if (yMot < 0) entity.Controls.WalkVector.Mul(0.5);

			if (entity.Swimming)
			{
				entity.Controls.WalkVector.Y = 2 * currentMoveSpeed;
				entity.Controls.FlyVector.Y = 2 * currentMoveSpeed;
			}

			locationTimer -= dt;

			if (!returningHome && locationTimer <= 0)
			{
				if (locationsVisited < locationCount)
				{
					locationPos = Chooselocation();
					locationsVisited++;
					locationInterval = RandomlocationInterval();
					locationTimer = locationInterval;

					if (isGliding)
					{
						Flyer.SetAiFlightAnimation(flyAnimation);
						isGliding = false;
						currentMoveSpeed = moveSpeed;
					}
				}
				else
				{
					Flyer.RegisterAirborneActivityCompleted();
					return false;
				}
			}

			//Glide near the end of a waypoint leg
			//This also saves a little speed before the next turn
			if ( locationTimer <= locationInterval * 0.2f && !isGliding )
			{
				Flyer.SetAiFlightAnimation(glideAnimation);
				isGliding = true;
				currentMoveSpeed = moveSpeed / 2;
			}

			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			//Cancelled tasks should not leave a flight clip running
			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();
			}

			base.FinishExecute(cancelled);
		}

		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}

		protected void ReadjustFlyHeight()
		{
			double flyHeight = height > 0f ? height : Flyer.DesiredFlyHeight;
			if (flyHeight <= 0) return;
			desiredYPos = JauntFlightUtil.GetDesiredFlyHeight(entity, flyHeight);
		}

		float RandomlocationInterval()
		{
			return minlocationInterval
				+ (float)world.Rand.NextDouble() * (maxlocationInterval - minlocationInterval);
		}

		//Soft home leash
		//Fly home when we get too far away
		private void UpdateHomeReturn()
		{
			if (!stayNearHome)
			{
				returningHome = false;
				return;
			}

			double distSq = Flyer.HorizontalSquareDistanceToSpawn(entity.Pos.XYZ);

			if (homeMaxRange > 0f && distSq > homeMaxRange * homeMaxRange)
			{
				returningHome = true;
			}
			else if (returningHome && distSq <= homeRange * homeRange)
			{
				returningHome = false;
				locationTimer = 0f;
			}

			if (returningHome && homeDetourTimer <= 0f)
			{
				locationPos = Flyer.GetSpawnPosCopy();
			}
		}

		/*private Vec3d Chooselocation()
		{
			var origin = stayNearSpawn ? SpawnPos : entity.Pos.XYZ;

			double rndx=0;
			double rndz=0;
			for (int i = 0; i < 10; i++)
			{
				float yaw = (float)world.Rand.NextDouble() * GameMath.TWOPI;
				rndx = radius * Math.Sin(yaw);
				rndz = radius * Math.Cos(yaw);

				if (origin.AddCopy(rndx, 0, rndz).HorizontalSquareDistanceTo(entity.Pos.XYZ) > minDistance) break;
			}

			return origin.AddCopy(rndx, 0, rndz);
		}
*/
		//Puts a waypoint in front of the new heading
		//Flips yaw first when it wants to back away from a wall
		private void ChooseEscapeWaypoint()
		{
			float escapeDistance = Math.Max(20f, Math.Min(radius, 40f));
			double escapeX = Math.Sin(entity.Pos.Yaw) * escapeDistance;
			double escapeZ = Math.Cos(entity.Pos.Yaw) * escapeDistance;

			locationPos.Set(entity.Pos.X + escapeX, entity.Pos.Y, entity.Pos.Z + escapeZ);
			locationInterval = RandomlocationInterval();
			locationTimer = locationInterval;
		}

		private Vec3d Chooselocation()
		{
			Vec3d origin;
			float searchRadius;

			if (stayNearHome)
			{
				origin = Flyer.GetSpawnPosCopy();
				searchRadius = Math.Min(radius, homeRange);
			}
			else
			{
				origin = entity.Pos.XYZ;
				searchRadius = radius;
			}

			double rndx = 0;
			double rndz = 0;
			for (int i = 0; i < 10; i++)
			{
				float yaw = (float)world.Rand.NextDouble() * GameMath.TWOPI;
				rndx = searchRadius * Math.Sin(yaw);
				rndz = searchRadius * Math.Cos(yaw);

				if (origin.AddCopy(rndx, 0, rndz).HorizontalSquareDistanceTo(entity.Pos.XYZ) > minDistance * minDistance) break;
			}

			return origin.AddCopy(rndx, 0, rndz);
		}

	}
}
