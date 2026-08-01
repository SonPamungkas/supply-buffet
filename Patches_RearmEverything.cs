using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class ShipRearmHelper
    {
        public static void EnsureShipWeaponsRearmable(WeaponStation weaponStation, Unit owner)
        {
            if (!Plugin.ExpressRearmEnabled.Value || weaponStation == null || weaponStation.Weapons == null || owner == null) return;
            try
            {
                Ship ship = owner as Ship ?? owner.GetComponentInParent<Ship>();
                if (ship != null)
                {
                    string shipName = Plugin.GetShipName(ship);
                    if (!string.IsNullOrEmpty(shipName) && Plugin.GetShipRearmEverythingConfig(shipName))
                    {
                        foreach (var w in weaponStation.Weapons)
                        {
                            if (w != null)
                            {
                                w.RequestRearmLevel = 0.999f;
                                w.Rearmable = true;
                            }
                        }
                    }
                }
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(Unit), "InitializeUnit")]
    public class Unit_InitializeUnit_ShipScan_Patch
    {
        static void Postfix(Unit __instance)
        {
            if (!Plugin.ExpressRearmEnabled.Value || __instance == null) return;
            try
            {
                Ship ship = __instance as Ship ?? __instance.GetComponentInParent<Ship>();
                if (ship != null)
                {
                    string shipName = Plugin.GetShipName(ship);
                    if (!string.IsNullOrEmpty(shipName))
                    {
                        bool rearmEverything = Plugin.GetShipRearmEverythingConfig(shipName);
                        if (ship.weaponStations != null)
                        {
                            foreach (var ws in ship.weaponStations)
                            {
                                if (ws?.Weapons != null)
                                {
                                    foreach (var w in ws.Weapons)
                                    {
                                        if (w != null)
                                        {
                                            w.RequestRearmLevel = 0.999f;
                                            if (rearmEverything)
                                            {
                                                w.Rearmable = true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in Unit_InitializeUnit_ShipScan_Patch: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Weapon), "AttachToUnit")]
    public class Weapon_AttachToUnit_RearmEverything_Patch
    {
        static void Postfix(Weapon __instance, Unit unit)
        {
            if (!Plugin.ExpressRearmEnabled.Value || __instance == null || unit == null) return;
            try
            {
                Ship ship = unit as Ship ?? unit.GetComponentInParent<Ship>();
                if (ship != null)
                {
                    string shipName = Plugin.GetShipName(ship);
                    if (!string.IsNullOrEmpty(shipName) && Plugin.GetShipRearmEverythingConfig(shipName))
                    {
                        __instance.RequestRearmLevel = 0.999f;
                        __instance.Rearmable = true;
                    }
                }
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    public class WeaponStation_ShipRearmEverything_Fire_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            ShipRearmHelper.EnsureShipWeaponsRearmable(__instance, owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "LaunchMount")]
    public class WeaponStation_ShipRearmEverything_LaunchMount_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            ShipRearmHelper.EnsureShipWeaponsRearmable(__instance, owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "RemoteFireAuto")]
    public class WeaponStation_ShipRearmEverything_RemoteFireAuto_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            ShipRearmHelper.EnsureShipWeaponsRearmable(__instance, owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "RemoteFireSingle")]
    public class WeaponStation_ShipRearmEverything_RemoteFireSingle_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            ShipRearmHelper.EnsureShipWeaponsRearmable(__instance, owner);
        }
    }
}