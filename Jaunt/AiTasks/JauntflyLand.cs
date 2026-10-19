using Jaunt.Entities;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

#nullable disable

namespace Jaunt.AiTasks
{
	//Landing task checks handles the descent and resets flight state on touchdown
	public class AiTaskJauntFlyLand : AiTaskBase, IJauntFlightTask
	{
		//How far down to look for a landing surface
		const double LandingCheckDistance = 64.0;

		//Give the flyer time to move before trying another spot
		const long LandingRejectionCooldownMs = 5000;

		//Landing tuning from JSON
		float moveSpeed;
		float landingTolerance;
		float descentSpeed;
		float forwardSpeed;
		float minHealth;
		float maxHealth;
		int minAirActivitiesBeforeLanding;
		int maxAirActivitiesBeforeLanding;
		protected double desiredYPos;

		bool flyDownAnimationStarted;

		string descendAnimation;
		string landingAnimation;
		string landingSound;

		//Rejected block materials. Empty list assumes skip the surface check entirely
		readonly HashSet<EnumBlockMaterial> rejectedMaterials = new HashSet<EnumBlockMaterial>();
		bool loggedRejectedMaterials;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntFlyLand(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["currentMoveSpeed"].AsFloat(0.04f));
			landingTolerance = taskConfig["landingTolerance"].AsFloat(3f);
			descentSpeed = taskConfig["descentSpeed"].AsFloat(4f);
			forwardSpeed = taskConfig["forwardSpeed"].AsFloat(0f);
			minHealth = GameMath.Clamp(taskConfig["minHealth"].AsFloat(0f), 0f, 1f);
			maxHealth = GameMath.Clamp(taskConfig["maxHealth"].AsFloat(1f), 0f, 1f);
			minAirActivitiesBeforeLanding = taskConfig["minAirActivitiesBeforeLanding"].AsInt(1);
			maxAirActivitiesBeforeLanding = taskConfig["maxAirActivitiesBeforeLanding"].AsInt(3);
			descendAnimation = taskConfig["descendAnimation"].AsString("fly-down");
			landingAnimation = taskConfig["landingAnimation"].AsString("land");
			landingSound = taskConfig["landingSound"].AsString("game:sounds/creature/erel/slam");

			if (taskConfig["rejectedMaterials"] != null)
			{
				foreach (var materialJson in taskConfig["rejectedMaterials"].AsArray())
				{
					string materialName = materialJson.AsString()?.Trim();
					if (string.IsNullOrEmpty(materialName)) continue;

					if (Enum.TryParse(materialName, true, out EnumBlockMaterial material))
					{
						rejectedMaterials.Add(material);
					}
					else
					{
						entity.Api.Logger.Warning($"[jaunt] Unknown rejected landing material '{materialName}'.");
					}
				}
			}
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Airborne) return false;
			if (!JauntFlightUtil.IsHealthWithinGate(entity, minHealth, maxHealth)) return false;

			EntityBehaviorHealth health = entity.GetBehavior<EntityBehaviorHealth>();

			//A max health gate also forces landing
			if (health != null
				&& maxHealth < 1f
				&& entity.World.ElapsedMilliseconds >= Flyer.LandingRejectedUntilMs
				&& !Flyer.LandingRequested)
			{
				Flyer.RequestLanding();
				DebugLog($"[FlightDebug] Landing: Health gate forcing landing ({health.Health:0}/{health.MaxHealth:0}).");
			}

			//A landing requested by another task still has to pass the site check
			if (Flyer.LandingRequested)
			{
				if (HasValidLandingSite(out _, out _, out _)) return true;
				return RejectLanding();
			}

			//Normal path, wait for the airborne activity budget
			Flyer.EnsureAirborneActivityBudget(minAirActivitiesBeforeLanding, maxAirActivitiesBeforeLanding);

			if (!Flyer.AirborneActivityBudgetReached) return false;

			if (!HasValidLandingSite(out _, out _, out _))
			{
				return RejectLanding();
			}

