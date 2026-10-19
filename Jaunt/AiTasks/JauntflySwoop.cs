using Jaunt.Entities;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace Jaunt.AiTasks
{
	//Erel style swoop
	//It follows a path so it can curve through the target and climb back out
	public class AiTaskJauntFlySwoop : AiTaskJauntFlightTargetable
	{
		//Swoop tuning from JSON
		float seekingRangeHor;
		float seekingRangeVer;
		float moveSpeed;
		float damageRange;
		float damage;
		int damageTier;
		float knockbackStrength;
		float minVerticalDistance;
		float minHorizontalDistance;
		int pathLength;
		float swoopEntryHeight;
		float exitDistanceMultiplier;
		float exitHeightMultiplier;
		float speedThresholdForDamage;

		//Damage is checked in small ticks, not every frame
		const float DamageTickInterval = 0.2f;
		const long AttemptCooldownMs = 1500;

		bool requireLineOfSight;

		EnumDamageType damageType;

		string flyAnimation;
		string swoopAnimation;
		string escapeAnimation;
		string swoopSound;
		string impactSound;

		List<Vec3d> swoopPath;
		Vec3d beginAttackPos;

		//Each entity only gets hit once per swoop
		HashSet<long> didDamageEntity;
		float damageAccum;
		bool impactSoundPlayed;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntFlySwoop(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			seekingRangeHor = taskConfig["seekingRangeHor"].AsFloat(25f);
			seekingRangeVer = taskConfig["seekingRangeVer"].AsFloat(25f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["moveSpeed"].AsFloat(0.06f));
			damageRange = taskConfig["damageRange"].AsFloat(4f);
			damage = taskConfig["damage"].AsFloat(5f);
			damageTier = taskConfig["damageTier"].AsInt(0);
			knockbackStrength = taskConfig["knockbackStrength"].AsFloat(2f);
			minVerticalDistance = taskConfig["minVerticalDistance"].AsFloat(5f);
			minHorizontalDistance = taskConfig["minHorizontalDistance"].AsFloat(15f);
			pathLength = taskConfig["pathLength"].AsInt(25);
			swoopEntryHeight = taskConfig["swoopEntryHeight"].AsFloat(10f);
			exitDistanceMultiplier = taskConfig["exitDistanceMultiplier"].AsFloat(1.3f);
			exitHeightMultiplier = taskConfig["exitHeightMultiplier"].AsFloat(0.5f);
			speedThresholdForDamage = taskConfig["speedThresholdForDamage"].AsFloat(0.3f);
			requireLineOfSight = taskConfig["requireLineOfSight"].AsBool(true);
			damageType = Enum.Parse<EnumDamageType>(taskConfig["damageType"].AsString("SlashingAttack"), true);
			flyAnimation = taskConfig["flyAnimation"].AsString("fly");
			swoopAnimation = taskConfig["swoopAnimation"].AsString("fly-down");
			escapeAnimation = taskConfig["escapeAnimation"].AsString("fly-up");
			swoopSound = taskConfig["swoopSound"].AsString("game:sounds/creature/erel/dive");
			impactSound = taskConfig["impactSound"].AsString("game:sounds/creature/erel/slam");

			if (pathLength < 2) pathLength = 2;

			didDamageEntity = new HashSet<long>();
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (Flyer.LandingRequested) return false;
			if (entity.World.ElapsedMilliseconds < cooldownUntilMs) return false;

			//Rate limit attempts so a failed path does not retry every tick
			cooldownUntilMs = entity.World.ElapsedMilliseconds + AttemptCooldownMs;

			targetEntity = FindTargetEntity();

			if (targetEntity == null) return false;

			//The target needs to be below us and far enough away for a swoop
			if (entity.Pos.Y - targetEntity.Pos.Y <= minVerticalDistance) return false;
			if (entity.Pos.HorDistanceTo(targetEntity.Pos) <= minHorizontalDistance) return false;

			beginAttackPos = entity.Pos.XYZ.AddCopy(0, 0, 0);
			swoopPath = new List<Vec3d>(getSwoopPath(targetEntity, pathLength, false));

			//Skip obvious bad paths before committing
			return pathClear(swoopPath);
		}

		public override void StartExecute()
		{
			base.StartExecute();

			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;

			didDamageEntity.Clear();
			damageAccum = 0f;
			impactSoundPlayed = false;

			//Build the real path once and let the path traverser fly it
			swoopPath.Clear();
			swoopPath.AddRange(getSwoopPath(targetEntity, pathLength, true));
			pathTraverser.FollowRoute(swoopPath, moveSpeed, 8, null, null);

			Flyer.SetAiFlightAnimation(flyAnimation);

			PlaySound(swoopSound);
			DebugLog($"[FlightDebug] Swoop: Targeting {targetEntity.Code} (id {targetEntity.EntityId}) with {swoopPath.Count} path points.");
		}

		public override bool ContinueExecute(float dt)
		{
			if (!entity.Alive) return false;

			//Only damage while actually moving fast enough
			damageAccum += dt;
			if (damageAccum > DamageTickInterval && entity.Pos.Motion.Length() > speedThresholdForDamage)
			{
				damageAccum = 0f;
				AttackEntities();
			}

			double speed = entity.Pos.Motion.Length();
			if (speed > 0.01)
			{
				entity.Pos.Roll = (float)Math.Asin(GameMath.Clamp(-entity.Pos.Motion.Y / speed, -1, 1));
			}

			//The animation follows vertical motion
			//Down for the attack and up for the escape
			if (entity.Pos.Motion.Y < -0.005)
			{
				Flyer.SetAiFlightAnimation(swoopAnimation);
			}
			else if (entity.Pos.Motion.Y > 0.005)
			{
				Flyer.SetAiFlightAnimation(escapeAnimation);
			}
			else
			{
				Flyer.SetAiFlightAnimation(flyAnimation);
			}

			//Path done ending the activity with count
			if (!pathTraverser.Active)
			{
				Flyer.RegisterAirborneActivityCompleted();
				DebugLog("[FlightDebug] Swoop: Swoop path complete.");
				return false;
			}

			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			//Always stop the path, even on cancel
			pathTraverser.Stop();

			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();
				entity.Controls.WalkVector.Set(0, 0, 0);
				entity.Controls.FlyVector.Set(0, 0, 0);
			}
			else
			{
				Flyer.SetAiFlightAnimation(flyAnimation);
			}

			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;

			base.FinishExecute(cancelled);
		}

		//Coarse collision check along the path to skip obvious walls without doing a full path solve
		protected bool pathClear(List<Vec3d> swoopPath)
		{
			int skipPoints = 2;
			Vec3d tmppos = new Vec3d();

			for (int i = 0; i < swoopPath.Count; i += skipPoints)
			{
				tmppos.Set(swoopPath[i]);
				tmppos.Y--;

				if (world.CollisionTester.IsColliding(entity.World.BlockAccessor, entity.CollisionBox, tmppos))
				{
					return false;
				}
			}

			return true;
		}

		//Two phase path to first drop toward the target then climb away from the attack
		protected Vec3d[] getSwoopPath(Entity target, int its, bool simplifiedOut)
		{
			EntityPos targetEntityPos = target.Pos.Copy();
			EntityPos thisEntityPos = entity.Pos.Copy();

			Vec3d targetPos = targetEntityPos.XYZ.AddCopy(target.LocalEyePos);

			Vec3d start1 = entity.Pos.XYZ;
			Vec3d end1 = new Vec3d(start1.X, targetPos.Y + swoopEntryHeight, start1.Z);

			Vec3d start2 = end1;
			Vec3d end2 = targetPos;

			Vec3d delta1 = end1 - start1;
			Vec3d delta2 = end2 - start2;

			int outits = simplifiedOut ? its / 3 : its;
			Vec3d[] points = new Vec3d[its + outits];

			for (int i = 0; i < its; i++)
			{
				double p = (double)i / its;

				Vec3d mid1 = start1 + p * delta1;
				Vec3d mid2 = start2 + p * delta2;

				points[i] = (1 - p) * mid1 + p * mid2;
			}

			start1 = points[its - 1];
			Vec3d offs = (targetEntityPos.XYZ - thisEntityPos.XYZ) * 1;
			end1 = new Vec3d(targetPos.X + offs.X, targetPos.Y, targetPos.Z + offs.Z);

			start2 = end1;
			end2 = new Vec3d(targetPos.X + offs.X * exitDistanceMultiplier, targetPos.Y + (beginAttackPos.Y - targetPos.Y) * exitHeightMultiplier, targetPos.Z + offs.Z * exitDistanceMultiplier);

			delta1 = end1 - start1;
			delta2 = end2 - start2;

			for (int i = 0; i < outits; i++)
			{
				double p = (double)i / outits;

				Vec3d mid1 = start1 + p * delta1;
				Vec3d mid2 = start2 + p * delta2;

				points[its + i] = (1 - p) * mid1 + p * mid2;
			}

			return points;
		}

		//Hit each valid entity once per swoop
		protected void AttackEntities()
		{
			List<Entity> attackableEntities = new List<Entity>();
			EntityPartitioning ep = entity.Api.ModLoader.GetModSystem<EntityPartitioning>();

			ep.GetNearestEntity(entity.Pos.XYZ, damageRange + 1, (e) =>
			{
				if (e == null || e == entity || !e.Alive) return false;
				if (e.Code == null || !IsTargetableEntity(e, damageRange)) return false;
				if (didDamageEntity.Contains(e.EntityId)) return false;
				if (!hasDirectContact(e, damageRange, damageRange)) return false;

				attackableEntities.Add(e);
				return false;
			}, EnumEntitySearchType.Creatures);

			foreach (Entity attackEntity in attackableEntities)
			{
				attackEntity.ReceiveDamage(
					new DamageSource
					{
						Source = EnumDamageSource.Entity,
						SourceEntity = entity,
						Type = damageType,
						DamageTier = damageTier,
						KnockbackStrength = knockbackStrength
					},
					damage * GlobalConstants.CreatureDamageModifier
				);

				if (entity is IMeleeAttackListener imal)
				{
					imal.DidAttack(attackEntity);
				}

				didDamageEntity.Add(attackEntity.EntityId);
				DebugLog($"[FlightDebug] Swoop: Hit {attackEntity.Code} (id {attackEntity.EntityId}) for {damage:0.#} damage.");

				if (!impactSoundPlayed)
				{
					impactSoundPlayed = true;
					PlaySound(impactSound);
				}
			}
		}

		//Nearest valid swoop target
		private Entity FindTargetEntity()
		{
			return FindNearestTarget(seekingRangeHor, seekingRangeVer, requireLineOfSight);
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
