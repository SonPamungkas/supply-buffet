using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    public static class SupplyRunWet
    {
        public static bool IsSpawning = false;
        public static Dictionary<Unit, float> _lastSupplyRun = new Dictionary<Unit, float>();
        [HarmonyPatch(typeof(RearmMissionController), "RegisterNeedsRearm")]
        public class Wet_SupplyRun_Notify_Patch
        {
            static void Postfix(RearmMissionController __instance, Unit requester)
            {
                if (!Plugin.ExpressRearmEnabled.Value || requester == null) return;
                bool isShip = requester.GetComponentInParent<Ship>() != null;
                var turret = requester.GetComponentInParent<Turret>();
                if (turret != null)
                {
                    Unit attached = turret.GetAttachedUnit();
                    if (attached != null && attached.GetComponentInParent<Ship>() != null)
                        isShip = true;
                }
                if (!isShip) return;
                if (ResupplyMissionManager.IsUnitAssignedOrQueued(requester)) return;
                if (_lastSupplyRun.TryGetValue(requester, out float lastRun))
                {
                    if (Time.timeSinceLevelLoad - lastRun < 60f) return;
                }
                _lastSupplyRun[requester] = Time.timeSinceLevelLoad;
                FactionHQ hq = __instance.GetComponentInParent<FactionHQ>();
                if (hq == null) hq = requester.NetworkHQ;
                if (hq == null) return;
                HandleNavalResupply(hq, requester);
            }
        }
        public static void HandleNavalResupply(FactionHQ hq, Unit requester)
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
                            if (mount.info.rearmShip || k == "NavalPallet1" || k == "NavalSupplyContainer1" || k == "NavalSupplyContainerx2")
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
                        return n.IndexOf("Aryx_SupplyShip1", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Helipad", StringComparison.OrdinalIgnoreCase) >= 0;
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
                        AircraftDefinition spawnDef = helo;
                        StandardLoadout spawnLoadout = bestLoadoutHelo;
                        if (dist > 10000f && vtol != null && bestLoadoutVtol != null && spawnBase.CanSpawnAircraft(vtol))
                        {
                            spawnDef = vtol;
                            spawnLoadout = bestLoadoutVtol;
                        }
                        else if ((helo == null || bestLoadoutHelo == null || !spawnBase.CanSpawnAircraft(helo)) && vtol != null && bestLoadoutVtol != null && spawnBase.CanSpawnAircraft(vtol))
                        {
                            spawnDef = vtol;
                            spawnLoadout = bestLoadoutVtol;
                        }
                        if (spawnDef != null && spawnLoadout != null)
                        {
                            int randomLivery = spawnDef.aircraftParameters.GetRandomLiveryForFaction(hq.faction);
                            var spawnResult = spawnBase.TrySpawnAircraft(null, spawnDef, new LiveryKey(randomLivery), spawnLoadout.loadout, spawnLoadout.FuelRatio);
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawning {spawnDef.unitName} (Loadout: {spawnLoadout.Name}) at {spawnBase.gameObject.name} for ship {requester.unitName}. Dist: {dist:F1}. Success: {spawnResult.Allowed}");
                        }
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid Airbase found for HQ {hq.faction?.factionName} to spawn helicopter for {requester.unitName}.");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"[SupplyBuffetMod] Could not find a naval loadout for UtilityHelo1 or QuadVTOL1! Spawning aborted.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Naval Supply Run failed: {ex.Message}");
            }
            finally
            {
                IsSpawning = false;
            }
        }
        [HarmonyPatch(typeof(AircraftParameters), "GetRandomStandardLoadout")]
        public class Wet_AircraftParameters_GetRandomStandardLoadout_Patch
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
                        if (mount.info.rearmShip || key == "NavalPallet1" || key == "NavalSupplyContainer1" || key == "NavalSupplyContainerx2")
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
        public class Wet_Unit_ExpressRearm_Patch
        {
            static void Postfix(Unit __instance, WeaponStation weaponStation)
            {
                if (Plugin.ExpressRearmEnabled.Value && __instance != null && weaponStation != null && weaponStation.Weapons != null)
                {
                    Ship ship = __instance.GetComponentInParent<Ship>();
                    if (ship != null && !string.IsNullOrEmpty(ship.unitName))
                    {
                        string shipName = ship.unitName;
                        bool rearmEverything = Plugin.GetShipRearmEverythingConfig(shipName);
                        foreach (Weapon w in weaponStation.Weapons)
                        {
                            if (w != null)
                            {
                                w.RequestRearmLevel = 0.999f;
                                if (rearmEverything)
                                {
                                    w.Rearmable = true;
                                }
                                Plugin.Log.LogInfo($"[SupplyBuffetMod] ExpressRearm enabled (Rearmable={w.Rearmable}) for naval weapon '{w.name}' on '{shipName}'");
                            }
                        }
                    }
                }
            }
        }
    }
}