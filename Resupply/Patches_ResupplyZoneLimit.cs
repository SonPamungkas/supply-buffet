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
            if (key != "UtilityHelo1" && key != "QuadVTOL1") return;
            if (ResupplyMissionManager.IsUnitAssignedOrQueued(target, aircraft))
            {
                if (Plugin.Dbg)
                    Plugin.Log.LogInfo($"[SB|P7] {aircraft.definition.unitName} rejected target {target.unitName}: another transport is already delivering to it.");
                Reject(__instance);
                return;
            }
            float dist = Vector3.Distance(aircraft.transform.position, target.transform.position);
            float limit;
            if (key == "UtilityHelo1")
            {
                limit = Plugin.ThresholdA.Value;
            }
            else
            {
                limit = Plugin.ThresholdB.Value;
                if (Plugin.IsExtendedZoneTarget(target) && LaunchedFromShip(aircraft))
                {
                    limit *= 3f;
                }
            }
            if (dist > limit)
            {
                if (Plugin.Dbg)
                    Plugin.Log.LogInfo($"[SB|P7] {aircraft.definition.unitName} rejected target {target.unitName} (Dist: {dist:F0} > limit {limit:F0}).");
                Reject(__instance);
                return;
            }
            ResupplyMissionManager.AssignTransport(target, aircraft);
        }
        private static bool LaunchedFromShip(Aircraft aircraft)
        {
            if (!ResupplyCensus.TryGetHomeBase(aircraft, out Airbase home)) return false;
            return home.TryGetAttachedUnit(out Unit attached) && attached is Ship;
        }
        private static void Reject(AIHeloTransportState state)
        {
            TargetSearchResultsRef(state) = new CombatAI.TargetSearchResults();
            CurrentTargetRef(state) = null;
        }
    }
}