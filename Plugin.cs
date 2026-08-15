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
    [BepInPlugin("neutral.supplybuffet", "SupplyBuffetMod", "2.1.4")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;
        public static ConfigEntry<bool> ResupplyEnabled;
        public static ConfigEntry<bool> LocalAirbaseRepairEnabled;
        public static ConfigEntry<bool> InterbaseRepairEnabled;
        public static ConfigEntry<bool> HeavyRepairEnabled;
        public static ConfigEntry<float> AirbaseRepairCooldown;
        public static ConfigEntry<int> ActiveLocalRepairLimit;
        public static ConfigEntry<int> ActiveInterbaseRepairLimit;
        public static ConfigEntry<int> ActiveHeavyRepairLimit;
        public static ConfigEntry<int> ActiveIbisLimitDryConfig;
        public static ConfigEntry<int> ActiveIbisLimitWetConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitDryConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitWetConfig;
        public static ConfigEntry<int> ActiveChimeraLimitDryConfig;
        public static ConfigEntry<int> ActiveChimeraLimitWetConfig;
        public static ConfigEntry<float> SpawnInterval;
        public static ConfigEntry<float> ThresholdA;
        public static ConfigEntry<float> ThresholdB;
        public static ConfigEntry<float> UnitCooldown;
        public static ConfigEntry<bool> TopUpUntilFull;
        public static ConfigEntry<bool> AllowNuclearFieldRearm;
        public static ConfigEntry<float> ChimeraCruiseAltitude;
        public static ConfigEntry<float> ChimeraDescentDistance;
        public static ConfigEntry<float> ChimeraReleaseInterval;
        public static ConfigEntry<float> PostDropHoldBase;
        public static ConfigEntry<float> PostDropHoldPerCrate;
        public static ConfigEntry<float> ChimeraAirdropMaxRoll;
        public static ConfigEntry<float> ChimeraAirdropMaxVerticalSpeed;
        public static ConfigEntry<float> ChimeraAirdropMaxCrossTrack;
        public static ConfigEntry<int> ChimeraAirdropMaxAttempts;
        public static ConfigEntry<bool> JammerEnabled;
        public static ConfigEntry<float> JammerRange;
        public static ConfigEntry<int> FlareBurstCount;
        public static ConfigEntry<float> FlareBurstInterval;
        public static ConfigEntry<bool> ChimeraUseRunwayDelivery;
        public static ConfigEntry<float> ChimeraRunwayDropAltitude;
        public static ConfigEntry<float> ChimeraRunwayDropTolerance;
        public static ConfigEntry<float> ChimeraRunwayDescentDistance;
        public static ConfigEntry<float> ChimeraRunwayMinReleaseSpeed;
        public static ConfigEntry<float> ChimeraRunwayMaxReleaseSpeed;
        public static ConfigEntry<float> ChimeraRunwayMaxRoll;
        public static ConfigEntry<float> ChimeraRunwayMaxVerticalSpeed;
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
        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> MunitionsPallet2Radius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;
        public static ConfigEntry<bool> FullRestoreMunitionsPallet1;
        public static ConfigEntry<bool> FullRestoreMunitionsPallet2;
        public static ConfigEntry<bool> FullRestoreNavalPallet1;
        public static ConfigEntry<bool> FullRestoreMunitionsContainer1;
        public static ConfigEntry<bool> FullRestoreNavalSupplyContainer1;
        public static ConfigEntry<bool> ExcludeLogisticsFromAILimit;
        public static ConfigEntry<bool> ExpressRearmEnabled;
        public static ConfigEntry<bool> ExpressRearmGroundEnabled;
        public static ConfigEntry<float> RearmRequestSensitivity;
        public static ConfigEntry<float> RestampInterval;
        public static ConfigEntry<float> StampThrottle;
        public static ConfigEntry<bool> DebugLogging;
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
        private const float MONITOR_INTERVAL = 5f; 
        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("[SupplyBuffetMod] Plugin v2.1.4 initializing...");
            BindConfigs();
            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();
            Log.LogInfo("[SupplyBuffetMod] v2.1.4 loaded successfully.");
        }
        private void BindConfigs()
        {
            const string S_RESUPPLY = "Resupply";
            ResupplyEnabled = Config.Bind(S_RESUPPLY, "Enabled", true, "Master switch for ammunition resupply dispatch. Disabling it stops new sorties being sent; a transport already airborne finishes its delivery and returns.");
            const string S_REPAIR = "Repair";
            LocalAirbaseRepairEnabled = Config.Bind(S_REPAIR, "LocalAirbaseRepair", false, "Let an airbase repair its own damaged buildings with an Ibis that never leaves the field.");
            InterbaseRepairEnabled = Config.Bind(S_REPAIR, "InterbaseRepair", false, "Let an Ibis or Tarantula fly a repair dozer in from a different friendly base or ship.");
            HeavyRepairEnabled = Config.Bind(S_REPAIR, "HeavyRepair", false, "Let a Chimera fly a repair dozer when no helicopter base is within its threshold.");
            AirbaseRepairCooldown = Config.Bind(S_REPAIR, "Cooldown", 600f, "Cooldown in seconds before another repair aircraft can be sent to the same airbase or outpost group.");
            ActiveLocalRepairLimit = Config.Bind(S_REPAIR, "ActiveLocalRepairLimit", 1, "Max concurrent local airbase repair flights per faction.");
            ActiveInterbaseRepairLimit = Config.Bind(S_REPAIR, "ActiveInterbaseRepairLimit", 1, "Max concurrent interbase repair flights per faction.");
            ActiveHeavyRepairLimit = Config.Bind(S_REPAIR, "ActiveHeavyRepairLimit", 1, "Max concurrent heavy (Chimera) repair flights per faction.");
            const string S_LIMITS = "ActiveLimits";
            ActiveIbisLimitDryConfig = Config.Bind(S_LIMITS, "ActiveIbisLimitDry", 2, "Max number of active UtilityHelo1 doing dry resupply");
            ActiveIbisLimitWetConfig = Config.Bind(S_LIMITS, "ActiveIbisLimitWet", 1, "Max number of active UtilityHelo1 doing wet resupply");
            ActiveTarantulaLimitDryConfig = Config.Bind(S_LIMITS, "ActiveTarantulaLimitDry", 2, "Max number of active QuadVTOL1 doing dry resupply");
            ActiveTarantulaLimitWetConfig = Config.Bind(S_LIMITS, "ActiveTarantulaLimitWet", 1, "Max number of active QuadVTOL1 doing wet resupply");
            ActiveChimeraLimitDryConfig = Config.Bind(S_LIMITS, "ActiveChimeraLimitDry", 1, "Max number of active Aryx_CargoPlane1 doing dry resupply");
            ActiveChimeraLimitWetConfig = Config.Bind(S_LIMITS, "ActiveChimeraLimitWet", 1, "Max number of active Aryx_CargoPlane1 doing wet resupply");
            SpawnInterval = Config.Bind(S_LIMITS, "SpawnIntervalSeconds", 60f, "Minimum seconds between resupply transport launches for one faction, across all airframes. 0 disables.");
            const string S_THRESHOLDS = "Thresholds";
            ThresholdA = Config.Bind(S_THRESHOLDS, "ThresholdA", 5000f, "Threshold distance in meters for UtilityHelo1 (Ibis).");
            ThresholdB = Config.Bind(S_THRESHOLDS, "ThresholdB", 15000f, "Threshold distance in meters for QuadVTOL1 (Tarantula).");
            const string S_REARMING = "Rearming";
            UnitCooldown = Config.Bind(S_REARMING, "UnitCooldown", 1f, "Minimum time (in seconds) between successive resupplies of the same unit to prevent rapid-fire loops.");
            TopUpUntilFull = Config.Bind(S_REARMING, "TopUpUntilFull", true, "Let a unit that is still short of ammunition draw its next delivery without waiting out UnitCooldown. Disable for the stricter one-delivery-per-cooldown behaviour.");
            AllowNuclearFieldRearm = Config.Bind(S_REARMING, "AllowNuclearFieldRearm", false, "Let full-restore containers refill nuclear-capable stations in the field. Vanilla requires warheads AND the container to sit inside an airbase radius (Rearmer.cs:171-177), which never happens at sea; 0.33.4 had no such gate at all.");
            const string S_CHIMERA = "Chimera";
            ChimeraCruiseAltitude = Config.Bind(S_CHIMERA, "CruiseAltitude", 2500f, "Transit altitude in metres AGL for the climb and cruise legs. Floored at the drop altitude + 300m.");
            ChimeraDescentDistance = Config.Bind(S_CHIMERA, "DescentStartDistance", 8000f, "Distance to the approach point at which the Chimera leaves cruise altitude and descends for the drop run.");
            ChimeraReleaseInterval = Config.Bind(S_CHIMERA, "ReleaseInterval", 0.2f, "Seconds between successive cargo releases in a multi-item drop.");
            PostDropHoldBase = Config.Bind(S_CHIMERA, "PostDropHoldBase", 6f, "Seconds a transport flies straight after a release with an empty bay, before turning for home. Also the control-nullifier duration.");
            PostDropHoldPerCrate = Config.Bind(S_CHIMERA, "PostDropHoldPerCrate", 6f, "Additional seconds of post-drop hold per crate still aboard. Each rail launch needs seconds to finish, and a transport with deliveries left needs a settled, straight position before its next approach is computed.");
            JammerEnabled = Config.Bind(S_CHIMERA, "JammerEnabled", true, "Employ the Chimera's jamming pod against incoming radar missiles and radar-emitting hostile aircraft.");
            JammerRange = Config.Bind(S_CHIMERA, "JammerRange", 17000f, "Maximum range in metres at which the jamming pod will engage a target.");
            FlareBurstCount = Config.Bind(S_CHIMERA, "FlareBurstCount", 10, "Number of scripted flare pulses released at the cargo drop, when the run-in is most exposed. 0 disables.");
            FlareBurstInterval = Config.Bind(S_CHIMERA, "FlareBurstInterval", 0.5f, "Seconds between the scripted drop flare pulses.");
            ChimeraAirdropMaxRoll = Config.Bind(S_CHIMERA, "AirdropMaxRoll", 10f, "Maximum absolute roll in degrees for an airdrop release. Crossing the release point above this abandons the run and re-flies the join.");
            ChimeraAirdropMaxVerticalSpeed = Config.Bind(S_CHIMERA, "AirdropMaxVerticalSpeed", 10f, "Maximum absolute vertical speed in m/s for an airdrop release.");
            ChimeraAirdropMaxCrossTrack = Config.Bind(S_CHIMERA, "AirdropMaxCrossTrack", 150f, "Maximum lateral offset from the run centreline, in metres, for an airdrop release.");
            ChimeraAirdropMaxAttempts = Config.Bind(S_CHIMERA, "AirdropMaxAttempts", 3, "Run restarts allowed before the cargo is released regardless of attitude, so a transport never carries its load home. Logged as forced=True.");
            const string S_CHIMERA_REPAIR = "ChimeraRepair";
            ChimeraUseRunwayDelivery = Config.Bind(S_CHIMERA_REPAIR, "UseRunwayDelivery", true, "Deliver a repair dozer with a low gear-down pass along the target airbase's runway instead of a 700m airdrop. Falls back to the airdrop for outposts and airbases with no usable runway.");
            ChimeraRunwayDropAltitude = Config.Bind(S_CHIMERA_REPAIR, "RunwayDropAltitude", 5f, "Release height in metres AGL for the runway pass. Clamped to 2-8m.");
            ChimeraRunwayDropTolerance = Config.Bind(S_CHIMERA_REPAIR, "RunwayDropTolerance", 2f, "Permitted deviation from the release height, in metres.");
            ChimeraRunwayDescentDistance = Config.Bind(S_CHIMERA_REPAIR, "RunwayDescentDistance", 6000f, "Distance before the release point at which the low-level descent begins.");
            ChimeraRunwayMinReleaseSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMinReleaseSpeed", 75f, "Minimum airspeed for a runway release.");
            ChimeraRunwayMaxReleaseSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxReleaseSpeed", 190f, "Maximum airspeed for a runway release.");
            ChimeraRunwayMaxRoll = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxRoll", 18f, "Maximum absolute roll in degrees at release.");
            ChimeraRunwayMaxVerticalSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxVerticalSpeed", 30f, "Maximum absolute vertical speed in m/s at release.");
            const string S_CAPACITY = "SupplyCapacity";
            MunitionsPalletCapacity = Config.Bind(S_CAPACITY, "MunitionsPallet1x1", 6000f, "Supply capacity for Munitions Pallet 1x1.");
            MunitionsPalletSingleUse = Config.Bind(S_CAPACITY, "MunitionsPallet1x1_SingleUse", true, "Whether Munitions Pallet 1x1 is single use.");
            MunitionsPallet2Capacity = Config.Bind(S_CAPACITY, "MunitionsPallet2x2", 1500f, "Supply capacity for Munitions Pallet 2x2.");
            MunitionsPallet2SingleUse = Config.Bind(S_CAPACITY, "MunitionsPallet2x2_SingleUse", true, "Whether Munitions Pallet 2x2 is single use.");
            NavalPalletCapacity = Config.Bind(S_CAPACITY, "NavalPallet1x1", 6000f, "Supply capacity for Naval Pallet.");
            NavalPalletSingleUse = Config.Bind(S_CAPACITY, "NavalPallet1x1_SingleUse", true, "Whether Naval Pallet is single use.");
            MunitionsContainerCapacity = Config.Bind(S_CAPACITY, "MunitionsContainer1", 10000f, "Supply capacity for Munitions Container.");
            MunitionsContainerSingleUse = Config.Bind(S_CAPACITY, "MunitionsContainer1_SingleUse", true, "Whether Munitions Container is single use.");
            NavalContainerCapacity = Config.Bind(S_CAPACITY, "NavalSupplyContainer1", 10000f, "Supply capacity for Naval Container.");
            NavalContainerSingleUse = Config.Bind(S_CAPACITY, "NavalSupplyContainer1_SingleUse", true, "Whether Naval Container is single use.");
            const string S_RADIUS = "SupplyRadius";
            MunitionsPalletRadius = Config.Bind(S_RADIUS, "MunitionsPallet1x1", 100f, "Rearm radius for Munitions Pallet 1x1.");
            MunitionsPallet2Radius = Config.Bind(S_RADIUS, "MunitionsPallet2x2", 100f, "Rearm radius for Munitions Pallet 2x2.");
            NavalPalletRadius = Config.Bind(S_RADIUS, "NavalPallet1x1", 100f, "Rearm radius for Naval Pallet.");
            MunitionsContainerRadius = Config.Bind(S_RADIUS, "MunitionsContainer1", 100f, "Rearm radius for Munitions Container.");
            NavalContainerRadius = Config.Bind(S_RADIUS, "NavalSupplyContainer1", 200f, "Rearm radius for Naval Container.");
            const string S_FULLRESTORE = "FullRestore";
            FullRestoreMunitionsPallet1 = Config.Bind(S_FULLRESTORE, "MunitionsPallet1x1", false, "Munitions Pallet 1x1 restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreMunitionsPallet2 = Config.Bind(S_FULLRESTORE, "MunitionsPallet2x2", false, "Munitions Pallet 2x2 restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreNavalPallet1 = Config.Bind(S_FULLRESTORE, "NavalPallet1x1", false, "Naval Pallet restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreMunitionsContainer1 = Config.Bind(S_FULLRESTORE, "MunitionsContainer1", false, "Munitions Container restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreNavalSupplyContainer1 = Config.Bind(S_FULLRESTORE, "NavalSupplyContainer1", false, "Naval Supply Container restores every weapon and is consumed, ignoring its capacity.");
            const string S_AI = "AI";
            ExcludeLogisticsFromAILimit = Config.Bind(S_AI, "ExcludeLogisticsFromAILimit", true, "Exclude resupply aircraft from Faction AI limits.");
            const string S_ADVANCED = "Advanced";
            ExpressRearmEnabled = Config.Bind(S_ADVANCED, "ExpressRearmEnabled", true, "When enabled, all ships (naval) rearm weapons unconditionally (Wet resupply).");
            ExpressRearmGroundEnabled = Config.Bind(S_ADVANCED, "ExpressRearmGroundEnabled", true, "When enabled, all ground vehicles and buildings rearm weapons unconditionally (Dry resupply).");
            RearmRequestSensitivity = Config.Bind(S_ADVANCED, "RearmRequestSensitivity", 0.5f, new ConfigDescription("Ammo fraction remaining below which a unit requests rearm. 0.999 means any expenditure asks; 0.5 matches vanilla but starves large-magazine launchers.", new AcceptableValueRange<float>(0.0f, 1.0f)));
            DebugLogging = Config.Bind(S_ADVANCED, "DebugLogging", false, "Enable verbose debug logging for troubleshooting.");
            RestampInterval = Config.Bind(S_ADVANCED, "RestampInterval", 5f, "Seconds between re-stamping RequestRearmLevel across every unit, catching weapons created after spawn. 0 disables the sweep.");
            StampThrottle = Config.Bind(S_ADVANCED, "StampThrottle", 2f, "Minimum seconds between weapon re-stamps of the same unit on the fire path. The stamp used to walk every station and weapon on every shot fired, which dominated the frame during naval gunfire. 0 restores the old per-shot behaviour.");
        }
        private void Update()
        {
            if (Time.timeSinceLevelLoad - _lastMonitorTime >= MONITOR_INTERVAL)
            {
                _lastMonitorTime = Time.timeSinceLevelLoad;
                PeriodicMonitor();
            }
            ChimeraSpawnQueue.Drain();
        }
        private void PeriodicMonitor()
        {
            if (Encyclopedia.i == null || FactionRegistry.HQLookup == null) return;
            ResupplyMissionManager.Update();
            AirbaseRepairManager.Update();
            DozerShepherd.Update();
            RestampUnits();
            foreach (var hq in FactionRegistry.HQLookup.Values)
            {
                if (hq == null || !hq.isActiveAndEnabled || hq.faction == null) continue;
                var controller = hq.RearmMissionController;
                if (controller == null) continue;
                if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, true, false, null, out Unit shipNeedingRearm) && shipNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected naval unit needing rearm: {shipNeedingRearm.unitName} (HQ: {hq.name}). Triggering wet resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, shipNeedingRearm);
                }
                else if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, false, true, null, out Unit groundNeedingRearm) && groundNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected ground unit needing rearm: {groundNeedingRearm.unitName} (HQ: {hq.name}). Triggering dry resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, groundNeedingRearm);
                }
                else if (ResupplyMissionManager.TryGetRestockingSupplyVehicle(controller, null, out Unit restockingTruck) && restockingTruck != null)
                {
                    if (DebugLogging.Value && restockingTruck.TryGetComponent(out Rearmer truckRearmer))
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Supply vehicle '{restockingTruck.unitName}' is driving to restock (capacity {truckRearmer.Capacity:F0}/{truckRearmer.GetMaxCapacity():F0}). Triggering dry resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, restockingTruck);
                }
            }
        }
        private float _lastRestampTime;
        private void RestampUnits()
        {
            if (RestampInterval == null || RestampInterval.Value <= 0f) return;
            if (Time.timeSinceLevelLoad - _lastRestampTime < RestampInterval.Value) return;
            _lastRestampTime = Time.timeSinceLevelLoad;
            if (UnitRegistry.allUnits == null) return;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled) continue;
                RearmStampHelper.StampUnit(unit);
            }
        }
        public static bool IsNavalUnit(Unit unit) => unit is Ship;
        public static bool IsModDispatchedFlight(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            return ResupplyCensus.WasDispatchedByMod(aircraft)
                || ResupplyMissionManager.IsAssignedToResupply(aircraft);
        }
        private static bool IsSpawnedAircraftWet(Aircraft ac)
        {
            if (ac == null) return false;
            if (ResupplyCensus.TryGetIsWet(ac, out bool tagged)) return tagged;
            if (ac.loadout == null || ac.loadout.weapons == null) return false;
            foreach (var w in ac.loadout.weapons)
            {
                if (w == null || w.info == null) continue;
                string infoName = w.info.name;
                if (!string.IsNullOrEmpty(infoName) && infoName.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
        private static int GetActiveResupplyCount(FactionHQ hq, string jsonKey, bool isWet, bool includeQueue = false)
        {
            int count = ResupplyCensus.CountInFlight(hq, jsonKey, isWet);
            if (UnitRegistry.allAircraft != null)
            {
                foreach (var ac in UnitRegistry.allAircraft)
                {
                    if (ac != null && !ac.disabled && ac.definition != null && ac.definition.jsonKey == jsonKey)
                    {
                        if (!IsModDispatchedFlight(ac)) continue;
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
        private class NullifierWindow
        {
            public float EndTime;
            public Vector3 LockedDir;
        }
        private static readonly ConditionalWeakTable<Aircraft, NullifierWindow> Nullified =
            new ConditionalWeakTable<Aircraft, NullifierWindow>();
        private static float _nullifierLatestEnd;
        public static bool AnyControlNullified => Time.timeSinceLevelLoad < _nullifierLatestEnd;
        public static void TriggerControlNullifier(Aircraft aircraft, float duration = 3.0f)
        {
            if (aircraft == null) return;
            float endTime = Time.timeSinceLevelLoad + duration;
            NullifierWindow w = Nullified.GetValue(aircraft, _ => new NullifierWindow());
            w.EndTime = endTime;
            Vector3 dir = (aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f)
                ? aircraft.rb.velocity
                : aircraft.transform.forward;
            dir.y = 0f;
            w.LockedDir = (dir.sqrMagnitude > 0.001f) ? dir.normalized : Vector3.forward;
            if (endTime > _nullifierLatestEnd) _nullifierLatestEnd = endTime;
            Log.LogInfo($"[SupplyBuffetMod] Control nullifier triggered for '{aircraft.unitName}' until T={endTime:F1}s ({duration}s duration).");
        }
        public static bool IsControlNullified(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            return Nullified.TryGetValue(aircraft, out NullifierWindow w) && Time.timeSinceLevelLoad < w.EndTime;
        }
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