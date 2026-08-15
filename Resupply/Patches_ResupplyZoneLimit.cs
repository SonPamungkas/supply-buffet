using HarmonyLib;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(AIHeloTransportState), "TargetSearch")]
    public class Patches_ResupplyZoneLimit
    {
        private static readonly AccessTools.FieldRef<AIHeloTransportState, Aircraft> AircraftRef =
            AccessTools.FieldRefAccess<AIHeloTransportState, Aircraft>("aircraft");
        private static readonly AccessTools.FieldRef<AIHeloTransportState, CombatAI.TargetSearchResults> TargetSearchResultsRef =
            AccessTools.FieldRefAccess<AIHeloTransportState, CombatAI.TargetSearchResults>("targetSearchResults");
        private static readonly AccessTools.FieldRef<AIHeloTransportState, Unit> CurrentTargetRef =
            AccessTools.FieldRefAccess<AIHeloTransportState, Unit>("currentTarget");
        static void Postfix(AIHeloTransportState __instance)
        {
            if (__instance == null) return;
            Aircraft aircraft = AircraftRef(__instance);
            if (aircraft == null || aircraft.definition == null) return;
            Unit target = TargetSearchResultsRef(__instance).target;
            if (target == null) return;
            string key = aircraft.definition.jsonKey;
            if (key == "UtilityHelo1" || key == "QuadVTOL1")
            {
                float dist = Vector3.Distance(aircraft.transform.position, target.transform.position);
                if (key == "UtilityHelo1" && dist > Plugin.ThresholdA.Value)
                {
                    if (Plugin.DebugLogging.Value)
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] Ibis rejected target {target.unitName} (Dist: {dist} > ThresholdA {Plugin.ThresholdA.Value})");
                    TargetSearchResultsRef(__instance) = new CombatAI.TargetSearchResults();
                    CurrentTargetRef(__instance) = null;
                }
                else if (key == "QuadVTOL1")
                {
                    float limit = Plugin.ThresholdB.Value;
                    if (Plugin.IsExtendedZoneTarget(target))
                    {
                        limit *= 3f;
                    }
                    if (dist > limit)
                    {
                        if (Plugin.DebugLogging.Value)
                            Plugin.Log.LogInfo($"[SupplyBuffetMod] Tarantula rejected target {target.unitName} (Dist: {dist} > ThresholdB {limit})");
                        TargetSearchResultsRef(__instance) = new CombatAI.TargetSearchResults();
                        CurrentTargetRef(__instance) = null;
                    }
                }
            }
        }
    }
}