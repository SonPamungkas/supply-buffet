
using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(AIPilotCombatModes), "FixedUpdateState")]
    public static class Patches_FixedWingTransport
    {
        static bool Prefix(AIPilotCombatModes __instance, Pilot pilot)
        {
            try
            {
                if (pilot == null || pilot.aircraft == null || pilot.aircraft.weaponStations == null)
                    return true;
                if (TransportFaultGuard.IsFaulted(pilot.aircraft)) return true;
                if (Patches_AryxChimera.AryxPresent && !ResupplyCensus.WasDispatchedByMod(pilot.aircraft)) return true;
                if (ResupplyCensus.WasDispatchedByMod(pilot.aircraft)
                    && CargoDemand.ItemsAboard(pilot.aircraft) == 0)
                {
                    Plugin.Log.LogInfo($"[SB|P8] {pilot.aircraft.unitName} is empty; taking it back from combat AI so it does not fly a jamming pattern.");
                    pilot.SwitchState(new AIFixedWingTransportState(pilot.aircraft));
                    return false;
                }
                foreach (WeaponStation weaponStation in pilot.aircraft.weaponStations)
                {
                    if (weaponStation != null && weaponStation.Cargo && weaponStation.Ammo > 0 && Time.timeSinceLevelLoad - pilot.flightInfo.LastCargoDelivery > 15f)
                    {
                        Plugin.Log.LogInfo($"[SB|P8] Fixed-wing aircraft {pilot.aircraft.unitName} has active cargo! Switching to AIFixedWingTransportState.");
                        pilot.SwitchState(new AIFixedWingTransportState(pilot.aircraft));
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] {pilot.aircraft.unitName} completed the switch into AIFixedWingTransportState.");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Exception routing a fixed-wing transport; leaving it to vanilla AI. {ex}");
                if (pilot != null) TransportFaultGuard.Report(pilot.aircraft, "AIFixedWingTransportState (entry)", ex);
                return true;
            }
        }
    }
}