			Flyer.RequestLanding();
			DebugLog($"[FlightDebug] Landing: Activity budget reached ({Flyer.AirborneActivitiesCompleted}/{Flyer.AirborneActivitiesBeforeLanding}). Starting landing.");
			return true;
		}

		//Look straight down and reject bad surface materials
		private bool HasValidLandingSite(out Block block, out BlockPos pos, out EnumBlockMaterial material)
		{
			block = null;
			pos = null;
			material = default;

			//Log the parsed material list once so bad config is easy to spot
			if (!loggedRejectedMaterials)
			{
				loggedRejectedMaterials = true;
				DebugLog(rejectedMaterials.Count == 0
					? "[FlightDebug] Landing: No rejected materials configured; landing site check skipped."
					: $"[FlightDebug] Landing: Rejected materials = [{string.Join(", ", rejectedMaterials)}].");
			}

			if (rejectedMaterials.Count == 0) return true;

			var blockAccessor = entity.World.BlockAccessor;
			BlockPos entityPos = entity.Pos.AsBlockPos;
			int startY = (int)Math.Floor(entity.Pos.Y + 0.1);
			int endY = Math.Max(0, startY - (int)LandingCheckDistance);

			for (int y = startY; y >= endY; y--)
			{
				BlockPos candidate = new BlockPos(entityPos.X, y, entityPos.Z);

				//Check the fluid layer first
				//Otherwise water is easy to skip and we land in the lake
				Block fluid = blockAccessor.GetBlock(candidate, BlockLayersAccess.Fluid);
				if (fluid != null && fluid.Id != 0 && fluid.IsLiquid())
				{
					block = fluid;
					pos = candidate;
					material = fluid.GetBlockMaterial(blockAccessor, candidate);

					bool accepted = !rejectedMaterials.Contains(material);
					LogLandingDetection(block, pos, material, accepted);
					return accepted;
				}

				//Then check the solid layer
				//First surface wins because that is what we would touch down on
				Block solid = blockAccessor.GetBlock(candidate);
				if (solid != null && solid.Id != 0 )
				{
					block = solid;
					pos = candidate;
					material = solid.GetBlockMaterial(blockAccessor, candidate);

					bool accepted = !rejectedMaterials.Contains(material);
					LogLandingDetection(block, pos, material, accepted);
					return accepted;
				}
			}

			//No surface in range means no landing
			DebugLog($"[FlightDebug] Landing: No landing surface within {LandingCheckDistance:0} blocks below rejected.");
			return false;
		}

		//Useful when a creature refuses a spot that looks fine
		private void LogLandingDetection(Block block, BlockPos pos, EnumBlockMaterial material, bool accepted)
		{
			if (!DebugMode) return;
			DebugLog($"[FlightDebug] Landing: Detected {block.Code} ({material}) at {pos} {(accepted ? "accepted" : "rejected")}.");
		}

		//Clear the request and start the cooldown so we keep flying
		//Backing the completed count off by one means another activity must finish before the next try
		private bool RejectLanding()
		{
			Flyer.LandingRequested = false;
			Flyer.LandingRejectedUntilMs = entity.World.ElapsedMilliseconds + LandingRejectionCooldownMs;
			if (Flyer.AirborneActivitiesCompleted > 0)
			{
				Flyer.AirborneActivitiesCompleted--;
			}

			DebugLog($"[FlightDebug] Landing: Site rejected. Budget is now {Flyer.AirborneActivitiesCompleted}/{Flyer.AirborneActivitiesBeforeLanding}; continuing flight.");
			return false;
		}

		public override void StartExecute()
		{
			base.StartExecute();
			Flyer.BeginFlightPhase(JauntFlightPhase.Landing);
			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;
			ReadjustFlyHeight();

			flyDownAnimationStarted = false;

			//Kill any horizontal carry over from the previous task
			Flyer.StopAiFlightAnimation();
			StopHorizontalMovement();

			DebugLog($"[FlightDebug] Landing: Starting descent with forwardSpeed {forwardSpeed:0.##}.");
		}

		public override bool ContinueExecute(float dt)
		{
			if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
			{
				ReadjustFlyHeight();
			}

			double dy = desiredYPos - entity.Pos.Y;
			double yMot = GameMath.Clamp(dy, -1, 1);
			int groundY = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);

			if (!flyDownAnimationStarted && entity.Pos.Motion.Y <= -0.01)
			{
				Flyer.SetAiFlightAnimation(descendAnimation);
				flyDownAnimationStarted = true;
			}

			//No forward speed means a hard vertical landing
			//In that case stop all horizontal drift
			if (forwardSpeed <= 0f)
			{
				StopHorizontalMovement();
			}

			double cosYaw = Math.Cos(entity.Pos.Yaw);
			double sinYaw = Math.Sin(entity.Pos.Yaw);

			//Optional forward motion while descending
			entity.Controls.WalkVector.Set(sinYaw * forwardSpeed, yMot * descentSpeed, cosYaw * forwardSpeed);
			entity.Controls.WalkVector.Mul(moveSpeed);

			if (yMot < 0) entity.Controls.WalkVector.Y *= 0.5f;

			//Shallow water is ignored speed wise
			if (entity.Swimming)
			{
				entity.Controls.WalkVector.Y = 2 * moveSpeed;
				entity.Controls.FlyVector.Y = 2 * moveSpeed;
			}

			if (entity.OnGround || entity.Pos.Y - groundY <= landingTolerance)
			{
				TouchDown();
				return false;
			}

			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			if (cancelled)
			{
				Flyer.StopAiFlightAnimation();
			}

			//If landing did not complete, hand the air back to the flight tasks
			if (Flyer != null && Flyer.FlightPhase == JauntFlightPhase.Landing)
			{
				Flyer.BeginFlightPhase(JauntFlightPhase.Airborne);
			}

			base.FinishExecute(cancelled);
		}

		//Used by vertical landings and the dive handoff
		//This is what keeps us from sliding along the ground
		private void StopHorizontalMovement()
		{
			entity.Controls.WalkVector.X = 0;
			entity.Controls.WalkVector.Z = 0;
			entity.Controls.FlyVector.X = 0;
			entity.Controls.FlyVector.Z = 0;
			entity.Pos.Motion.X = 0;
			entity.Pos.Motion.Z = 0;
		}

		//Landing height is the local ground, no extra hover height
		protected void ReadjustFlyHeight()
		{
			desiredYPos = JauntFlightUtil.GetDesiredFlyHeight(entity, 0);
		}

		//Snap to rest and reset the phase so the next takeoff starts clean
		private void TouchDown()
		{
			Flyer.SetAiFlightAnimation(landingAnimation);
			PlaySound(landingSound);

			entity.Controls.WalkVector.Set(0, 0, 0);
			entity.Controls.FlyVector.Set(0, 0, 0);
			entity.Pos.Motion.X = 0;
			entity.Pos.Motion.Y = 0;
			entity.Pos.Motion.Z = 0;
			entity.Pos.Pitch = 0;
			entity.Pos.Roll = 0;

			Flyer.ResetFlightState();
		}

		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}

		private void PlaySound(string location)
		{
			if (string.IsNullOrEmpty(location)) return;
			entity.World.PlaySoundAt(new AssetLocation(location), entity);
		}
	}
}
