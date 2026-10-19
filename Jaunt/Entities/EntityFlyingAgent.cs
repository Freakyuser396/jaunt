using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace Jaunt.Entities;

//The flight state every flying task shares
//Only one phase owns movement at a time
public enum JauntFlightPhase
{
	Grounded,
	TakingOff,
	Airborne,
	Landing
}

public class EntityFlyingAgent : EntityJauntAgent
{
	public override bool CanStepPitch => false;
	public override bool CanSwivel => true;
	public override bool CanSwivelNow => true;
	public override double FrustumSphereRadius => base.FrustumSphereRadius * 5;
	public override bool InRangeOf(Vec3d position, float horRangeSq, float vertRange)
	{

		return base.InRangeOf(position, horRangeSq + 128, vertRange);

	}

	public bool IsInAiFlight = false;
		public override bool ApplyGravity
		{
			get
			{
				return !IsInAiFlight;
			}
		}

	//Shared flight state tasks set the request flags and the phase decides what can run
	public JauntFlightPhase FlightPhase = JauntFlightPhase.Grounded;
	public bool TakeoffRequested;
	public bool LandingRequested;
	public long LandingRejectedUntilMs;
	public double DesiredFlyHeight = 20.0;
	public Vec3d SpawnPos;//Home base used by Wander

	//Landing budget
	//Every completed airborne activity counts toward the next landing
	public int AirborneActivitiesCompleted;
	public int AirborneActivitiesBeforeLanding;

	//The animation owner so tasks do not fight over the same clips
	public string CurrentAiFlightAnimation { get; private set; }

	public bool AirborneActivityBudgetReached => AirborneActivitiesBeforeLanding > 0 && AirborneActivitiesCompleted >= AirborneActivitiesBeforeLanding;


	public void RequestTakeoff()
	{
		if (FlightPhase == JauntFlightPhase.Grounded)
		{
			TakeoffRequested = true;
		}
	}

	public void RequestLanding()
	{
		if (FlightPhase == JauntFlightPhase.Airborne)
		{
			LandingRequested = true;
		}
	}

	//Switch phases and keep the gravity flag in sync
	public void BeginFlightPhase(JauntFlightPhase phase)
	{
		FlightPhase = phase;
		IsInAiFlight = phase != JauntFlightPhase.Grounded;
	}

	//Clean slate after landing or a failed launch
	public void ResetFlightState()
	{
		FlightPhase = JauntFlightPhase.Grounded;
		IsInAiFlight = false;
		TakeoffRequested = false;
		LandingRequested = false;
		ResetAirborneActivityBudget();
	}

	//One animation owner for all flight tasks to stop tasks from stopping each other's clips by accident
	public void SetAiFlightAnimation(string code)
	{
		if (string.IsNullOrEmpty(code))
		{
			StopAiFlightAnimation();
			return;
		}

		if (CurrentAiFlightAnimation == code && AnimManager != null && AnimManager.IsAnimationActive(code))
		{
			return;
		}

		StopAiFlightAnimation();
		StartAnimation(code);
		CurrentAiFlightAnimation = code;
	}

	public void StopAiFlightAnimation()
	{
		if (string.IsNullOrEmpty(CurrentAiFlightAnimation)) return;

		StopAnimation(CurrentAiFlightAnimation);
		CurrentAiFlightAnimation = null;
	}

	//Roll the landing budget once per flight cycle
	//The task that lands supplies its own min and max
	public void EnsureAirborneActivityBudget(int min, int max)
	{
		if (AirborneActivitiesBeforeLanding > 0) return;

		if (max < min)
		{
			int temp = min;
			min = max;
			max = temp;
		}

		if (min < 1) min = 1;
		if (max < 1) max = 1;

		AirborneActivitiesBeforeLanding = World.Rand.Next(min, max + 1);
		DebugFlight($"Landing budget rolled: {AirborneActivitiesBeforeLanding} airborne activities ({min}-{max}).");
	}

	public void RegisterAirborneActivityCompleted()
	{
		AirborneActivitiesCompleted++;
		DebugFlight($"Airborne activity completed: {AirborneActivitiesCompleted}/{AirborneActivitiesBeforeLanding}.");

		if (AirborneActivityBudgetReached)
		{
			DebugFlight("Landing budget reached.");
		}
	}

	//Fresh budget after a real touchdown
	public void ResetAirborneActivityBudget()
	{
		if (AirborneActivitiesCompleted > 0 || AirborneActivitiesBeforeLanding > 0)
		{
			DebugFlight($"Landing budget reset after {AirborneActivitiesCompleted}/{AirborneActivitiesBeforeLanding} airborne activities.");
		}

		AirborneActivitiesCompleted = 0;
		AirborneActivitiesBeforeLanding = 0;
	}

	private void DebugFlight(string message)
	{
		if (JauntModSystem.Instance?.DebugMode != true) return;
		Api.Logger.Debug($"[FlightDebug] {message}");
	}

	//Saved through the old spawnPos attributes to keep existing saves working without a new data key
	public void loadOrCreateSpawnPos()
	{
		if (WatchedAttributes.HasAttribute("spawnPosX"))
		{
			SpawnPos = new Vec3d(
				WatchedAttributes.GetDouble("spawnPosX"),
				WatchedAttributes.GetDouble("spawnPosY"),
				WatchedAttributes.GetDouble("spawnPosZ")
			);
		}
		else
		{
			SetSpawnPos(Pos.XYZ);
		}
	}

	public void SetSpawnPos(Vec3d pos)
	{
		SpawnPos = pos.AddCopy(0, 0, 0);
		WatchedAttributes.SetDouble("spawnPosX", SpawnPos.X);
		WatchedAttributes.SetDouble("spawnPosY", SpawnPos.Y);
		WatchedAttributes.SetDouble("spawnPosZ", SpawnPos.Z);
	}

	public Vec3d GetSpawnPosCopy()
	{
		EnsureSpawnPos();
		return SpawnPos.AddCopy(0, 0, 0);
	}

	public double HorizontalSquareDistanceToSpawn(Vec3d pos)
	{
		EnsureSpawnPos();
		return SpawnPos.HorizontalSquareDistanceTo(pos);
	}

	public bool IsBeyondSpawn(float range)
	{
		if (range <= 0f) return false;
		return HorizontalSquareDistanceToSpawn(Pos.XYZ) > range * range;
	}

	private void EnsureSpawnPos()
	{
		if (SpawnPos == null)
		{
			loadOrCreateSpawnPos();
		}
	}

	public override void AfterInitialized(bool onFirstSpawn)
	{
		base.AfterInitialized(onFirstSpawn);

		if (World.Side != EnumAppSide.Server) return;

		var taskManager = GetBehavior<EntityBehaviorTaskAI>()?.TaskManager;
		if (taskManager == null) return;

		//Flight tasks are always allowed
		//Everything else needs a grounded creature that is actually on the ground
		taskManager.OnShouldExecuteTask += TaskManager_OnShouldExecuteTask;
	}

	private bool TaskManager_OnShouldExecuteTask(IAiTask task)
	{
		if (task is IJauntFlightTask) return true;
		if (FlightPhase != JauntFlightPhase.Grounded) return false;
		if (!OnGround) return false;

		return true;
	}

	public override void OnEntitySpawn()
	{
		base.OnEntitySpawn();
		loadOrCreateSpawnPos();
	}

	public override void OnEntityLoaded()
	{
		base.OnEntityLoaded();
		loadOrCreateSpawnPos();
	}

}
