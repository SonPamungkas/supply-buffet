using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
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
    public static class ChimeraHelper
    {
        private static AircraftDefinition _cachedChimeraDef;
        private static Dictionary<string, WeaponMount> _weaponMountCache;
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
        public const string RearCargoBay  = "Cargo Bay Rear";   
        public const string FrontCargoBay = "Cargo Bay Front";  
        public const string MissionBay    = "Mission Bay";      
        public const string WingPylons    = "Wing Pylons";      
        public const string WetContainerMount = "NavalSupplyContainerx1";
        public const string DryContainerMount = "MunitionsContainerx1";
        public const string DryPalletMount    = "MunitionsSmallPallet2x4";
        public const string DryTruckMount     = "Aryx_MC260_HLT-M";
        public const string JammerMount       = "JammingPod1";
        public static bool IsDrivingToRestock(Unit unit)
        {
            return unit is GroundVehicle gv && !gv.disabled
                && gv.TryGetComponent<RearmVehicleAI>(out RearmVehicleAI ai)
                && ai.GetStateName() == "Driving to Restock";
        }
        public static string SelectDryMount(Unit target)
        {
            if (IsDrivingToRestock(target)) return DryContainerMount;
            if (target is GroundVehicle vehicle)
            {
                return vehicle.GetHoldPosition() ? DryTruckMount : DryPalletMount;
            }
            return DryContainerMount;
        }
        public static Loadout CreateDynamicLoadout(Unit target, bool isWet, out string loadoutName, out string cargoMountKey)
        {
            var byStation = new Dictionary<string, WeaponMount>(StringComparer.Ordinal);
            if (isWet)
            {
                loadoutName = "Naval Supply Double";
                cargoMountKey = WetContainerMount;
                WeaponMount wetMount = ResolveSupplyMount(WetContainerMount);
                byStation[RearCargoBay]  = wetMount;
                byStation[FrontCargoBay] = wetMount;
            }
            else
            {
                cargoMountKey = SelectDryMount(target);
                WeaponMount dryMount = ResolveSupplyMount(cargoMountKey);
                if (cargoMountKey == DryTruckMount)
                {
                    loadoutName = "Munition Truck";
                    byStation[MissionBay] = dryMount;   
                }
                else if (cargoMountKey == DryPalletMount)
                {
                    loadoutName = "Small Pallet Octuple";
                    byStation[RearCargoBay]  = dryMount;
                    byStation[FrontCargoBay] = dryMount;
                }
                else
                {
                    loadoutName = "Munitions Container Double";
                    byStation[RearCargoBay]  = dryMount;
                    byStation[FrontCargoBay] = dryMount;
                }
            }
            WeaponMount jammer = GetWeaponMount(JammerMount);
            if (jammer != null) byStation[WingPylons] = jammer;
            return BuildLoadout(byStation);
        }
        private static WeaponMount ResolveSupplyMount(string key)
        {
            WeaponMount mount = GetWeaponMount(key);
            if (mount == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Supply mount '{key}' not found in the mount catalogue.");
                return null;
            }
            if (mount.info != null)
            {
                mount.info.rearmGround = true;
                mount.info.rearmShip   = true;
            }
            return mount;
        }
        private static Loadout BuildLoadout(Dictionary<string, WeaponMount> byStation)
        {
            var loadout = new Loadout { weapons = new List<WeaponMount>() };
            AircraftDefinition def = GetChimeraDefinition();
            WeaponManager manager = null;
            if (def != null && def.unitPrefab != null)
            {
                Aircraft prefabAircraft = def.unitPrefab.GetComponent<Aircraft>();
                if (prefabAircraft != null) manager = prefabAircraft.weaponManager;
            }
            if (manager == null || manager.hardpointSets == null)
            {
                Plugin.Log.LogWarning("[SupplyBuffetMod] Chimera hardpointSets unavailable; loadout cannot be built.");
                return loadout;
            }
            foreach (HardpointSet hardpoint in manager.hardpointSets)
            {
                WeaponMount mount = null;
                if (hardpoint != null) byStation.TryGetValue(hardpoint.name, out mount);
                loadout.weapons.Add(mount);
            }
            return loadout;
        }
    }
}