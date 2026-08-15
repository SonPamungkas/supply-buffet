using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(AIHeloCombatState), "AssessHQTargets")]
    public static class Patches_HeloSpecialTransport
    {
        private static readonly AccessTools.FieldRef<PilotBaseState, Pilot> PilotRef =
            AccessTools.FieldRefAccess<PilotBaseState, Pilot>("pilot");
        private static readonly AccessTools.FieldRef<PilotBaseState, Aircraft> AircraftRef =
            AccessTools.FieldRefAccess<PilotBaseState, Aircraft>("aircraft");
        private const int MAX_RTB_REDIRECTS = 5;
        private static readonly ConditionalWeakTable<Aircraft, StrongBox<int>> RtbRedirects =
            new ConditionalWeakTable<Aircraft, StrongBox<int>>();
        static bool Prefix(AIHeloCombatState __instance)
        {
            try
            {
                return Route(__instance);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Exception routing a helo transport; leaving it to vanilla AI. {ex}");
                if (__instance != null) TransportFaultGuard.Report(AircraftRef(__instance), "AIHeloSpecialTransportState (entry)", ex);
                return true;
            }
        }
        private static bool Route(AIHeloCombatState __instance)
        {
            if (__instance == null) return true;
            Aircraft aircraft = AircraftRef(__instance);
            Pilot pilot = PilotRef(__instance);
            if (aircraft == null || pilot == null || aircraft.weaponStations == null) return true;
            if (TransportFaultGuard.IsFaulted(aircraft)) return true;
            if (!AirbaseRepairManager.AssignedRepairs.TryGetValue(aircraft, out Unit target) || target == null)
            {
                return TryReturnToBase(aircraft, pilot);
            }
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station != null && station.Cargo && station.Ammo > 0 &&
                    Time.timeSinceLevelLoad - pilot.flightInfo.LastCargoDelivery > 15f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} is tasked to repair '{target.unitName}'. Switching to AIHeloSpecialTransportState.");
                    pilot.SwitchState(new AIHeloSpecialTransportState(aircraft));
                    return false;
                }
            }
            return TryReturnToBase(aircraft, pilot);
        }
        private static bool TryReturnToBase(Aircraft aircraft, Pilot pilot)
        {
            if (aircraft.Player != null) return true;
            if (!ResupplyCensus.WasDispatchedByMod(aircraft)) return true;
            if (CargoDemand.ItemsAboard(aircraft) != 0) return true;
            if (pilot.AIHeloLandingState == null) return true;
            StrongBox<int> attempts = RtbRedirects.GetOrCreateValue(aircraft);
            if (attempts.Value >= MAX_RTB_REDIRECTS) return true;
            attempts.Value++;
            if (attempts.Value == 1 || attempts.Value == MAX_RTB_REDIRECTS)
            {
                string note = (attempts.Value == MAX_RTB_REDIRECTS)
                    ? " (final attempt; leaving it to vanilla after this)"
                    : string.Empty;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} has delivered its cargo; returning to base instead of taking a combat target{note}.");
            }
            pilot.SwitchState(pilot.AIHeloLandingState);
            return false;
        }
    }
}