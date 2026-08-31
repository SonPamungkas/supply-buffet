using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    internal static class FactionHQFields
    {
        public static readonly AccessTools.FieldRef<FactionHQ, List<Aircraft>> ActiveAIAircraftRef =
            AccessTools.FieldRefAccess<FactionHQ, List<Aircraft>>("activeAIAircraft");
    }
    [HarmonyPatch(typeof(FactionHQ), "RegisterFactionUnit")]
    public static class Patches_RegisterFactionUnit_AILimit
    {
        static void Postfix(FactionHQ __instance, Unit unit)
        {
            if (__instance == null || unit == null) return;
            if (!(unit is Aircraft aircraft) || aircraft.Player != null) return;
            ResupplyCensus.OnAircraftRegistered(__instance, aircraft);
            AirbaseRepairManager.TryClaimPendingAircraft(__instance, aircraft);
            if (Plugin.ExcludeLogisticsFromAILimit == null || !Plugin.ExcludeLogisticsFromAILimit.Value) return;
            if (!Plugin.IsModDispatchedFlight(aircraft)) return;
            List<Aircraft> list = FactionHQFields.ActiveAIAircraftRef(__instance);
            if (list != null && list.Remove(aircraft))
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Excluded mod-dispatched flight '{aircraft.unitName}' from FactionHQ AI Aircraft Limit (RegisterFactionUnit).");
            }
        }
    }
    [HarmonyPatch(typeof(FactionHQ), "DeployAIAircraft")]
    public static class Patches_DeployAIAircraft_AILimit
    {
        static void Prefix(FactionHQ __instance)
        {
            if (__instance == null) return;
            if (Plugin.ExcludeLogisticsFromAILimit == null || !Plugin.ExcludeLogisticsFromAILimit.Value) return;
            List<Aircraft> list = FactionHQFields.ActiveAIAircraftRef(__instance);
            if (list != null)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    Aircraft aircraft = list[i];
                    if (aircraft == null || aircraft.disabled || Plugin.IsModDispatchedFlight(aircraft))
                    {
                        if (aircraft != null && !aircraft.disabled)
                        {
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Removed mod-dispatched flight '{aircraft.unitName}' from FactionHQ AI Aircraft Limit before DeployAIAircraft.");
                        }
                        list.RemoveAt(i);
                    }
                }
            }
        }
    }
}