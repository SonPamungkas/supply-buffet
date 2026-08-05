// ============================================================================
// FILE: Patches_InstantDispatch.cs
// PURPOSE: Triggers a resupply attempt the instant a unit registers as needing
//          rearm, instead of waiting for the next Plugin.PeriodicMonitor() poll
//          (up to 5s later).
//
// TRIGGERS:
//   - RearmMissionController_RegisterNeedsRearm_InstantDispatch_Patch: Postfix
//     on RearmMissionController.RegisterNeedsRearm. Same signal the checkpoint
//     build used (checkpoint/SupplyRunDry.cs, checkpoint/SupplyRunWet.cs), but
//     one class branching on Plugin.IsNavalUnit instead of two near-identical ones.
//
// SCOPE (deliberate): RegisterNeedsRearm is one-shot per rearm cycle. Vanilla
//   only reaches it from Unit.RequestRearm behind !HasRequestedRearm (Unit.cs:856),
//   and clears that flag only once EVERY station is full again (Unit.cs:1891-1893).
//   So this fires on a unit's first need per cycle; later top-ups fall back to the
//   5s poll. That is exactly the checkpoint's behaviour.
//
// EFFECTS:
//   - Calls the existing SupplyRunWet/SupplyRunDry entry points, which self-gate
//     via _lastSupplyRun/UnitCooldown, so this cannot cause duplicate or
//     rapid-fire dispatch - it only removes the polling latency.
//   - Aircraft are skipped: vanilla routes player aircraft (and countermeasure
//     reloads, CountermeasureManager.cs:110) through the same RequestRearm call,
//     and this system never air-drops to aircraft.
// ============================================================================

using System;
using HarmonyLib;

namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(RearmMissionController), "RegisterNeedsRearm")]
    public class RearmMissionController_RegisterNeedsRearm_InstantDispatch_Patch
    {
        static void Postfix(Unit requester)
        {
            if (requester == null || requester.disabled || requester is Aircraft) return;
            FactionHQ hq = requester.NetworkHQ;
            if (hq == null) return;
            if (ResupplyMissionManager.IsUnitAssignedOrQueued(requester)) return;
            try
            {
                if (Plugin.IsNavalUnit(requester))
                {
                    SupplyRunWet.HandleNavalResupply(hq, requester);
                }
                else
                {
                    SupplyRunDry.SpawnAirResupply(hq, requester);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in instant dispatch patch: {ex.Message}");
            }
        }
    }
}
