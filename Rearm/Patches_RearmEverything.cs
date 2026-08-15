using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class ShipRearmHelper
    {
        public static void StampShipWeapons(Ship ship)
        {
            if (!Plugin.ExpressRearmEnabled.Value || ship == null) return;
            try
            {
                float level = Plugin.RearmRequestSensitivity.Value;
                if (ship.weaponStations == null) return;
                foreach (var ws in ship.weaponStations)
                {
                    if (ws == null || ws.Weapons == null) continue;
                    foreach (var w in ws.Weapons)
                    {
                        if (w == null) continue;
                        w.Rearmable = true;
                        w.RequestRearmLevel = level;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] StampShipWeapons error: {ex.Message}");
            }
        }
        public static void EnsureShipWeaponsRearmable(WeaponStation weaponStation, Unit owner)
        {
            if (!Plugin.ExpressRearmEnabled.Value || weaponStation == null || owner == null) return;
            try
            {
                Ship ship = owner as Ship ?? owner.GetComponentInParent<Ship>();
                if (ship == null) return;
                float level = Plugin.RearmRequestSensitivity.Value;
                if (weaponStation.Weapons == null) return;
                foreach (var w in weaponStation.Weapons)
                {
                    if (w == null) continue;
                    w.Rearmable = true;
                    w.RequestRearmLevel = level;
                }
            }
            catch { }
        }
    }
    public static class GroundRearmHelper
    {
        public static void StampGroundWeapons(Unit unit)
        {
            if (!Plugin.ExpressRearmGroundEnabled.Value || unit == null) return;
            try
            {
                float level = Plugin.RearmRequestSensitivity.Value;
                if (unit.weaponStations == null) return;
                foreach (var ws in unit.weaponStations)
                {
                    if (ws == null || ws.Weapons == null) continue;
                    foreach (var w in ws.Weapons)
                    {
                        if (w == null) continue;
                        w.Rearmable = true;
                        w.RequestRearmLevel = level;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] StampGroundWeapons error: {ex.Message}");
            }
        }
    }
    public static class RearmStampHelper
    {
        private static readonly ConditionalWeakTable<Unit, StrongBox<float>> LastStamp =
            new ConditionalWeakTable<Unit, StrongBox<float>>();
        public static void StampUnit(Unit unit)
        {
            if (unit == null) return;
            bool ship = unit is Ship;
            if (ship)
            {
                if (Plugin.ExpressRearmEnabled == null || !Plugin.ExpressRearmEnabled.Value) return;
            }
            else if (unit is GroundVehicle || unit is Building)
            {
                if (Plugin.ExpressRearmGroundEnabled == null || !Plugin.ExpressRearmGroundEnabled.Value) return;
            }
            else
            {
                return;
            }
            float throttle = (Plugin.StampThrottle != null) ? Plugin.StampThrottle.Value : 2f;
            if (throttle > 0f)
            {
                float now = Time.timeSinceLevelLoad;
                StrongBox<float> last = LastStamp.GetOrCreateValue(unit);
                if (last.Value != 0f && now - last.Value < throttle) return;
                last.Value = now;
            }
            if (ship) ShipRearmHelper.StampShipWeapons((Ship)unit);
            else GroundRearmHelper.StampGroundWeapons(unit);
        }
    }
    [HarmonyPatch(typeof(Unit), "InitializeUnit")]
    public class Unit_InitializeUnit_RearmEverything_Patch
    {
        static void Postfix(Unit __instance)
        {
            RearmStampHelper.StampUnit(__instance);
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
                if (ship == null) return;
                __instance.Rearmable = true;
                __instance.RequestRearmLevel = Plugin.RearmRequestSensitivity.Value;
            }
            catch { }
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    public class WeaponStation_ShipRearmEverything_Fire_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            RearmStampHelper.StampUnit(owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "LaunchMount")]
    public class WeaponStation_ShipRearmEverything_LaunchMount_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            RearmStampHelper.StampUnit(owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "RemoteFireAuto")]
    public class WeaponStation_ShipRearmEverything_RemoteFireAuto_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            RearmStampHelper.StampUnit(owner);
        }
    }
    [HarmonyPatch(typeof(WeaponStation), "RemoteFireSingle")]
    public class WeaponStation_ShipRearmEverything_RemoteFireSingle_Patch
    {
        static void Prefix(WeaponStation __instance, Unit owner)
        {
            RearmStampHelper.StampUnit(owner);
        }
    }
}