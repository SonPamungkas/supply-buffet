// ============================================================================
// FILE: SupplyRunWet.cs
// PURPOSE: Entry point and state tracker for Wet (naval) resupply missions.
//
// TRIGGERS:
//   - Periodic monitor in Plugin.Update detects a ship or naval vessel needing ammunition.
//
// EFFECTS:
//   - Delegates aircraft selection and spawning to ResupplySpawner.TriggerResupply.
//   - Tracks last resupply times to enforce UnitCooldown between missions.
// ============================================================================

using System.Runtime.CompilerServices;
using UnityEngine;

namespace SupplyBuffetMod
{
    /// <summary>
    /// Handles Wet (naval vessel) resupply scheduling and cooldown tracking.
    /// </summary>
    public static class SupplyRunWet
    {
        // ConditionalWeakTable, not Dictionary<Unit, float>: a dictionary keyed by Unit
        // roots every destroyed requester for the whole session and never shrinks.
        public static readonly ConditionalWeakTable<Unit, StrongBox<float>> _lastSupplyRun =
            new ConditionalWeakTable<Unit, StrongBox<float>>();

        /// <summary>
        /// Triggers a naval air resupply mission for a ship if the unit is off cooldown.
        /// </summary>
        /// <param name="hq">The FactionHQ providing resupply.</param>
        /// <param name="requester">The naval unit needing ammunition.</param>
        public static void HandleNavalResupply(FactionHQ hq, Unit requester)
        {
            if (hq == null || requester == null || requester.disabled)
            {
                return;
            }

            // Enforce unit cooldown to prevent rapid-fire rearm loops
            if (_lastSupplyRun.TryGetValue(requester, out StrongBox<float> lastTime))
            {
                if (Time.timeSinceLevelLoad - lastTime.Value < Plugin.UnitCooldown.Value)
                {
                    return;
                }
            }

            _lastSupplyRun.GetOrCreateValue(requester).Value = Time.timeSinceLevelLoad;

            // Delegate all decision logic (distance, thresholds, limits, aircraft selection) to ResupplySpawner
            ResupplySpawner.TriggerResupply(hq, requester, isWet: true);
        }
    }
}
