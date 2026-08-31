using System;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class CargoDemand
    {
        public static float FullLoadMass(Unit unit)
        {
            return SumMass(unit, fullLoad: true);
        }
        public static float MissingMass(Unit unit)
        {
            return SumMass(unit, fullLoad: false);
        }
        private static float SumMass(Unit unit, bool fullLoad)
        {
            if (unit == null || unit.weaponStations == null) return 0f;
            float mass = 0f;
            foreach (WeaponStation ws in unit.weaponStations)
            {
                if (ws == null || ws.WeaponInfo == null) continue;
                float massPerRound = ws.WeaponInfo.massPerRound;
                if (ws.WeaponInfo.cargo || massPerRound == 0f) continue;
                int rounds = fullLoad ? ws.FullAmmo : (ws.FullAmmo - ws.GetAmmoTotal());
                if (rounds > 0) mass += rounds * massPerRound;
            }
            return mass;
        }
        public static int ItemsToRelease(float demandMass, float perItemCapacity, int itemsAboard)
        {
            if (itemsAboard <= 0) return 0;
            if (perItemCapacity <= 0f || demandMass <= 0f) return 1;
            return Mathf.Clamp(Mathf.CeilToInt(demandMass / perItemCapacity), 1, itemsAboard);
        }
        public static int ItemsAboard(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.weaponStations == null) return 0;
            int items = 0;
            foreach (WeaponStation ws in aircraft.weaponStations)
            {
                if (ws != null && ws.WeaponInfo != null && ws.WeaponInfo.cargo && ws.Ammo > 0)
                {
                    items += ws.Ammo;
                }
            }
            return items;
        }
        public static bool IsPalletStick(string cargoUnitKey)
        {
            return !string.IsNullOrEmpty(cargoUnitKey)
                   && cargoUnitKey.IndexOf("MunitionsPallet2", StringComparison.Ordinal) >= 0;
        }
        public static float ItemCapacity(bool isWet, string cargoUnitKey)
        {
            if (!string.IsNullOrEmpty(cargoUnitKey))
            {
                if (cargoUnitKey.IndexOf("NavalSupplyContainer", StringComparison.Ordinal) >= 0)
                    return Value(Plugin.NavalContainerCapacity, 10000f);
                if (cargoUnitKey.IndexOf("NavalPallet", StringComparison.Ordinal) >= 0)
                    return Value(Plugin.NavalPalletCapacity, 6000f);
                if (cargoUnitKey.IndexOf("MunitionsContainer", StringComparison.Ordinal) >= 0)
                    return Value(Plugin.MunitionsContainerCapacity, 10000f);
                if (cargoUnitKey.IndexOf("MunitionsPallet2", StringComparison.Ordinal) >= 0)
                    return Value(Plugin.MunitionsPallet2Capacity, 1500f);
                if (cargoUnitKey.IndexOf("MunitionsPallet1", StringComparison.Ordinal) >= 0)
                    return Value(Plugin.MunitionsPalletCapacity, 6000f);
            }
            return isWet ? Value(Plugin.NavalContainerCapacity, 10000f)
                         : Value(Plugin.MunitionsContainerCapacity, 10000f);
        }
        private static float Value(BepInEx.Configuration.ConfigEntry<float> entry, float fallback)
        {
            return entry != null ? entry.Value : fallback;
        }
    }
}