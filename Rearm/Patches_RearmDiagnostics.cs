using HarmonyLib;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "ProcessRearmRequest")]
    public static class Rearmer_ProcessRearmRequest_Diagnostics_Patch
    {
        [HarmonyPriority(Priority.First)]
        static void Prefix(Rearmer __instance, Unit unitToRearm, out float __state)
        {
            __state = (__instance != null) ? __instance.Capacity : 0f;
        }
        static void Postfix(Rearmer __instance, Unit unitToRearm, float __state, bool __result)
        {
            if (Plugin.DebugLogging == null || !Plugin.DebugLogging.Value) return;
            if (__instance == null || unitToRearm == null || unitToRearm.weaponStations == null) return;
            string rearmerName = (__instance.Unit != null) ? __instance.Unit.unitName : "?";
            Plugin.Log.LogInfo($"[SupplyBuffetMod][Rearm] '{rearmerName}' -> '{unitToRearm.unitName}': granted={__result}, capacity {__state:F0} -> {__instance.Capacity:F0} (spent {__state - __instance.Capacity:F0}).");
            for (int i = 0; i < unitToRearm.weaponStations.Count; i++)
            {
                WeaponStation ws = unitToRearm.weaponStations[i];
                if (ws == null || ws.WeaponInfo == null) continue;
                WeaponInfo info = ws.WeaponInfo;
                int loaded = ws.GetAmmoTotal();
                int deficit = ws.FullAmmo - loaded;
                string verdict;
                if (info.cargo || info.massPerRound == 0f) verdict = "skipped (cargo or zero mass)";
                else if (deficit <= 0) verdict = "no deficit";
                else if (info.nuclear) verdict = "nuclear - needs warheads AND the container inside an airbase radius";
                else if (__state < info.massPerRound) verdict = "mass budget exhausted before this station";
                else verdict = "served or partially served";
                Plugin.Log.LogInfo($"[SupplyBuffetMod][Rearm]   WS{i} '{info.weaponName}' ammo={loaded}/{ws.FullAmmo} deficit={deficit} mass/rd={info.massPerRound} cost/rd={info.costPerRound} gun={info.gun} nuclear={info.nuclear} rearmable={IsStationRearmable(ws)} -> {verdict}");
            }
        }
        private static bool IsStationRearmable(WeaponStation ws)
        {
            if (ws.Weapons == null) return false;
            foreach (Weapon w in ws.Weapons)
            {
                if (w != null && w.Rearmable) return true;
            }
            return false;
        }
    }
}