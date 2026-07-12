using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(FactionHQ), "NotifyRequiresRearm")]
    public class SupplyRun_Notify_Patch
    {
        private static Dictionary<Unit, float> _lastSupplyRun = new Dictionary<Unit, float>();
        static void Postfix(FactionHQ __instance, Unit unit)
        {
            if (!Plugin.AdaptiveSupplyEnabled.Value) return;
            if (unit == null) return;
            if (!(unit is Ship) && !(unit is GroundVehicle)) return;
            if (_lastSupplyRun.TryGetValue(unit, out float lastRun))
            {
                if (Time.timeSinceLevelLoad - lastRun < 60f)
                {
                    return;
                }
            }
            _lastSupplyRun[unit] = Time.timeSinceLevelLoad;
            var helo = Encyclopedia.i.aircraft.FirstOrDefault(a => a.jsonKey == "UtilityHelo1");
            var vtol = Encyclopedia.i.aircraft.FirstOrDefault(a => a.jsonKey == "QuadVTOL1");
            if (helo != null) __instance.AddSupplyUnit(helo, 1);
            if (vtol != null) __instance.AddSupplyUnit(vtol, 1);
            Plugin.ForceSpawnInProgress = true;
            Plugin.ForceSpawnIsNaval = unit is Ship;
            try
            {
                if (helo != null && helo.aircraftParameters.StandardLoadouts != null)
                {
                    StandardLoadout bestLoadout = null;
                    foreach (var stdLoadout in helo.aircraftParameters.StandardLoadouts)
                    {
                        if (stdLoadout.disabled) continue;
                        foreach (var mount in stdLoadout.loadout.weapons)
                        {
                            if (mount == null || mount.info == null) continue;
                            string k = Plugin.GetMountKey(mount);
                            if (k == null) continue;
                            if (Plugin.ForceSpawnIsNaval && (k == "NavalPallet1" || k == "NavalSupplyContainer1"))
                                bestLoadout = stdLoadout;
                            else if (!Plugin.ForceSpawnIsNaval && (k == "MunitionsPallet1" || k == "MunitionsContainer1"))
                                bestLoadout = stdLoadout;
                        }
                    }
                    if (bestLoadout != null)
                    {
                        Airbase spawnBase = null;
                        var airbases = FactionRegistry.airbaseLookup.Values;
                        foreach (var ab in airbases)
                        {
                            if (ab != null && !ab.disabled && ab.CurrentHQ == __instance)
                            {
                                string n = ab.gameObject.name;
                                if (n.IndexOf("Aryx_SupplyShip1", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                    n.IndexOf("Supply Ship", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                    n.IndexOf("Helipad", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                    n.IndexOf("Atlas", StringComparison.OrdinalIgnoreCase) >= 0)
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
                                if (ab != null && !ab.disabled && ab.CurrentHQ == __instance)
                                {
                                    spawnBase = ab;
                                    break;
                                }
                            }
                        }
                        if (spawnBase != null)
                        {
                            int randomLivery = helo.aircraftParameters.GetRandomLiveryForFaction(__instance.faction);
                            spawnBase.TrySpawnAircraft(null, helo, new LiveryKey(randomLivery), bestLoadout.loadout, bestLoadout.FuelRatio);
                        }
                        else if (Plugin.DebugLogging.Value)
                        {
                            Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid airbase found for HQ {__instance.faction?.factionName}.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Spawn failed: {ex.Message}");
            }
            finally
            {
                Plugin.ForceSpawnInProgress = false;
            }
        }
    }
    [HarmonyPatch(typeof(AircraftParameters), "GetRandomStandardLoadout")]
    public class AircraftParameters_GetRandomStandardLoadout_Patch
    {
        static bool Prefix(AircraftParameters __instance, AircraftDefinition definition, FactionHQ hq, ref StandardLoadout __result)
        {
            if (!Plugin.ForceSpawnInProgress) return true; 
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
                    if (Plugin.ForceSpawnIsNaval)
                    {
                        if (mount.info.rearmShip || key == "NavalPallet1" || key == "NavalSupplyContainer1")
                        {
                            __result = stdLoadout;
                            return false; 
                        }
                    }
                    else
                    {
                        if (!mount.info.rearmShip && (key == "MunitionsPallet1" || key == "MunitionsContainer1"))
                        {
                            __result = stdLoadout;
                            return false; 
                        }
                    }
                }
            }
            return true;
        }
    }
}
