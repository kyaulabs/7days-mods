using System;
using System.Reflection;
using HarmonyLib;
using InControl;
using UnityEngine;

/// <summary>
/// Vehicle Cruise Control - recreated from the ground up for 7 Days to Die 3.1
/// Original mod by w00kie n00kie (The Winchester collection).
///
/// Behavior (faithful to the original):
///  - Press Q while riding a vehicle to cycle: Off -> Slow -> Sprint -> Off
///  - While active, forward thrust is applied automatically every frame
///  - Pressing forward, back, or brake (land vehicles only) cancels cruise
///  - Leaving/entering a vehicle resets cruise to off
///  - HUD bar (inserted above the fuel bar) shows state: Off (grey) / Slow (yellow) / Sprint (green)
/// </summary>
public class WMMVehicleCruiseControl
{
	public const string VehicleAutoGo = "vehicleautogo";

	/// <summary>Debounce timestamp for the toggle key (GameTimer ticks).</summary>
	private static ulong autoRunMarkedTime;

	/// <summary>Key used to toggle cruise control. Unity KeyCode 113 = Q.</summary>
	private static readonly KeyCode ToggleKey = KeyCode.Q;

	/// <summary>Cooldown in ticks between key toggles (game runs ~20 ticks/sec).</summary>
	private const ulong ToggleCooldownTicks = 5;

	public class WMMVehicleCruiseControl_Init : IModApi
	{
		public void InitMod(Mod _modInstance)
		{
			Log.Out("[WMM] Vehicle Cruise Control: initializing patches");
			Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.None);
			Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);

			Harmony harmony = new Harmony(GetType().ToString());
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}
	}

	/// <summary>Cycles the cruise state and forces forward input while cruise is active.</summary>
	[HarmonyPatch(typeof(EntityVehicle))]
	[HarmonyPatch("MoveByAttachedEntity")]
	[HarmonyPatch(new Type[] { typeof(EntityPlayerLocal) })]
	public class PatchEntityVehicleMoveByAttachedEntity
	{
		private static void Postfix(EntityVehicle __instance, EntityPlayerLocal _player, MovementInput ___movementInput)
		{
			EntityAlive playerAlive = _player;

			// Toggle cruise control with the cruise key (debounced).
			if (Input.GetKey(ToggleKey) && (autoRunMarkedTime == 0L || GameTimer.Instance.ticks > autoRunMarkedTime))
			{
				float state = playerAlive.GetCVar(VehicleAutoGo);
				if (state == 1f)
				{
					playerAlive.SetCVar(VehicleAutoGo, 2f); // Slow -> Sprint
				}
				else if (state == 2f)
				{
					playerAlive.SetCVar(VehicleAutoGo, 0f); // Sprint -> Off
				}
				else
				{
					playerAlive.SetCVar(VehicleAutoGo, 1f); // Off -> Slow
				}
				autoRunMarkedTime = GameTimer.Instance.ticks + ToggleCooldownTicks;
			}

			// Player input cancels cruise: forward, back, or brake (heli/gyro keep their analogue braking).
			LocalPlayerUI localPlayerUI = LocalPlayerUI.GetUIForPlayer(_player);
			if (localPlayerUI != null && localPlayerUI.playerInput != null)
			{
				PlayerActionsVehicle vehicleActions = localPlayerUI.playerInput.VehicleActions;
				bool isHeli = __instance is EntityVHelicopter;
				bool isGyro = __instance is EntityVGyroCopter;

				if (vehicleActions.Move.Y > 0f ||
					vehicleActions.MoveBack.IsPressed ||
					(vehicleActions.Brake.IsPressed && !isHeli && !isGyro))
				{
					playerAlive.SetCVar(VehicleAutoGo, 0f);
				}

				// Apply cruise: keep moving forward at slow or sprint pace.
				float state = playerAlive.GetCVar(VehicleAutoGo);
				if (state == 1f)
				{
					___movementInput.moveForward = 1f;
					___movementInput.running = false;
				}
				else if (state == 2f)
				{
					___movementInput.moveForward = 1f;
					___movementInput.running = true;
				}
			}
		}
	}

	/// <summary>Reset cruise state when a local player leaves the vehicle.</summary>
	[HarmonyPatch(typeof(EntityVehicle))]
	[HarmonyPatch("DetachEntity")]
	[HarmonyPatch(new Type[] { typeof(Entity) })]
	public class PatchEntityVehicleDetachEntity
	{
		private static void Postfix(Entity _entity)
		{
			if (!_entity.isEntityRemote && GameManager.Instance.World != null && _entity is EntityPlayerLocal)
			{
				((EntityAlive)_entity).SetCVar(VehicleAutoGo, 0f);
				autoRunMarkedTime = 0L;
			}
		}
	}

	/// <summary>Reset cruise state when a local player mounts the vehicle.</summary>
	[HarmonyPatch(typeof(EntityVehicle))]
	[HarmonyPatch("AttachEntityToSelf")]
	[HarmonyPatch(new Type[] { typeof(Entity), typeof(int) })]
	public class PatchEntityVehicleAttachEntityToSelf
	{
		private static void Postfix(Entity _entity)
		{
			if (!_entity.isEntityRemote && GameManager.Instance.World != null && _entity is EntityPlayerLocal)
			{
				((EntityAlive)_entity).SetCVar(VehicleAutoGo, 0f);
				autoRunMarkedTime = 0L;
			}
		}
	}
}
