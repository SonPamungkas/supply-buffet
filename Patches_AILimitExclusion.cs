// ============================================================================
// FILE: Patches_AILimitExclusion.cs
// PURPOSE: Excludes logistics/resupply aircraft from faction AI population caps.
//
// TRIGGERS:
//   - Patches_RegisterFactionUnit_AILimit: Postfix on FactionHQ.RegisterFactionUnit.
//   - Patches_DeployAIAircraft_AILimit: Prefix on FactionHQ.DeployAIAircraft.
//
// EFFECTS:
//   - When a logistics aircraft (Ibis, Tarantula, Chimera) is registered with an HQ,
//     it is removed from the activeAIAircraft list so it doesn't count against faction AI limits.
// ============================================================================

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
            if (Plugin.ExcludeLogisticsFromAILimit == null || !Plugin.ExcludeLogisticsFromAILimit.Value) return;

            if (unit is Aircraft aircraft && aircraft.Player == null)
            {
                if (Plugin.IsLogisticAircraft(aircraft))
                {
                    List<Aircraft> list = FactionHQFields.ActiveAIAircraftRef(__instance);
                    if (list != null && list.Contains(aircraft))
                    {
                        list.Remove(aircraft);
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] Excluded logistic flight '{aircraft.unitName}' from FactionHQ AI Aircraft Limit (RegisterFactionUnit).");
                    }
                }
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
                    if (aircraft == null || aircraft.disabled || Plugin.IsLogisticAircraft(aircraft))
                    {
                        if (aircraft != null && !aircraft.disabled)
                        {
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Removed logistic flight '{aircraft.unitName}' from FactionHQ AI Aircraft Limit before DeployAIAircraft.");
                        }
                        list.RemoveAt(i);
                    }
                }
            }
        }
    }
}
