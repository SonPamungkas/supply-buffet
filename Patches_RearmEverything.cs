// ============================================================================
// FILE: Patches_RearmEverything.cs
// PURPOSE: Allows naval vessels and supply sources to rearm all weapon types
//          regardless of standard rearm restrictions.
//
// APPROACH:
//   - Patch typeof(Ship) directly so ALL ship instances are caught regardless
//     of name. This avoids the old name-based filtering that silently excluded
//     modded or renamed ships.
//   - On every Unit.RequestRearm call for a Ship, force all weapon instances on
//     that ship to Rearmable=true and set RequestRearmLevel from config so that
//     rearming sources will supply them unconditionally.
//   - On Weapon.AttachToUnit for any Ship-hosted weapon, same treatment so
//     weapons added dynamically (e.g. strike missiles just loaded) are covered
//     the moment they attach.
// ============================================================================

using System;
using HarmonyLib;
using UnityEngine;

namespace SupplyBuffetMod
{
    // -----------------------------------------------------------------------
    // Helper: stamps all weapons on a Ship as rearmable at the configured level.
    // Called from multiple patches to ensure coverage at every relevant moment.
    // -----------------------------------------------------------------------
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
                    if (ws?.Weapons == null) continue;
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

        // Also call from WeaponStation helpers for dynamically-attached weapons
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

    // -----------------------------------------------------------------------
    // Helper: stamps all weapons on a GroundVehicle or Building as rearmable.
    // Same logic as StampShipWeapons but gated by ExpressRearmGroundEnabled.
    // -----------------------------------------------------------------------
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
                    if (ws?.Weapons == null) continue;
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

    // -----------------------------------------------------------------------
    // Patch typeof(Unit).InitializeUnit — guard with `is Ship` for naval,
    // `is GroundVehicle` or `is Building` for dry-supply targets.
    // -----------------------------------------------------------------------
    [HarmonyPatch(typeof(Unit), "InitializeUnit")]
    public class Unit_InitializeUnit_RearmEverything_Patch
    {
        static void Postfix(Unit __instance)
        {
            if (__instance is Ship ship)
                ShipRearmHelper.StampShipWeapons(ship);
            else if (__instance is GroundVehicle || __instance is Building)
                GroundRearmHelper.StampGroundWeapons(__instance);
        }
    }

    // NOTE: there is deliberately no patch on Unit.RequestRearm here. Vanilla calls it
    // on every fire event that leaves ammo below RequestRearmLevel (WeaponStation.cs:451
    // /466/483/522) and on every countermeasure release (CountermeasureManager.cs:110),
    // so re-stamping there walked every station x every weapon per shot to rewrite two
    // fields that were already set. InitializeUnit covers the initial stamp and
    // Weapon.AttachToUnit covers weapons that load later, which is the full set of cases.

    // -----------------------------------------------------------------------
    // Stamp each Weapon as it attaches to a unit that is (or is part of) a
    // Ship. Handles strike missiles and other weapons that load dynamically.
    // -----------------------------------------------------------------------
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

    // -----------------------------------------------------------------------
    // Re-stamp at each fire event so weapons never drift back to non-rearmable.
    // -----------------------------------------------------------------------
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
