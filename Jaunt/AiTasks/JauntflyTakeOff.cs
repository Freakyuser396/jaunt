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
	//Normal vertical takeoff with an optional forward push
	//The flee takeoff is a thin subclass of this task
	public class AiTaskJauntTakeOff : AiTaskJauntFlightTargetable
	{
		//Takeoff tuning from JSON
		float takeoffHeight;
		float moveSpeed;
		float forwardSpeed;
		float clearanceHeight;
		float minHealth;
		float maxHealth;

		//Safety checks so a blocked takeoff cannot run forever
		float takeoffTimeout;
		float stuckYTimeout;
		bool abortToLanding;
		protected double desiredYPos;

		float takeoffElapsed;
		float yStuckTimer;
		double lastY;
		long lastClearanceLogMs;

		bool flyUpAnimationStarted;
		bool completed;

		string takeoffAnimation;
		string ascendAnimation;
		string cruiseAnimation;

		private EntityFlyingAgent Flyer => entity as EntityFlyingAgent;

		public AiTaskJauntTakeOff(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
		{
			takeoffHeight = taskConfig["takeoffHeight"].AsFloat(20f);
			moveSpeed = taskConfig["movespeed"].AsFloat(taskConfig["currentMoveSpeed"].AsFloat(0.04f));
			forwardSpeed = taskConfig["forwardSpeed"].AsFloat(0f);
			clearanceHeight = taskConfig["clearanceHeight"].AsFloat(8f);
			minHealth = GameMath.Clamp(taskConfig["minHealth"].AsFloat(0f), 0f, 1f);
			maxHealth = GameMath.Clamp(taskConfig["maxHealth"].AsFloat(1f), 0f, 1f);
			takeoffTimeout = taskConfig["takeoffTimeout"].AsFloat(15f);
			stuckYTimeout = taskConfig["stuckYTimeout"].AsFloat(3f);
			abortToLanding = taskConfig["abortToLanding"].AsBool(true);
			takeoffAnimation = taskConfig["takeoffAnimation"].AsString("takeoff");
			ascendAnimation = taskConfig["ascendAnimation"].AsString("fly-up");
			cruiseAnimation = taskConfig["cruiseAnimation"].AsString("fly");
		}

		public override bool ShouldExecute()
		{
			if (entity.World.Side != EnumAppSide.Server) return false;
            if (IsOnCooldown()) return false;
			if (!PreconditionsSatisfied()) return false;
			if (Flyer == null) return false;

			//Health gate can keep an injured creature grounded
			if (!JauntFlightUtil.IsHealthWithinGate(entity, minHealth, maxHealth)) return false;
			if (Flyer.FlightPhase != JauntFlightPhase.Grounded) return false;
			if (!entity.OnGround) return false;

			//Straight up check, only solid blocks stop the launch
			if (!HasTakeoffClearance(out BlockSelection hit))
			{
				LogClearanceBlocked(hit);
				return false;
			}

			//Takeoff asks for itself
			if (!Flyer.TakeoffRequested)
			{
				Flyer.RequestTakeoff();
			}

			return true;
		}

		public override void StartExecute()
		{
			base.StartExecute();

			//TakingOff owns movement until the target height is reached
			Flyer.BeginFlightPhase(JauntFlightPhase.TakingOff);
			Flyer.LandingRequested = false;
			Flyer.DesiredFlyHeight = takeoffHeight;
			ReadjustFlyHeight();

			takeoffElapsed = 0f;
			yStuckTimer = 0f;
			lastY = entity.Pos.Y;
			flyUpAnimationStarted = false;
			completed = false;

			DebugLog($"[FlightDebug] Takeoff clearance confirmed for {clearanceHeight:0.#} blocks. Starting at Y {entity.Pos.Y:0.0}, target Y {desiredYPos:0.0}.");
			Flyer.SetAiFlightAnimation(takeoffAnimation);
		}

		public override bool ContinueExecute(float dt)
		{
			takeoffElapsed += dt;

			//If Y movement stops for too long something above is blocking us
			if (Math.Abs(entity.Pos.Y - lastY) < 0.00001)
			{
				yStuckTimer += dt;
			}
			else
			{
				yStuckTimer = 0f;
				lastY = entity.Pos.Y;
			}

			if (takeoffElapsed >= takeoffTimeout)
			{
				AbortTakeoff($"takeoff timeout after {takeoffElapsed:0.0}s at Y {entity.Pos.Y:0.0}, target Y {desiredYPos:0.0}");
				return false;
			}

			if (yStuckTimer >= stuckYTimeout)
			{
				AbortTakeoff($"no Y change for {yStuckTimer:0.0}s at Y {entity.Pos.Y:0.0}, target Y {desiredYPos:0.0}");
				return false;
			}

			if (entity.OnGround || entity.World.Rand.NextDouble() < 0.03)
			{
				ReadjustFlyHeight();
			}

			double dy = desiredYPos - entity.Pos.Y;
			double yMot = GameMath.Clamp(dy, -1, 1);
			int groundY = entity.World.BlockAccessor.GetTerrainMapheightAt(entity.Pos.AsBlockPos);

			//Forward speed uses the current yaw
			double cosYaw = Math.Cos(entity.Pos.Yaw);
			double sinYaw = Math.Sin(entity.Pos.Yaw);

			entity.Controls.WalkVector.Set(sinYaw * forwardSpeed, yMot, cosYaw * forwardSpeed);
			entity.Controls.WalkVector.Mul(moveSpeed);

			//Shallow water should not trap the launch, this is to account for slow down it causes
			if (entity.Swimming)
			{
				entity.Controls.WalkVector.Y = 2 * moveSpeed;
				entity.Controls.FlyVector.Y = 2 * moveSpeed;
			}

			//Switch to the climb animation once clearly off the ground
			if (!flyUpAnimationStarted && entity.Pos.Y - groundY >= 2)
			{
				Flyer.SetAiFlightAnimation(ascendAnimation);
				flyUpAnimationStarted = true;
			}

			//Reached the target height, let the airborne tasks take over
			if (dy <= 0.1)
			{
				Flyer.SetAiFlightAnimation(cruiseAnimation);

				DebugLog($"[FlightDebug] Takeoff complete at Y {entity.Pos.Y:0.0}. Handing off to airborne tasks.");
				Flyer.TakeoffRequested = false;
				Flyer.BeginFlightPhase(JauntFlightPhase.Airborne);
				completed = true;
				return false;
			}
			return true;
		}

		public override void FinishExecute(bool cancelled)
		{
			//Failed launches go back to a clean grounded state
			if (!completed)
			{
				Flyer.StopAiFlightAnimation();
				Flyer.ResetFlightState();
			}

			base.FinishExecute(cancelled);
		}

		//Terrain height is refreshed so hills or water change the target
		protected void ReadjustFlyHeight()
		{
			desiredYPos = JauntFlightUtil.GetDesiredFlyHeight(entity, Flyer.DesiredFlyHeight);
		}

		//Straight up check, only blocks with collision count
		private bool HasTakeoffClearance(out BlockSelection hit)
		{
			hit = null;

			Vec3d from = entity.Pos.XYZ.AddCopy(0, 0.1, 0);
			Vec3d to = from.AddCopy(0, clearanceHeight, 0);

			BlockSelection blockSelection = null;
			EntitySelection entitySelection = null;

			entity.World.RayTraceForSelection(
				from,
				to,
				ref blockSelection,
				ref entitySelection,
				(pos, block) => block != null && block.Id != 0 && block.GetCollisionBoxes(entity.World.BlockAccessor, pos)?.Length > 0,
				other => false
			);

			hit = blockSelection;
			return blockSelection == null;
		}

		//Stopping in the air is worse than landing
		//So try a landing handoff first if configured
		private void AbortTakeoff(string reason)
		{
			Flyer.StopAiFlightAnimation();

			entity.Controls.WalkVector.Set(0, 0, 0);
			entity.Controls.FlyVector.Set(0, 0, 0);
			entity.Pos.Motion.X = 0;
			entity.Pos.Motion.Y = 0;
			entity.Pos.Motion.Z = 0;

			if (abortToLanding && !entity.OnGround)
			{
				DebugLog($"[FlightDebug] Takeoff aborted: {reason}. Handing off to landing.");
				Flyer.BeginFlightPhase(JauntFlightPhase.Airborne);
				Flyer.LandingRequested = true;
			}
			else
			{
				DebugLog($"[FlightDebug] Takeoff aborted: {reason}. Cancelling and staying grounded.");
				Flyer.ResetFlightState();
			}

			completed = true;
		}

		//const bool DebugMode = true;
		private bool DebugMode => JauntModSystem.Instance?.DebugMode == true;

		//This check runs every AI tick while blocked, log is throttled for less debug spam
		private void LogClearanceBlocked(BlockSelection hit)
		{
			if (!DebugMode) return;

			long now = entity.World.ElapsedMilliseconds;
			if (lastClearanceLogMs != 0 && now - lastClearanceLogMs < 2000) return;
			lastClearanceLogMs = now;

			string blockCode = hit?.Block?.Code?.ToString() ?? "unknown block";
			string blockPos = hit?.Position?.ToString() ?? "unknown position";
			DebugLog($"[FlightDebug] Takeoff blocked: {blockCode} at {blockPos} within {clearanceHeight:0.#} blocks above.");
		}

		private void DebugLog(string message)
		{
			if (!DebugMode) return;
			entity.Api.Logger.Debug(message);
		}

	}
}
