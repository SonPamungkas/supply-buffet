// ============================================================================
// FILE: Plugin.cs
// PURPOSE: Main BepInEx entry point, configuration manager, periodic monitor,
//          and shared utility methods for SupplyBuffetMod.
//
// TRIGGERS:
//   - Awake: Called by BepInEx on game startup.
//   - Update: Called once per frame; runs periodic resupply checks every 5 seconds.
//
// EFFECTS:
//   - Applies Harmony patches for supply radius, capacity, cooldown, and limits.
//   - Monitors faction HQs for ships and ground units with low ammunition.
//   - Triggers SupplyRunDry / SupplyRunWet when units require rearming.
//   - Drives ChimeraSpawnQueue, which owns Chimera spawn dispatch itself.
//   - Tracks control nullification states for cargo deployments.
// ============================================================================

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using NuclearOption.SavedMission;

namespace SupplyBuffetMod
{
    [BepInPlugin("neutral.supplybuffet", "SupplyBuffetMod", "2.1.2")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;
        public static ConfigEntry<bool> DebugLogging;

        // General AI and rearm flags
        public static ConfigEntry<bool> ExcludeLogisticsFromAILimit;
        public static ConfigEntry<bool> ExpressRearmEnabled;         // Ships (Wet)
        public static ConfigEntry<bool> ExpressRearmGroundEnabled;    // GroundVehicle + Building (Dry)

        // Supply container radius overrides
        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> MunitionsPallet2Radius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;

        // Supply container capacity and single-use overrides
        public static ConfigEntry<float> MunitionsPalletCapacity;
        public static ConfigEntry<bool> MunitionsPalletSingleUse;
        public static ConfigEntry<float> MunitionsPallet2Capacity;
        public static ConfigEntry<bool> MunitionsPallet2SingleUse;
        public static ConfigEntry<float> NavalPalletCapacity;
        public static ConfigEntry<bool> NavalPalletSingleUse;
        public static ConfigEntry<float> MunitionsContainerCapacity;
        public static ConfigEntry<bool> MunitionsContainerSingleUse;
        public static ConfigEntry<float> NavalContainerCapacity;
        public static ConfigEntry<bool> NavalContainerSingleUse;

        // Rearming cooldown between resupply missions
        public static ConfigEntry<float> UnitCooldown;
        public static ConfigEntry<float> RearmRequestSensitivity;

        // Distance thresholds
        public static ConfigEntry<float> ThresholdA; // < ThresholdA -> UtilityHelo1
        public static ConfigEntry<float> ThresholdB; // <= ThresholdB -> QuadVTOL1, > -> Chimera

        // Active aircraft population limits
        public static ConfigEntry<int> ActiveIbisLimitDryConfig;
        public static ConfigEntry<int> ActiveIbisLimitWetConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitDryConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitWetConfig;
        public static ConfigEntry<int> ActiveChimeraLimitDryConfig;
        public static ConfigEntry<int> ActiveChimeraLimitWetConfig;

        // Tracks last rearm timestamp per unit for UnitCooldown checks
        public static ConditionalWeakTable<Unit, StrongBox<float>> UnitLastRearmTime = new ConditionalWeakTable<Unit, StrongBox<float>>();

        // Tracks units that were assigned a Tarantula via the 3x distance fallback rule (grants 3x zone limit).
        // ConditionalWeakTable, not HashSet: a set would root every destroyed unit for the whole session.
        private static readonly ConditionalWeakTable<Unit, object> ExtendedZoneTable = new ConditionalWeakTable<Unit, object>();

        public static void MarkExtendedZoneTarget(Unit unit)
        {
            if (unit != null) ExtendedZoneTable.GetValue(unit, _ => new object());
        }

        public static bool IsExtendedZoneTarget(Unit unit)
        {
            return unit != null && ExtendedZoneTable.TryGetValue(unit, out _);
        }


        private float _lastMonitorTime = 0f;
        private const float MONITOR_INTERVAL = 5f; // Run mission scan every 5 seconds

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("[SupplyBuffetMod] Plugin v2.1.2 initializing...");

            // --- Bind Configuration Entries ---
            ExcludeLogisticsFromAILimit = Config.Bind("AI", "ExcludeLogisticsFromAILimit", true, "Exclude resupply aircraft from Faction AI limits.");
            ExpressRearmEnabled = Config.Bind("Advanced", "ExpressRearmEnabled", true, "When enabled, all ships (naval) rearm weapons unconditionally (Wet resupply).");
            ExpressRearmGroundEnabled = Config.Bind("Advanced", "ExpressRearmGroundEnabled", true, "When enabled, all ground vehicles and buildings rearm weapons unconditionally (Dry resupply).");
            RearmRequestSensitivity = Config.Bind("Advanced", "RearmRequestSensitivity", 0.999f, new ConfigDescription("Threshold of ammo remaining to trigger rearm (e.g., 0.999 = 99.9%)", new AcceptableValueRange<float>(0.0f, 1.0f)));
            DebugLogging = Config.Bind("Advanced", "DebugLogging", false, "Enable verbose debug logging for troubleshooting.");

            ThresholdA = Config.Bind("Thresholds", "ThresholdA", 5000f, "Threshold distance in meters for UtilityHelo1 (Ibis).");
            ThresholdB = Config.Bind("Thresholds", "ThresholdB", 15000f, "Threshold distance in meters for QuadVTOL1 (Tarantula).");

            ActiveIbisLimitDryConfig = Config.Bind("ActiveLimits", "ActiveIbisLimitDry", 2, "Max number of active UtilityHelo1 doing dry resupply");
            ActiveIbisLimitWetConfig = Config.Bind("ActiveLimits", "ActiveIbisLimitWet", 1, "Max number of active UtilityHelo1 doing wet resupply");
            ActiveTarantulaLimitDryConfig = Config.Bind("ActiveLimits", "ActiveTarantulaLimitDry", 2, "Max number of active QuadVTOL1 doing dry resupply");
            ActiveTarantulaLimitWetConfig = Config.Bind("ActiveLimits", "ActiveTarantulaLimitWet", 1, "Max number of active QuadVTOL1 doing wet resupply");
            ActiveChimeraLimitDryConfig = Config.Bind("ActiveLimits", "ActiveChimeraLimitDry", 1, "Max number of active Aryx_CargoPlane1 doing dry resupply");
            ActiveChimeraLimitWetConfig = Config.Bind("ActiveLimits", "ActiveChimeraLimitWet", 1, "Max number of active Aryx_CargoPlane1 doing wet resupply");

            MunitionsPalletRadius = Config.Bind("SupplyRadius", "MunitionsPallet1x1", 100f, "Rearm radius for Munitions Pallet 1x1.");
            MunitionsPallet2Radius = Config.Bind("SupplyRadius", "MunitionsPallet2x2", 100f, "Rearm radius for Munitions Pallet 2x2.");
            NavalPalletRadius = Config.Bind("SupplyRadius", "NavalPallet1x1", 100f, "Rearm radius for Naval Pallet.");
            MunitionsContainerRadius = Config.Bind("SupplyRadius", "MunitionsContainer1", 100f, "Rearm radius for Munitions Container.");
            NavalContainerRadius = Config.Bind("SupplyRadius", "NavalSupplyContainer1", 200f, "Rearm radius for Naval Container.");

            // Vanilla defaults from supply-vanilla.log:
            //   MunitionsContainer1:   maxCapacity=10000, singleUse=True
            //   MunitionsPallet1:      maxCapacity=6000,  singleUse=True
            //   MunitionsPallet2:      maxCapacity=1500,  singleUse=True
            //   NavalSupplyContainer1: maxCapacity=10000, singleUse=True
            //   NavalPallet1:          maxCapacity=6000,  singleUse=True
            MunitionsPalletCapacity = Config.Bind("SupplyCapacity", "MunitionsPallet1x1", 6000f, "Supply capacity for Munitions Pallet 1x1.");
            MunitionsPalletSingleUse = Config.Bind("SupplyCapacity", "MunitionsPallet1x1_SingleUse", true, "Whether Munitions Pallet 1x1 is single use.");
            MunitionsPallet2Capacity = Config.Bind("SupplyCapacity", "MunitionsPallet2x2", 1500f, "Supply capacity for Munitions Pallet 2x2.");
            MunitionsPallet2SingleUse = Config.Bind("SupplyCapacity", "MunitionsPallet2x2_SingleUse", true, "Whether Munitions Pallet 2x2 is single use.");
            NavalPalletCapacity = Config.Bind("SupplyCapacity", "NavalPallet1x1", 6000f, "Supply capacity for Naval Pallet.");
            NavalPalletSingleUse = Config.Bind("SupplyCapacity", "NavalPallet1x1_SingleUse", true, "Whether Naval Pallet is single use.");
            MunitionsContainerCapacity = Config.Bind("SupplyCapacity", "MunitionsContainer1", 10000f, "Supply capacity for Munitions Container.");
            MunitionsContainerSingleUse = Config.Bind("SupplyCapacity", "MunitionsContainer1_SingleUse", true, "Whether Munitions Container is single use.");
            NavalContainerCapacity = Config.Bind("SupplyCapacity", "NavalSupplyContainer1", 10000f, "Supply capacity for Naval Container.");
            NavalContainerSingleUse = Config.Bind("SupplyCapacity", "NavalSupplyContainer1_SingleUse", true, "Whether Naval Container is single use.");

            UnitCooldown = Config.Bind("Rearming", "UnitCooldown", 120f, "Minimum time (in seconds) between successive resupplies of the same unit to prevent rapid-fire loops.");

            // Apply Harmony patches
            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();

            Log.LogInfo("[SupplyBuffetMod] v2.1.2 loaded successfully.");
        }

