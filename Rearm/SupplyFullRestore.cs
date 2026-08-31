using System;
namespace SupplyBuffetMod
{
    public static class SupplyFullRestore
    {
        public static bool IsFullRestore(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (Has(name, "NavalSupplyContainer")) return Value(Plugin.FullRestoreNavalSupplyContainer1);
            if (Has(name, "MunitionsContainer")) return Value(Plugin.FullRestoreMunitionsContainer1);
            if (Has(name, "NavalPallet")) return Value(Plugin.FullRestoreNavalPallet1);
            if (Has(name, "MunitionsPallet2")) return Value(Plugin.FullRestoreMunitionsPallet2);
            if (Has(name, "MunitionsPallet1")) return Value(Plugin.FullRestoreMunitionsPallet1);
            return false;
        }
        public static bool IsFullRestore(Rearmer rearmer)
        {
            if (rearmer == null || rearmer.gameObject == null) return false;
            return IsFullRestore(rearmer.gameObject.name);
        }
        private static bool Has(string name, string token) => name.IndexOf(token, StringComparison.Ordinal) >= 0;
        private static bool Value(BepInEx.Configuration.ConfigEntry<bool> entry) => entry != null && entry.Value;
    }
}