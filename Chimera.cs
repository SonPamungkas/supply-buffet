using HarmonyLib;
using System;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Hangar), "CanSpawnAircraft")]
    public class Hangar_CanSpawnAircraft_Patch
    {
        static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (definition != null && definition.jsonKey == "Aryx_CargoPlane1")
            {
                if (__instance.gameObject.name.IndexOf("hangar_med", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    __result = true;
                }
            }
        }
    }
    [HarmonyPatch(typeof(SupplyRunDry), "SpawnAirResupply")]
    public class SupplyRunDry_SpawnAirResupply_Patch
    {
        private static AircraftDefinition _cachedChimeraDef;
        public static System.Collections.Generic.Dictionary<Hangar, float> _hangarSpawnTimes = new System.Collections.Generic.Dictionary<Hangar, float>();
        private static System.Collections.Generic.Dictionary<string, WeaponMount> _weaponMountCache;
        public static AircraftDefinition GetChimeraDefinition()
        {
            if (_cachedChimeraDef == null)
            {
                var allAircraft = UnityEngine.Resources.FindObjectsOfTypeAll<AircraftDefinition>();
                _cachedChimeraDef = System.Linq.Enumerable.FirstOrDefault(allAircraft, a => a != null && a.jsonKey == "Aryx_CargoPlane1");
                if (_cachedChimeraDef != null)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Cached Chimera definition: {_cachedChimeraDef.jsonKey}");
                }
            }
            return _cachedChimeraDef;
        }
        public static WeaponMount GetWeaponMount(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_weaponMountCache == null)
            {
                _weaponMountCache = new System.Collections.Generic.Dictionary<string, WeaponMount>(StringComparer.OrdinalIgnoreCase);
                foreach (var mount in UnityEngine.Resources.FindObjectsOfTypeAll<WeaponMount>())
                {
                    if (mount != null && !string.IsNullOrEmpty(mount.jsonKey))
                    {
                        _weaponMountCache[mount.jsonKey] = mount;
                    }
                    if (mount != null && !string.IsNullOrEmpty(mount.name) && !_weaponMountCache.ContainsKey(mount.name))
                    {
                        _weaponMountCache[mount.name] = mount;
                    }
                }
            }
            _weaponMountCache.TryGetValue(key, out WeaponMount result);
            return result;
        }
        static bool Prefix(FactionHQ hq, Unit requester)
        {
            var chimera = GetChimeraDefinition();
            if (chimera == null) return true;
            int randomChoice = UnityEngine.Random.Range(0, 3);
            string containerKey = "";
            string loadoutName = "";
            if (randomChoice == 0)
            {
                containerKey = "MunitionsContainerx1";
                loadoutName = "Container Double";
            }
            else if (randomChoice == 1)
            {
                containerKey = "MunitionsPallet2x2";
                loadoutName = "Pallet Quadruple";
            }
            else
            {
                containerKey = "MunitionsPallet2x4";
                loadoutName = "Pallet Octo";
            }
            WeaponMount payloadMount = GetWeaponMount(containerKey);
            if (payloadMount == null) 
            {
                payloadMount = GetWeaponMount("MunitionsPallet1x1");
                if (payloadMount == null) payloadMount = GetWeaponMount("MunitionsContainerx1");
            }
            if (payloadMount != null && payloadMount.info != null)
            {
                payloadMount.info.rearmGround = true;
                payloadMount.info.rearmShip = true;
            }
            NuclearOption.SavedMission.Loadout bestLoadout = new NuclearOption.SavedMission.Loadout();
            bestLoadout.weapons = new System.Collections.Generic.List<WeaponMount>();
            bestLoadout.weapons.Add(null);
            bestLoadout.weapons.Add(payloadMount);
            bestLoadout.weapons.Add(payloadMount);
            while (bestLoadout.weapons.Count < 8)
            {
                bestLoadout.weapons.Add(null);
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod][Dry] Generated dynamic loadout: {loadoutName}");
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
                if (ab != null && !ab.disabled && ab.CurrentHQ == hq && HasHangarMed(ab))
                {
                    spawnBase = ab;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][Dry] Found hangar_med base in HQ: {spawnBase.gameObject.name}");
                    break;
                }
            }
            if (spawnBase == null)
            {
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ != null && ab.CurrentHQ.faction == hq.faction && HasHangarMed(ab))
                    {
                        spawnBase = ab;
                        Plugin.Log.LogInfo($"[SupplyBuffetMod][Dry] Found other friendly hangar_med base: {spawnBase.gameObject.name}");
                        break;
                    }
                }
            }
            if (spawnBase == null)
            {
                Plugin.Log.LogInfo("[SupplyBuffetMod][Dry] No hangar_med base found, checking CanSpawnAircraft.");
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ == hq && ab.CanSpawnAircraft(chimera))
                    {
                        spawnBase = ab;
                        Plugin.Log.LogInfo($"[SupplyBuffetMod][Dry] Found fallback base: {spawnBase.gameObject.name}");
                        break;
                    }
                }
            }
            if (spawnBase != null)
            {
                float dist = UnityEngine.Vector3.Distance(spawnBase.transform.position, requester.transform.position);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Evaluating Dry Chimera for {requester.unitName}: dist={dist:F1} (need > 14000), spawnBase={spawnBase.gameObject.name}");
                if (dist > 14000f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Enqueueing Dry Chimera spawn for {requester.unitName}.");
                    Plugin.SpawnQueue.Enqueue(new Plugin.ChimeraSpawnRequest
                    {
                        HQ = hq,
                        Requester = requester,
                        ChimeraDef = chimera,
                        Loadout = bestLoadout,
                        LoadoutName = loadoutName,
                        IsWet = false,
                        RequestTime = UnityEngine.Time.timeSinceLevelLoad
                    });
                    SupplyRunDry.IsSpawning = false;
                    return false;
                }
                else
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][Dry] Distance {dist:F1} is not > 14000, skipping Chimera.");
                }
            }
            else
            {
                Plugin.Log.LogInfo("[SupplyBuffetMod][Dry] No valid spawn base found for Chimera.");
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(SupplyRunWet), "HandleNavalResupply")]
    public class SupplyRunWet_SpawnAirResupply_Patch
    {
        static bool Prefix(FactionHQ hq, Unit requester)
        {
            var chimera = SupplyRunDry_SpawnAirResupply_Patch.GetChimeraDefinition();
            if (chimera == null) return true;
            string loadoutName = "Container Double";
            WeaponMount payloadMount = SupplyRunDry_SpawnAirResupply_Patch.GetWeaponMount("NavalContainer1x1");
            if (payloadMount == null) 
            {
                payloadMount = SupplyRunDry_SpawnAirResupply_Patch.GetWeaponMount("NavalSupplyContainerx1");
            }
            if (payloadMount != null && payloadMount.info != null)
            {
                payloadMount.info.rearmGround = true;
                payloadMount.info.rearmShip = true;
            }
            NuclearOption.SavedMission.Loadout bestLoadout = new NuclearOption.SavedMission.Loadout();
            bestLoadout.weapons = new System.Collections.Generic.List<WeaponMount>();
            bestLoadout.weapons.Add(null);
            bestLoadout.weapons.Add(payloadMount);
            bestLoadout.weapons.Add(payloadMount);
            while (bestLoadout.weapons.Count < 8)
            {
                bestLoadout.weapons.Add(null);
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod][Wet] Generated dynamic loadout: {loadoutName}");
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
                if (ab != null && !ab.disabled && ab.CurrentHQ == hq && HasHangarMed(ab))
                {
                    spawnBase = ab;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][Wet] Found hangar_med base in HQ: {spawnBase.gameObject.name}");
                    break;
                }
            }
            if (spawnBase == null)
            {
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ != null && ab.CurrentHQ.faction == hq.faction && HasHangarMed(ab))
                    {
                        spawnBase = ab;
                        Plugin.Log.LogInfo($"[SupplyBuffetMod][Wet] Found other friendly hangar_med base: {spawnBase.gameObject.name}");
                        break;
                    }
                }
            }
            if (spawnBase == null)
            {
                Plugin.Log.LogInfo("[SupplyBuffetMod][Wet] No hangar_med base found, checking CanSpawnAircraft.");
                foreach (var ab in airbases)
                {
                    if (ab != null && !ab.disabled && ab.CurrentHQ == hq && ab.CanSpawnAircraft(chimera))
                    {
                        spawnBase = ab;
                        Plugin.Log.LogInfo($"[SupplyBuffetMod][Wet] Found fallback base: {spawnBase.gameObject.name}");
                        break;
                    }
                }
            }
            if (spawnBase != null)
            {
                float dist = UnityEngine.Vector3.Distance(spawnBase.transform.position, requester.transform.position);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Evaluating Wet Chimera for {requester.unitName}: dist={dist:F1} (need > 14000), spawnBase={spawnBase.gameObject.name}");
                if (dist > 14000f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Enqueueing Wet Chimera spawn for {requester.unitName}.");
                    Plugin.SpawnQueue.Enqueue(new Plugin.ChimeraSpawnRequest
                    {
                        HQ = hq,
                        Requester = requester,
                        ChimeraDef = chimera,
                        Loadout = bestLoadout,
                        LoadoutName = loadoutName,
                        IsWet = true,
                        RequestTime = UnityEngine.Time.timeSinceLevelLoad
                    });
                    SupplyRunWet.IsSpawning = false;
                    return false;
                }
                else
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][Wet] Distance {dist:F1} is not > 14000, skipping Chimera.");
                }
            }
            else
            {
                Plugin.Log.LogInfo("[SupplyBuffetMod][Wet] No valid spawn base found for Chimera.");
            }
            return true;
        }
    [HarmonyPatch(typeof(WeaponManager), "OrganizeWeaponStations")]
    public class WeaponManager_OrganizeWeaponStations_Patch
    {
        static void Postfix(WeaponManager __instance)
        {
            var aircraft = __instance.GetComponent<Aircraft>();
            if (aircraft != null && aircraft.unitName != null && aircraft.unitName.Contains("Chimera"))
            {
                if (aircraft.weaponStations != null)
                {
                    foreach (var ws in aircraft.weaponStations)
                    {
                        if (ws != null && ws.WeaponInfo != null && (ws.WeaponInfo.rearmShip || ws.WeaponInfo.rearmGround))
                        {
                            __instance.currentWeaponStation = ws;
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Forced currentWeaponStation to {ws.WeaponInfo.weaponName} for {aircraft.unitName}");
                            break;
                        }
                    }
                }
            }
        }
    }
}
}