using System;
using HarmonyLib;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(FactionHQ), "NotifyNeedsRepair")]
    public static class Patches_InstantRepair
    {
        static void Postfix(FactionHQ __instance, Unit unit)
        {
            if (__instance == null || unit == null) return;
            try
            {
                AirbaseRepairManager.TryDispatchRepair(__instance, unit);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in instant repair patch: {ex.Message}");
            }
        }
    }
}