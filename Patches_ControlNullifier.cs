// ============================================================================
// FILE: Patches_ControlNullifier.cs
// PURPOSE: Stabilizes the Chimera (AIFixedWingTransportState) post-drop flight
//          by zeroing roll/yaw input and locking heading for a fixed window
//          starting at the cargo release ("fire") moment.
//
// TRIGGERS:
//   - The 15s window itself is started directly by
//     AIFixedWingTransportState.DeployCargo(), which calls
//     Plugin.TriggerControlNullifier(aircraft, 15f) right after pilot.Fire().
//     No Harmony patch is used for that - patching it by method name is fragile,
//     since renaming the method silently breaks PatchAll() for the whole assembly.
//   - Aircraft_FilterInputs_Nullifier_Patch / Aircraft_FixedUpdate_Nullifier_Patch:
//     Enforce the nullification window on any AI-controlled aircraft while
//     Plugin.IsControlNullified() is true, by zeroing roll/yaw input and
//     snapping velocity back onto the locked heading each tick.
//
// NOTE: We do NOT patch AIHeloTransportState (vanilla helicopters).
//       The vanilla helicopter state machine transitions to AIHeloTakeoffState
//       naturally after deploying cargo - patching it caused fights between
//       our nullifier and the vanilla hover/takeoff autopilot, resulting in
//       erratic aircraft behavior. Vanilla handles Ibis/Tarantula post-drop fine.
// ============================================================================

using HarmonyLib;
using UnityEngine;

namespace SupplyBuffetMod
{
    /// <summary>
    /// Zeroes roll/yaw input on AI-controlled aircraft while control-nullified.
    /// Postfix only - a Prefix here is pointless because vanilla FilterInputs
    /// rewrites the inputs during the call, so only the Postfix can hold.
    /// Every body opens with Plugin.AnyControlNullified, a single float compare:
    /// these run on every aircraft every physics tick and the window is almost
    /// always inactive, so the per-instance lookup must not be the first thing done.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    public class Aircraft_FilterInputs_Nullifier_Patch
    {
        static void Postfix(Aircraft __instance)
        {
            if (!Plugin.AnyControlNullified) return;
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                var inputs = __instance.GetInputs();
                if (inputs != null)
                {
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                }
            }
        }
    }

    /// <summary>
    /// Zeroes roll/yaw input and snaps velocity onto the locked heading each physics
    /// tick while an AI-controlled aircraft is control-nullified.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    public class Aircraft_FixedUpdate_Nullifier_Patch
    {
        static void Prefix(Aircraft __instance)
        {
            if (!Plugin.AnyControlNullified) return;
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                var inputs = __instance.GetInputs();
                if (inputs != null)
                {
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                }
            }
        }

        static void Postfix(Aircraft __instance)
        {
            if (!Plugin.AnyControlNullified) return;
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                if (__instance.rb != null && Plugin.TryGetNullifiedVelocityDir(__instance, out Vector3 dir))
                {
                    float speed = __instance.rb.velocity.magnitude;
                    if (speed > 0.1f)
                    {
                        __instance.rb.velocity = dir * speed;
                    }
                }
            }
        }
    }
}
