using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    public static class SupplyRunDry
    {
        public static bool IsSpawning = false;
        public static Dictionary<Unit, float> _lastSupplyRun = new Dictionary<Unit, float>();
        [HarmonyPatch(typeof(RearmMissionController), "RegisterNeedsRearm")]
        public class Dry_SupplyRun_Notify_Patch
        {
            static void Postfix(RearmMissionController __instance, Unit requester)
            {
                if (!Plugin.ExpressRearmEnabled.Value || requester == null) return;
                bool isShip = requester.GetComponentInParent<Ship>() != null;
                bool isVehicle = requester.GetComponentInParent<GroundVehicle>() != null;
                var turret = requester.GetComponentInParent<Turret>();
                if (turret != null)
                {
                    Unit attached = turret.GetAttachedUnit();
                    if (attached != null)
                    {
                        if (attached.GetComponentInParent<Ship>() != null)
                            isShip = true;
                        else if (attached.GetComponentInParent<GroundVehicle>() != null)
                            isVehicle = true;
                    }
                }
                if (isShip || !isVehicle) return;
                if (ResupplyMissionManager.IsUnitAssignedOrQueued(requester)) return;
                if (_lastSupplyRun.TryGetValue(requester, out float lastRun))
                {
                    if (Time.timeSinceLevelLoad - lastRun < 60f) return;
                }
                _lastSupplyRun[requester] = Time.timeSinceLevelLoad;
                FactionHQ hq = __instance.GetComponentInParent<FactionHQ>();
                if (hq == null) hq = requester.NetworkHQ;
                if (hq == null) return;
                HandleGroundResupply(hq, requester);
            }
        }
        private static void HandleGroundResupply(FactionHQ hq, Unit requester)
        {
            var truckDef = Encyclopedia.i.vehicles.FirstOrDefault(v => 
                v.unitPrefab != null && 
                v.unitPrefab.GetComponentInChildren<RearmVehicleAI>() != null);
            if (truckDef != null)
            {
                if (hq.TryGetNearestDepot(requester.transform.position, float.MaxValue, out VehicleDepot bestDepot))
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawning {truckDef.unitName} at {bestDepot.unitName} for ground vehicle {requester.unitName}");
                    bestDepot.TrySpawnVehicle(truckDef);
                }
                else if (Plugin.DebugLogging.Value)
                {
                    Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid VehicleDepot found for HQ {hq.faction?.factionName} to spawn truck.");
                }
            }
            SpawnAirResupply(hq, requester);
        }
        public static void SpawnAirResupply(FactionHQ hq, Unit requester)
        {
            var helo = Encyclopedia.i.aircraft.FirstOrDefault(a => a.jsonKey == "UtilityHelo1");
            var vtol = Encyclopedia.i.aircraft.FirstOrDefault(a => a.jsonKey == "QuadVTOL1");
            if (helo != null) hq.AddSupplyUnit(helo, 1);
            if (vtol != null) hq.AddSupplyUnit(vtol, 1);
            IsSpawning = true;
            try
            {
                StandardLoadout GetRequiredLoadout(AircraftDefinition def)
                {
                    if (def == null || def.aircraftParameters.StandardLoadouts == null) return null;
                    StandardLoadout best = null;
                    foreach (var stdLoadout in def.aircraftParameters.StandardLoadouts)
                    {
                        if (stdLoadout.disabled) continue;
                        foreach (var mount in stdLoadout.loadout.weapons)
                        {
                            if (mount == null || mount.info == null) continue;
                            string k = Plugin.GetMountKey(mount);
                            if (k == null) continue;
                            if (!mount.info.rearmShip && (k == "MunitionsPallet1" || k == "MunitionsContainer1" || k == "MunitionsContainerx2"))
                                best = stdLoadout;
                        }
                    }
                    return best;
                }
                StandardLoadout bestLoadoutHelo = GetRequiredLoadout(helo);
                StandardLoadout bestLoadoutVtol = GetRequiredLoadout(vtol);
                if (bestLoadoutHelo != null || bestLoadoutVtol != null)
                {
                    Airbase spawnBase = null;
                    var airbases = FactionRegistry.airbaseLookup.Values;
                    bool IsBaseAllowed(Airbase ab)
                    {
                        string n = ab.gameObject.name;
                        bool isHelipadOrSupply = n.IndexOf("Aryx_SupplyShip1", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Helipad", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isCarrier = n.IndexOf("AssaultCarrier1", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("FleetCarrier1", StringComparison.OrdinalIgnoreCase) >= 0;
                        return isHelipadOrSupply || isCarrier;
                    }
                    foreach (var ab in airbases)
                    {
                        if (ab != null && !ab.disabled && ab.CurrentHQ == hq && ab.gameObject != requester.gameObject && IsBaseAllowed(ab))
                        {
                            spawnBase = ab;
                            break;
                        }
                    }
                    if (spawnBase == null)
                    {
                        foreach (var ab in airbases)
                        {
                            if (ab != null && !ab.disabled && ab.CurrentHQ == hq && ab.gameObject != requester.gameObject &&
                               ((helo != null && ab.CanSpawnAircraft(helo)) || (vtol != null && ab.CanSpawnAircraft(vtol))))
                            {
                                spawnBase = ab;
                                break;
                            }
                        }
                    }
                    if (spawnBase != null)
                    {
                        float dist = Vector3.Distance(spawnBase.transform.position, requester.transform.position);
                        AircraftDefinition spawnDef = null;
                        StandardLoadout spawnLoadout = null;
                        float heloThreshold = Plugin.DistanceBase.Value * Plugin.ThresholdMultiplierB.Value;
                        if (dist > heloThreshold)
                        {
                            if (Plugin.IsResupplyLimitReached(hq, "QuadVTOL1"))
                            {
                                Plugin.Log.LogInfo($"[SupplyBuffetMod] Tarantula is responsible for dry resupply of '{requester.unitName}', but Active tarantula limit is reached. Aborting spawn without fallback.");
                                return;
                            }
                            if (vtol != null && bestLoadoutVtol != null && spawnBase.CanSpawnAircraft(vtol))
                            {
                                spawnDef = vtol;
                                spawnLoadout = bestLoadoutVtol;
                            }
                        }
                        else
                        {
                            if (Plugin.IsResupplyLimitReached(hq, "UtilityHelo1"))
                            {
                                Plugin.Log.LogInfo($"[SupplyBuffetMod] Ibis is responsible for dry resupply of '{requester.unitName}', but Active Ibis limit is reached. Aborting spawn without fallback.");
                                return;
                            }
                            if (helo != null && bestLoadoutHelo != null && spawnBase.CanSpawnAircraft(helo))
                            {
                                spawnDef = helo;
                                spawnLoadout = bestLoadoutHelo;
                            }
                        }
                        if (spawnDef != null && spawnLoadout != null)
                        {
                            int randomLivery = spawnDef.aircraftParameters.GetRandomLiveryForFaction(hq.faction);
                            var spawnResult = spawnBase.TrySpawnAircraft(null, spawnDef, new LiveryKey(randomLivery), spawnLoadout.loadout, spawnLoadout.FuelRatio);
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawning {spawnDef.unitName} (Loadout: {spawnLoadout.Name}) at {spawnBase.gameObject.name} for ground unit {requester.unitName}. Dist: {dist:F1}. Success: {spawnResult.Allowed}");
                        }
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid Airbase found for HQ {hq.faction?.factionName} to spawn helicopter for {requester.unitName}.");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"[SupplyBuffetMod] Could not find a munitions loadout for UtilityHelo1 or QuadVTOL1! Spawning aborted.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Ground Supply Run failed: {ex.Message}");
            }
            finally
            {
                IsSpawning = false;
            }
        }
        [HarmonyPatch(typeof(AircraftParameters), "GetRandomStandardLoadout")]
        public class Dry_AircraftParameters_GetRandomStandardLoadout_Patch
        {
            static bool Prefix(AircraftParameters __instance, AircraftDefinition definition, FactionHQ hq, ref StandardLoadout __result)
            {
                if (!IsSpawning) return true; 
                if (__instance.StandardLoadouts == null || __instance.StandardLoadouts.Length == 0) return true;
                var weaponManager = definition.unitPrefab.GetComponent<Aircraft>().weaponManager;
                foreach (var stdLoadout in __instance.StandardLoadouts)
                {
                    if (stdLoadout.disabled || !stdLoadout.AllowedByHQ(weaponManager, hq)) continue;
                    foreach (var mount in stdLoadout.loadout.weapons)
                    {
                        if (mount == null || mount.info == null) continue;
                        string key = Plugin.GetMountKey(mount);
                        if (key == null) continue;
                        if (!mount.info.rearmShip && (key == "MunitionsPallet1" || key == "MunitionsContainer1" || key == "MunitionsContainerx2"))
                        {
                            __result = stdLoadout;
                            return false; 
                        }
                    }
                }
                return true;
            }
        }
        [HarmonyPatch(typeof(Unit), "RegisterWeaponStation")]
        public class Dry_Unit_ExpressRearm_Patch
        {
            static void Postfix(Unit __instance, WeaponStation weaponStation)
            {
                if (Plugin.ExpressRearmEnabled.Value && __instance != null && weaponStation != null && weaponStation.Weapons != null)
                {
                    bool isShip = __instance.GetComponentInParent<Ship>() != null;
                    bool isVehicle = __instance.GetComponentInParent<GroundVehicle>() != null;
                    if (!isShip && isVehicle)
                    {
                        foreach (Weapon w in weaponStation.Weapons)
                        {
                            if (w != null)
                            {
                                w.RequestRearmLevel = 0.999f;
                                Plugin.Log.LogInfo($"[SupplyBuffetMod] ExpressRearm enabled for ground weapon '{w.name}' on '{__instance.unitName}'");
                            }
                        }
                    }
                }
            }
        }
    }
}