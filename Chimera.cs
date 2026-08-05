// ============================================================================
// FILE: Chimera.cs
// PURPOSE: Enables medium hangars to spawn the Chimera (Aryx_CargoPlane1) and
//          provides dynamic loadout construction for Chimera supply missions.
//
// TRIGGERS:
//   - Hangar_CanSpawnAircraft_Patch: Postfix on Hangar.CanSpawnAircraft.
//     Returns true for Aryx_CargoPlane1 when the hangar is named "hangar_med".
//
// EFFECTS:
//   - Overrides Hangar.CanSpawnAircraft so medium hangars can spawn the Chimera.
//   - Caches the AircraftDefinition for Aryx_CargoPlane1 and all WeaponMounts.
//   - Generates a custom 8-station supply loadout for Dry (Munition Truck on WS3)
//     and Wet (Naval Container on WS1+WS2) missions.
// ============================================================================
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    /// <summary>
    /// Enables medium hangars ("hangar_med") to spawn Aryx_CargoPlane1 (Chimera).
    /// </summary>
    [HarmonyPatch(typeof(Hangar), "CanSpawnAircraft")]
    public class Hangar_CanSpawnAircraft_Patch
    {
        static void Postfix(Hangar __instance, AircraftDefinition definition, ref bool __result)
        {
            if (definition != null && definition.jsonKey == "Aryx_CargoPlane1")
            {
                if (__instance.gameObject != null &&
                    __instance.gameObject.name.IndexOf("hangar_med", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    __result = true;
                }
            }
        }
    }
    /// <summary>
    /// Helper utilities for Chimera aircraft definition caching and loadout construction.
    /// All base-distance and naval-detection logic lives in ResupplySpawner.
    /// </summary>
    public static class ChimeraHelper
    {
        private static AircraftDefinition _cachedChimeraDef;
        private static Dictionary<string, WeaponMount> _weaponMountCache;
        /// <summary>
        /// Caches and returns the AircraftDefinition for Aryx_CargoPlane1.
        /// </summary>
        public static AircraftDefinition GetChimeraDefinition()
        {
            if (_cachedChimeraDef == null)
            {
                var allAircraft = Resources.FindObjectsOfTypeAll<AircraftDefinition>();
                _cachedChimeraDef = allAircraft.FirstOrDefault(a => a != null && a.jsonKey == "Aryx_CargoPlane1");
                if (_cachedChimeraDef != null)
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Cached Chimera definition: {_cachedChimeraDef.jsonKey}");
            }
            return _cachedChimeraDef;
        }
        /// <summary>
        /// Caches and retrieves a WeaponMount by jsonKey or name.
        /// </summary>
        public static WeaponMount GetWeaponMount(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (_weaponMountCache == null)
            {
                _weaponMountCache = new Dictionary<string, WeaponMount>(StringComparer.OrdinalIgnoreCase);
                foreach (var mount in Resources.FindObjectsOfTypeAll<WeaponMount>())
                {
                    if (mount != null && !string.IsNullOrEmpty(mount.jsonKey))
                        _weaponMountCache[mount.jsonKey] = mount;
                    if (mount != null && !string.IsNullOrEmpty(mount.name) && !_weaponMountCache.ContainsKey(mount.name))
                        _weaponMountCache[mount.name] = mount;
                }
            }
            _weaponMountCache.TryGetValue(key, out WeaponMount result);
            return result;
        }
        /// <summary>
        /// Creates a dynamic 8-station supply loadout for Aryx_CargoPlane1.
        ///   Dry: Aryx_MC260_HLT-M (Munition Truck) on WS3.
        ///   Wet: NavalContainer1x1 on WS1 and WS2.
        /// </summary>
        public static Loadout CreateDynamicLoadout(bool isWet, out string loadoutName)
        {
            if (isWet)
            {
                loadoutName = "Naval Supply Double";
                WeaponMount wetMount = GetWeaponMount("NavalContainer1x1") ?? GetWeaponMount("NavalSupplyContainerx1");
                if (wetMount?.info != null)
                {
                    wetMount.info.rearmGround = true;
                    wetMount.info.rearmShip   = true;
                }
                Loadout wetLoadout = new Loadout { weapons = new List<WeaponMount>() };
                wetLoadout.weapons.Add(null);     // WS0
                wetLoadout.weapons.Add(wetMount); // WS1
                wetLoadout.weapons.Add(wetMount); // WS2
                while (wetLoadout.weapons.Count < 8) wetLoadout.weapons.Add(null);
                return wetLoadout;
            }
            else
            {
                loadoutName = "Munition Truck";
                string containerKey = "Aryx_MC260_HLT-M";
                WeaponMount dryMount = GetWeaponMount(containerKey);
                if (dryMount?.info != null)
                {
                    dryMount.info.rearmGround = true;
                    dryMount.info.rearmShip   = true;
                }
                Loadout dryLoadout = new Loadout { weapons = new List<WeaponMount>() };
                dryLoadout.weapons.Add(null);     // WS0
                dryLoadout.weapons.Add(null);     // WS1
                dryLoadout.weapons.Add(null);     // WS2
                dryLoadout.weapons.Add(dryMount); // WS3
                while (dryLoadout.weapons.Count < 8) dryLoadout.weapons.Add(null);
                return dryLoadout;
            }
        }
    }
}
