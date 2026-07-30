using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(FactionHQ), "RegisterFactionUnit")]
    public static class Patches_RegisterFactionUnit_AILimit
    {
        private static FieldInfo _activeAIAircraftField = typeof(FactionHQ).GetField("activeAIAircraft", BindingFlags.Instance | BindingFlags.NonPublic);
        static void Postfix(FactionHQ __instance, Unit unit)
        {
            if (__instance == null || unit == null) return;
            if (!Plugin.ExcludeLogisticsFromAILimit.Value) return;
            if (unit is Aircraft aircraft && aircraft.Player == null)
            {
                if (Plugin.IsLogisticAircraft(aircraft))
                {
                    if (_activeAIAircraftField?.GetValue(__instance) is List<Aircraft> list)
                    {
                        if (list.Contains(aircraft))
                        {
                            list.Remove(aircraft);
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Excluded logistic flight '{aircraft.unitName}' from FactionHQ AI Aircraft Limit (RegisterFactionUnit).");
                        }
                    }
                }
            }
        }
    }
    [HarmonyPatch(typeof(FactionHQ), "DeployAIAircraft")]
    public static class Patches_DeployAIAircraft_AILimit
    {
        private static FieldInfo _activeAIAircraftField = typeof(FactionHQ).GetField("activeAIAircraft", BindingFlags.Instance | BindingFlags.NonPublic);
        static void Prefix(FactionHQ __instance)
        {
            if (__instance == null) return;
            if (!Plugin.ExcludeLogisticsFromAILimit.Value) return;
            if (_activeAIAircraftField?.GetValue(__instance) is List<Aircraft> list)
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