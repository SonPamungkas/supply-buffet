using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
namespace SupplyBuffetMod
{
    [BepInPlugin("neutral.supplybuffet", "SupplyBuffetMod", "2.1.1")] 
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> DebugLogging;
        public static ConfigEntry<bool> ExcludeLogisticsFromAILimit;
        public static ConfigEntry<bool> ExpressRearmEnabled;
        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> MunitionsPallet2Radius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;
        public static ConfigEntry<bool> MunitionsPalletSingleUse;
        public static ConfigEntry<bool> MunitionsPallet2SingleUse;
        public static ConfigEntry<bool> NavalPalletSingleUse;
        public static ConfigEntry<bool> MunitionsContainerSingleUse;
        public static ConfigEntry<bool> NavalContainerSingleUse;
        public static ConfigEntry<float> MunitionsPalletCapacity;
        public static ConfigEntry<float> MunitionsPallet2Capacity;
        public static ConfigEntry<float> NavalPalletCapacity;
        public static ConfigEntry<float> MunitionsContainerCapacity;
        public static ConfigEntry<float> NavalContainerCapacity;
        public static ConfigEntry<float> UnitCooldown;
        public static ConditionalWeakTable<Unit, StrongBox<float>> UnitLastRearmTime = new ConditionalWeakTable<Unit, StrongBox<float>>();
        public static bool ForceSpawnInProgress = false;
        public static bool ForceSpawnIsNaval = false;
        private static Dictionary<string, WeaponMount> _mountsByJsonKey;
        public class ChimeraSpawnRequest
        {
            public FactionHQ HQ;
            public Unit Requester;
            public AircraftDefinition ChimeraDef;
            public NuclearOption.SavedMission.Loadout Loadout;
            public string LoadoutName;
            public bool IsWet;
            public float RequestTime;
        }
        public static Queue<ChimeraSpawnRequest> SpawnQueue = new Queue<ChimeraSpawnRequest>();
        private float _lastQueueProcessTime = 0f;
        private float _lastResupplyMonitorTime = 0f;
        private static float _lastObservedLevelTime = 0f;
        public static ConfigFile ConfigInstance;
        public static Dictionary<string, ConfigEntry<bool>> ShipRearmEverythingConfigs = new Dictionary<string, ConfigEntry<bool>>();
        public static string SanitizeConfigKey(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            s = s.Replace("[", "(").Replace("]", ")")
                 .Replace("=", "-").Replace("\\", "/")
                 .Replace("'", "").Replace("\"", "")
                 .Replace("\n", " ").Replace("\t", " ");
            return s.Trim();
        }
        public static bool GetShipRearmEverythingConfig(string shipName)
        {
            if (string.IsNullOrEmpty(shipName) || ConfigInstance == null) return false;
            string safeName = SanitizeConfigKey(shipName);
            string key = safeName.ToLowerInvariant();
            if (!ShipRearmEverythingConfigs.TryGetValue(key, out var entry))
            {
                entry = ConfigInstance.Bind("Ship Rearm Settings", 
                                            $"Rearm Everything: {safeName}", 
                                            true, 
                                            new ConfigDescription($"If true, ALL weapons (including cruise/ballistic missiles) on '{shipName}' will be made rearmable and resupplied.",
                                                                  null,
                                                                  new ConfigurationManagerAttributes { Order = 0, Browsable = true }));
                ShipRearmEverythingConfigs[key] = entry;
                Log.LogInfo($"[SupplyBuffetMod] Registered Rearm Everything config toggle for ship: {safeName}");
            }
            return entry != null && entry.Value;
        }
        public static bool IsLogisticAircraft(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.disabled) return false;
            if (ResupplyMissionManager.IsAssignedToResupply(aircraft)) return true;
            if (aircraft.weaponStations != null)
            {
                foreach (WeaponStation ws in aircraft.weaponStations)
                {
                    if (ws != null && ws.Cargo && ws.Ammo > 0) return true;
                }
            }
            var pilot = aircraft.GetComponent<Pilot>();
            if (pilot != null && (pilot.currentState is AIHeloTransportState || pilot.currentState is AIFixedWingTransportState))
            {
                return true;
            }
            return false;
        }
        private static bool _hasScannedDefinitions = false;
        public static void ScanAndRegisterShipConfigs()
        {
            if (_hasScannedDefinitions) return;
            _hasScannedDefinitions = true;
            try
            {
                var shipDefs = Resources.FindObjectsOfTypeAll<ShipDefinition>();
                if (shipDefs != null)
                {
                    foreach (var def in shipDefs)
                    {
                        if (def == null) continue;
                        string name = def.unitName;
                        if (string.IsNullOrEmpty(name)) name = def.name;
                        if (!string.IsNullOrEmpty(name))
                        {
                            GetShipRearmEverythingConfig(name);
                        }
                    }
                }
                var allDefs = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                if (allDefs != null)
                {
                    foreach (var def in allDefs)
                    {
                        if (def == null) continue;
                        if (def is ShipDefinition || def.GetType().Name.Contains("Ship"))
                        {
                            string name = def.unitName;
                            if (string.IsNullOrEmpty(name)) name = def.name;
                            if (!string.IsNullOrEmpty(name))
                            {
                                GetShipRearmEverythingConfig(name);
                            }
                        }
                    }
                }
                if (Encyclopedia.i != null && Encyclopedia.i.ships != null)
                {
                    foreach (var shipDef in Encyclopedia.i.ships)
                    {
                        if (shipDef != null && !string.IsNullOrEmpty(shipDef.unitName))
                        {
                            GetShipRearmEverythingConfig(shipDef.unitName);
                        }
                    }
                }
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Scanned {ShipRearmEverythingConfigs.Count} ships (vanilla & modded) and generated 'Rearm Everything' config toggles.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception during ship config scan: {ex.Message}");
            }
        }
        public static void ResetMissionState()
        {
            Plugin.Log.LogInfo("[SupplyBuffetMod] Mission reset detected! Clearing queues, cooldowns, and resupply assignments.");
            SpawnQueue.Clear();
            SupplyRunWet.IsSpawning = false;
            SupplyRunDry.IsSpawning = false;
            SupplyRunWet._lastSupplyRun.Clear();
            SupplyRunDry._lastSupplyRun.Clear();
            UnitLastRearmTime = new ConditionalWeakTable<Unit, System.Runtime.CompilerServices.StrongBox<float>>();
            ResupplyMissionManager.Reset();
        }
        private void Update()
        {
            float currentTime = UnityEngine.Time.timeSinceLevelLoad;
            if (currentTime < _lastObservedLevelTime - 2.0f)
            {
                ResetMissionState();
                _lastResupplyMonitorTime = 0f;
                _lastQueueProcessTime = 0f;
            }
            _lastObservedLevelTime = currentTime;
            if (!_hasScannedDefinitions && Encyclopedia.i != null)
            {
                ScanAndRegisterShipConfigs();
            }
            ResupplyMissionManager.Update(currentTime);
            if (currentTime - _lastResupplyMonitorTime > 30.0f)
            {
                _lastResupplyMonitorTime = currentTime;
                CheckAndQueueNeededResupplies();
            }
            if (SpawnQueue.Count > 0 && currentTime - _lastQueueProcessTime > 2.0f)
            {
                _lastQueueProcessTime = currentTime;
                var req = SpawnQueue.Peek();
                if (req.Requester == null) 
                {
                    SpawnQueue.Dequeue();
                    return;
                }
                bool success = TryProcessSpawnRequest(req);
                if (success)
                {
                    SpawnQueue.Dequeue();
                }
            }
        }
        private void CheckAndQueueNeededResupplies()
        {
            var controllers = UnityEngine.Object.FindObjectsOfType<RearmMissionController>();
            if (controllers == null || controllers.Length == 0) return;
            var allAircraft = UnityEngine.Object.FindObjectsOfType<Aircraft>();
            foreach (var controller in controllers)
            {
                if (controller == null) continue;
                FactionHQ hq = controller.GetComponentInParent<FactionHQ>();
                if (hq == null) continue;
                int activeChimeras = 0;
                if (allAircraft != null)
                {
                    foreach (var a in allAircraft)
                    {
                        if (a != null && !a.disabled && a.NetworkHQ == hq && a.unitName != null && a.unitName.Contains("Chimera"))
                        {
                            activeChimeras++;
                        }
                    }
                }
                int queuedChimeras = 0;
                foreach (var req in SpawnQueue)
                {
                    if (req != null && req.HQ == hq)
                    {
                        queuedChimeras++;
                    }
                }
                if (activeChimeras + queuedChimeras >= 4)
                {
                    continue;
                }
                if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, true, false, null, out Unit shipNeedingRearm) && shipNeedingRearm != null)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected unassigned ship needing rearm: {shipNeedingRearm.unitName} (HQ: {hq.name}). Triggering naval resupply.");
                    SupplyRunWet.HandleNavalResupply(hq, shipNeedingRearm);
                }
                else if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, false, true, null, out Unit groundNeedingRearm) && groundNeedingRearm != null)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected unassigned ground/ship needing rearm: {groundNeedingRearm.unitName} (HQ: {hq.name}). Triggering dry resupply.");
                    SupplyRunDry.SpawnAirResupply(hq, groundNeedingRearm);
                }
            }
        }
        private bool TryProcessSpawnRequest(ChimeraSpawnRequest req)
        {
            Airbase spawnBase = null;
            var airbases = FactionRegistry.airbaseLookup.Values;
            bool HasHangarMed(Airbase baseToCheck)
            {
                foreach (var h in baseToCheck.hangars)
                {
                    if (h != null && h.gameObject.name.IndexOf("hangar_med", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                return false;
            }
            foreach (var ab in airbases)
            {
                if (ab != null && !ab.disabled && ab.CurrentHQ == req.HQ && HasHangarMed(ab))
                {
                    spawnBase = ab;
                    break;
                }
            }
            if (spawnBase == null)
            {
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ != null && ab.CurrentHQ.faction == req.HQ.faction && HasHangarMed(ab))
                    {
                        spawnBase = ab;
                        break;
                    }
                }
            }
            if (spawnBase == null)
            {
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ == req.HQ && ab.CanSpawnAircraft(req.ChimeraDef))
                    {
                        spawnBase = ab;
                        break;
                    }
                }
            }
            if (spawnBase != null)
            {
                int randomLivery = req.ChimeraDef.aircraftParameters.GetRandomLiveryForFaction(req.HQ.faction);
                Hangar chosenHangar = null;
                var hangars = spawnBase.hangars;
                foreach(var h in hangars)
                {
                    if (h == null || !h.Available || !h.CanSpawnAircraft(req.ChimeraDef)) continue;
                    if (h.gameObject.name.IndexOf("hangar_med", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    chosenHangar = h;
                    break;
                }
                if (chosenHangar == null)
                {
                    foreach(var h in hangars)
                    {
                        if (h == null || !h.Available || !h.CanSpawnAircraft(req.ChimeraDef)) continue;
                        chosenHangar = h;
                        break;
                    }
                }
                if (chosenHangar != null)
                {
                    req.HQ.AddSupplyUnit(req.ChimeraDef, 1);
                    var spawnResult = chosenHangar.TrySpawnAircraft(null, req.ChimeraDef, new LiveryKey(randomLivery), req.Loadout, 1f);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Queued spawn successful for {req.ChimeraDef.unitName} (Loadout: {req.LoadoutName}) at {spawnBase.gameObject.name} (Hangar: {chosenHangar.name}) for {req.Requester.unitName}. Success: {spawnResult.Allowed}");
                    return true;
                }
                else
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Waiting for available hangar at {spawnBase.gameObject.name} for {req.Requester.unitName}...");
                    return false;
                }
            }
            Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid spawn base found for queued Chimera spawn.");
            return true; 
        }
        public static string GetMountKey(WeaponMount mount)
        {
            if (mount == null) return null;
            if (!string.IsNullOrEmpty(mount.jsonKey)) return mount.jsonKey;
            string n = mount.name;
            if (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - 7);
            return n;
        }
        public static WeaponMount FindMountByKey(string key)
        {
            if (_mountsByJsonKey == null)
            {
                _mountsByJsonKey = Resources.FindObjectsOfTypeAll<WeaponMount>()
                    .Where(m => m != null && !string.IsNullOrEmpty(GetMountKey(m)))
                    .GroupBy(m => GetMountKey(m))
                    .ToDictionary(g => g.Key, g => g.First());
            }
            _mountsByJsonKey.TryGetValue(key, out var mount);
            return mount;
        }
        private void Awake()
        {
            ConfigInstance = Config;
            Log = Logger;
            DebugLogging = Config.Bind("General", "DebugLogging", false, "Enable debug logging for Supply Buffet.");
            ExcludeLogisticsFromAILimit = Config.Bind("General", "ExcludeLogisticsFromAILimit", true, "If true, logistic/resupply AI aircraft will not count towards the faction's AI Aircraft Limit, allowing combat sorties to spawn even when many resupply flights are active.");
            ExpressRearmEnabled = Config.Bind("ExpressRearm", "Enabled", true, "Let ships and ground vehicles immediately request rearm, and spawn supply helicopters when they do.");
            MunitionsPalletRadius = Config.Bind("SupplyRadius", "MunitionsPallet1", 100f, "Supply radius for Munitions Pallet");
            MunitionsPallet2Radius = Config.Bind("SupplyRadius", "MunitionsPallet2", 100f, "Supply radius for Small Munitions Pallet");
            NavalPalletRadius = Config.Bind("SupplyRadius", "NavalPallet1", 100f, "Supply radius for Naval Pallet");
            MunitionsContainerRadius = Config.Bind("SupplyRadius", "MunitionsContainer1", 100f, "Supply radius for Munitions Container");
            NavalContainerRadius = Config.Bind("SupplyRadius", "NavalSupplyContainer1", 200f, "Supply radius for Naval Container");
            MunitionsPalletSingleUse = Config.Bind("SupplyContainer", "MunitionsPallet1_SingleUse", false, "If true, the container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            MunitionsPallet2SingleUse = Config.Bind("SupplyContainer", "MunitionsPallet2_SingleUse", false, "If true, the small pallet is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            NavalPalletSingleUse = Config.Bind("SupplyContainer", "NavalPallet1_SingleUse", false, "If true, the naval pallet is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            MunitionsContainerSingleUse = Config.Bind("SupplyContainer", "MunitionsContainer1_SingleUse", false, "If true, the munitions container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            NavalContainerSingleUse = Config.Bind("SupplyContainer", "NavalSupplyContainer1_SingleUse", false, "If true, the naval container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            MunitionsPalletCapacity = Config.Bind("SupplyCapacity", "MunitionsPallet1", 6000f, "Supply capacity for Munitions Pallet");
            MunitionsPallet2Capacity = Config.Bind("SupplyCapacity", "MunitionsPallet2", 1500f, "Supply capacity for Small Munitions Pallet");
            NavalPalletCapacity = Config.Bind("SupplyCapacity", "NavalPallet1", 6000f, "Supply capacity for Naval Pallet");
            MunitionsContainerCapacity = Config.Bind("SupplyCapacity", "MunitionsContainer1", 10000f, "Supply capacity for Munitions Container");
            NavalContainerCapacity = Config.Bind("SupplyCapacity", "NavalSupplyContainer1", 10000f, "Supply capacity for Naval Container");
            UnitCooldown = Config.Bind("Rearming", "UnitCooldown", 10f, "Minimum time (in seconds) between successive resupplies of the same unit, to prevent nonstop firing/rearm loops.");
            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();
            Log.LogInfo("SupplyBuffetMod initialized.");
        }
    }
}
    public class ChimeraSpawnRequest
    {
        public FactionHQ HQ;
        public Unit Requester;
        public AircraftDefinition ChimeraDef;
        public NuclearOption.SavedMission.Loadout Loadout;
        public string LoadoutName;
        public bool IsWet;
    }