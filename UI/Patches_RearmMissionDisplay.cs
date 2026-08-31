using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(RearmMissionController), "ManageRearmMissions")]
    public static class Patches_RearmMissionDisplay
    {
        static void Postfix(RearmMissionController __instance)
        {
            try
            {
                if (!__instance.IsServer) return;
                if (__instance.RearmersWithMissions == null) return;
                if (UnitRegistry.allAircraft == null) return;
                foreach (Aircraft aircraft in UnitRegistry.allAircraft)
                {
                    if (aircraft == null || aircraft.disabled) continue;
                    if (!ResupplyCensus.WasDispatchedByMod(aircraft)) continue;
                    if (aircraft.NetworkHQ == null) continue;
                    if (aircraft.NetworkHQ.RearmMissionController != __instance) continue;
                    Unit target = AssignedTargetOf(aircraft);
                    if (target == null || target.disabled) continue;
                    __instance.RearmersWithMissions.Add(
                        new RearmMissionController.RearmerWithMission(aircraft, target));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Could not add mod sorties to the rearm map display: {ex.Message}");
            }
        }
        internal static Unit AssignedTargetOf(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null) return null;
            for (int i = 0; i < aircraft.pilots.Length; i++)
            {
                Pilot pilot = aircraft.pilots[i];
                if (pilot == null) continue;
                if (pilot.currentState is AIFixedWingTransportState transport) return transport.AssignedTarget;
            }
            return null;
        }
        internal static AIFixedWingTransportState TransportStateOf(Aircraft aircraft)
        {
            if (aircraft == null || aircraft.pilots == null) return null;
            for (int i = 0; i < aircraft.pilots.Length; i++)
            {
                Pilot pilot = aircraft.pilots[i];
                if (pilot == null) continue;
                if (pilot.currentState is AIFixedWingTransportState transport) return transport;
            }
            return null;
        }
    }
}