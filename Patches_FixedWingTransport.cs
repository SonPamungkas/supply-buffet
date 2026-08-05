// ============================================================================
// FILE: Patches_FixedWingTransport.cs
// PURPOSE: Hooks up custom AI fixed-wing transport state for Chimera aircraft.
//
// TRIGGERS:
//   - Patches_FixedWingTransport: Prefix on AIPilotCombatModes.FixedUpdateState.
//
// EFFECTS:
//   - When an AI pilot operates a fixed-wing aircraft carrying ground or naval
//     resupply containers, switches the pilot's state to AIFixedWingTransportState.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(AIPilotCombatModes), "FixedUpdateState")]
    public static class Patches_FixedWingTransport
    {
        static bool Prefix(AIPilotCombatModes __instance, Pilot pilot)
        {
            if (pilot == null || pilot.aircraft == null || pilot.aircraft.weaponStations == null)
                return true;

            foreach (WeaponStation weaponStation in pilot.aircraft.weaponStations)
            {
                if (weaponStation != null && weaponStation.Cargo && weaponStation.Ammo > 0 && Time.timeSinceLevelLoad - pilot.flightInfo.LastCargoDelivery > 15f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Fixed-wing aircraft {pilot.aircraft.unitName} has active cargo! Switching to AIFixedWingTransportState.");
                    pilot.SwitchState(new AIFixedWingTransportState(pilot.aircraft));
                    return false;
                }
            }
            return true;
        }
    }
}