        private void Update()
        {
            // Run periodic scan every 5 seconds
            if (Time.timeSinceLevelLoad - _lastMonitorTime >= MONITOR_INTERVAL)
            {
                _lastMonitorTime = Time.timeSinceLevelLoad;
                PeriodicMonitor();
            }

            // Retry any Chimera request that found no free hangar when it was filed.
            // Unthrottled: ChimeraSpawnQueue early-outs on an empty queue, and each
            // pending request has its own retry backoff.
            ChimeraSpawnQueue.Drain();
        }

        /// <summary>
        /// Scans active FactionHQs for unassigned ships or ground units requiring ammunition rearming.
        /// TRIGGERS: Runs every 5 seconds from Update().
        /// EFFECTS: Triggers SupplyRunWet for naval units and SupplyRunDry for ground units.
        /// </summary>
        private void PeriodicMonitor()
        {
            if (Encyclopedia.i == null || FactionRegistry.HQLookup == null) return;

            ResupplyMissionManager.Update(Time.timeSinceLevelLoad);

            foreach (var hq in FactionRegistry.HQLookup.Values)
            {
                if (hq == null || !hq.isActiveAndEnabled || hq.faction == null) continue;

                var controller = hq.RearmMissionController;
                if (controller == null) continue;

                // Check for unassigned naval units needing rearm
                if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, true, false, null, out Unit shipNeedingRearm) && shipNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected naval unit needing rearm: {shipNeedingRearm.unitName} (HQ: {hq.name}). Triggering wet resupply.");
                    }
                    SupplyRunWet.HandleNavalResupply(hq, shipNeedingRearm);
                }
                // Check for unassigned ground units needing rearm
                else if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, false, true, null, out Unit groundNeedingRearm) && groundNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected ground unit needing rearm: {groundNeedingRearm.unitName} (HQ: {hq.name}). Triggering dry resupply.");
                    }
                    SupplyRunDry.SpawnAirResupply(hq, groundNeedingRearm);
                }
            }
        }

        /// <summary>
        /// Returns true when the unit is a naval vessel.
        /// Ship is the single concrete naval class in Nuclear Option — all ships, carriers,
        /// and any modded surface/submarine units extend it. No name-guessing needed.
        /// </summary>
        public static bool IsNavalUnit(Unit unit) => unit is Ship;

        /// <summary>
        /// Checks if an aircraft is a logistics/resupply aircraft (Ibis, Tarantula, Chimera) to exclude from faction AI limits.
        /// </summary>
        public static bool IsLogisticAircraft(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.definition == null) return false;
            string key = aircraft.definition.jsonKey;
            return key == "UtilityHelo1" || key == "QuadVTOL1" || key == "Aryx_CargoPlane1" || ResupplyMissionManager.IsAssignedToResupply(aircraft);
        }

        /// <summary>
        /// Determines if a spawned aircraft is on a Wet mission by checking if any of its weapons
        /// have "Naval" in their asset name.
        /// </summary>
        private static bool IsSpawnedAircraftWet(Aircraft ac)
        {
            if (ac == null || ac.loadout == null || ac.loadout.weapons == null) return false;
            foreach (var w in ac.loadout.weapons)
            {
                if (w == null || w.info == null) continue;
                // UnityEngine.Object.name marshals through native and allocates a fresh
                // string per access - fetch it once, then a single ordinal search.
                string infoName = w.info.name;
                if (!string.IsNullOrEmpty(infoName) && infoName.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Counts how many aircraft of a specific jsonKey are currently active or queued for an HQ.
        /// </summary>
        private static int GetActiveResupplyCount(FactionHQ hq, string jsonKey, bool isWet, bool includeQueue = false)
        {
            int count = 0;
            if (UnitRegistry.allAircraft != null)
            {
                foreach (var ac in UnitRegistry.allAircraft)
                {
                    if (ac != null && !ac.disabled && ac.definition != null && ac.definition.jsonKey == jsonKey)
                    {
                        if (ac.NetworkHQ == hq || (ac.NetworkHQ != null && ac.NetworkHQ.faction == hq.faction))
                        {
                            if (IsSpawnedAircraftWet(ac) == isWet)
                            {
                                count++;
                            }
                        }
                    }
                }
            }

            if (includeQueue && jsonKey == "Aryx_CargoPlane1")
            {
                count += ChimeraSpawnQueue.CountFor(hq, isWet);
            }

            return count;
        }

        /// <summary>
        /// Checks if the active count for a resupply aircraft type has reached or exceeded its config limit.
        /// </summary>
        public static bool IsResupplyLimitReached(FactionHQ hq, string jsonKey, bool isWet, bool includeQueue = false)
        {
            int maxLimit = int.MaxValue;
            if (jsonKey == "UtilityHelo1") 
                maxLimit = isWet ? (ActiveIbisLimitWetConfig?.Value ?? int.MaxValue) : (ActiveIbisLimitDryConfig?.Value ?? int.MaxValue);
            else if (jsonKey == "QuadVTOL1") 
                maxLimit = isWet ? (ActiveTarantulaLimitWetConfig?.Value ?? int.MaxValue) : (ActiveTarantulaLimitDryConfig?.Value ?? int.MaxValue);
            else if (jsonKey == "Aryx_CargoPlane1") 
                maxLimit = isWet ? (ActiveChimeraLimitWetConfig?.Value ?? int.MaxValue) : (ActiveChimeraLimitDryConfig?.Value ?? int.MaxValue);

            int activeCount = GetActiveResupplyCount(hq, jsonKey, isWet, includeQueue);
            if (activeCount >= maxLimit)
            {
                if (DebugLogging != null && DebugLogging.Value)
                {
                    Log.LogInfo($"[SupplyBuffetMod] Active limit reached for '{jsonKey}' ({(isWet ? "Wet" : "Dry")}) (Active/Queued: {activeCount} >= Limit: {maxLimit}).");
                }
                return true;
            }
            return false;
        }

        // -----------------------------------------------------------------------
        // CONTROL NULLIFIER (Chimera / AIFixedWingTransportState only)
        // -----------------------------------------------------------------------
        // NOTE: Do NOT wire this up to AIHeloTransportState. A prior version patched
        // AIHeloTransportState.DeployCargo and it fought the vanilla helicopter
        // autopilot (hover / AIHeloTakeoffState), causing erratic post-drop flight.
        // Vanilla helicopters transition to AIHeloTakeoffState naturally on their own.
        // The Chimera's fixed-wing cargo drop has no such vanilla handling, so it's
        // safe (and needed) there: zeroes roll/yaw and locks heading for a fixed
        // window after cargo release to counter the release-physics jolt.
        private class NullifierWindow
        {
            public float EndTime;
            public Vector3 LockedDir;
        }

        // ConditionalWeakTable, not a Dictionary keyed by instance ID: the old maps were
        // never pruned and grew for the whole session. Entries here die with the aircraft.
        private static readonly ConditionalWeakTable<Aircraft, NullifierWindow> Nullified =
            new ConditionalWeakTable<Aircraft, NullifierWindow>();

        // Cheapest possible reject for the input patches, which run on every aircraft
        // every physics tick while the window is almost always inactive.
        private static float _nullifierLatestEnd;

        public static bool AnyControlNullified => Time.timeSinceLevelLoad < _nullifierLatestEnd;

        /// <summary>
        /// Starts a control-nullification window for the given aircraft: roll/yaw
        /// input is zeroed and heading is locked to the current velocity direction
        /// (or forward, if nearly stationary) for the given duration.
        /// </summary>
        public static void TriggerControlNullifier(Aircraft aircraft, float duration = 5.0f)
        {
            if (aircraft == null) return;
            float endTime = Time.timeSinceLevelLoad + duration;
            NullifierWindow w = Nullified.GetValue(aircraft, _ => new NullifierWindow());
            w.EndTime = endTime;
            w.LockedDir = (aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f)
                ? aircraft.rb.velocity.normalized
                : aircraft.transform.forward;
            if (endTime > _nullifierLatestEnd) _nullifierLatestEnd = endTime;
            Log.LogInfo($"[SupplyBuffetMod] Control nullifier triggered for '{aircraft.unitName}' until T={endTime:F1}s ({duration}s duration).");
        }

        /// <summary>
        /// Returns true while the given aircraft is within an active control-nullification window.
        /// </summary>
        public static bool IsControlNullified(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            return Nullified.TryGetValue(aircraft, out NullifierWindow w) && Time.timeSinceLevelLoad < w.EndTime;
        }

        /// <summary>
        /// Retrieves the locked heading direction captured when the nullifier was triggered.
        /// </summary>
        public static bool TryGetNullifiedVelocityDir(Aircraft aircraft, out Vector3 dir)
        {
            if (aircraft != null && Nullified.TryGetValue(aircraft, out NullifierWindow w))
            {
                dir = w.LockedDir;
                return true;
            }
            dir = Vector3.forward;
            return false;
        }
    }
}
