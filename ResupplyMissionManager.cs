// ============================================================================
// FILE: ResupplyMissionManager.cs
// PURPOSE: Tracks units needing rearmament and manages active Chimera assignments.
//
// TRIGGERS:
//   - TryGetUnassignedUnitNeedingRearm: Called by periodic monitor and AI transport state.
//   - Update: Called per frame to prune dead/destroyed Chimeras and handle stuck drop timeouts.
//
// EFFECTS:
//   - Filters unit lists to prevent assigning multiple resupply aircraft to the same unit.
//   - Tracks cargo drop timestamps to time out failed deliveries after 45 seconds.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SupplyBuffetMod
{
    /// <summary>
    /// Central manager for finding target units requiring ammunition and tracking
    /// active or queued Chimera resupply assignments.
    /// </summary>
    public static class ResupplyMissionManager
    {
        // Tracks which unit is currently assigned to which active Chimera aircraft
        public static Dictionary<Unit, Aircraft> AssignedChimeras = new Dictionary<Unit, Aircraft>();

        // Tracks when cargo was dropped for a target unit to detect stuck deliveries
        public static Dictionary<Unit, float> DroppedCargoTimes = new Dictionary<Unit, float>();

        // Reused by Update so the maintenance pass allocates nothing per call.
        private static readonly List<Unit> PruneScratch = new List<Unit>();

        /// <summary>
        /// Checks if a unit already has a pending Chimera request or an active Chimera assigned.
        /// </summary>
        /// <param name="unit">The unit to check.</param>
        /// <param name="ignoreAircraft">Optional aircraft to ignore in active assignment check.</param>
        public static bool IsUnitAssignedOrQueued(Unit unit, Aircraft ignoreAircraft = null)
        {
            if (unit == null || unit.disabled) return false;

            if (ChimeraSpawnQueue.Contains(unit)) return true;

            // Check if unit is actively assigned to an existing Chimera
            if (AssignedChimeras.TryGetValue(unit, out Aircraft assigned))
            {
                if (assigned != null && !assigned.disabled && assigned != ignoreAircraft)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks whether the specified aircraft is currently assigned to resupply any target unit.
        /// </summary>
        public static bool IsAssignedToResupply(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            foreach (var kvp in AssignedChimeras)
            {
                if (kvp.Value == aircraft) return true;
            }
            return false;
        }

        /// <summary>
        /// Registers a Chimera aircraft as assigned to resupply a target unit.
        /// </summary>
        public static void AssignChimera(Unit target, Aircraft chimera)
        {
            if (target == null || chimera == null) return;
            AssignedChimeras[target] = chimera;
        }

        /// <summary>
        /// Removes a target unit's active Chimera assignment.
        /// </summary>
        public static void UnassignChimera(Unit target)
        {
            if (target == null) return;
            AssignedChimeras.Remove(target);
        }

        /// <summary>
        /// Records the level load timestamp when cargo was dropped for a target unit.
        /// </summary>
        public static void RegisterDrop(Unit target, float time)
        {
            if (target == null) return;
            DroppedCargoTimes[target] = time;
        }

        /// <summary>
        /// True while a unit is within its post-drop cooldown window (UnitCooldown
        /// seconds since its last airdrop). Used purely as a dispatch-side filter so
        /// a unit isn't re-selected for a new resupply mission while it's still
        /// travelling to reach what was already dropped for it; vanilla rearm state
        /// (HasRequestedRearm / UnitsNeedingRearm / actual pickup) is never touched.
        /// </summary>
        public static bool IsInPostDropCooldown(Unit unit)
        {
            if (unit == null) return false;
            return DroppedCargoTimes.TryGetValue(unit, out float dropTime)
                && Time.timeSinceLevelLoad - dropTime < Plugin.UnitCooldown.Value;
        }

        /// <summary>
        /// Searches the HQ's RearmMissionController for the unit with the highest missing ammunition
        /// that is not already assigned or queued for resupply. Prefers units outside their post-drop
        /// cooldown (for distribution), but falls back to any still-valid candidate (ignoring cooldown)
        /// if that yields nothing, so the aircraft never idles while a genuine rearm request exists.
        /// TRIGGERS: Periodic monitor in Plugin.cs and AIFixedWingTransportState.
        /// </summary>
        /// <param name="controller">The faction HQ's RearmMissionController.</param>
        /// <param name="ships">True to search for naval vessels (Wet resupply).</param>
        /// <param name="vehicles">True to search for ground vehicles/structures (Dry resupply).</param>
        /// <param name="requestingAircraft">Optional aircraft asking for assignment.</param>
        /// <param name="target">Out parameter receiving the chosen unit needing ammunition.</param>
        public static bool TryGetUnassignedUnitNeedingRearm(RearmMissionController controller, bool ships, bool vehicles, Aircraft requestingAircraft, out Unit target)
        {
            if (TryFindUnitNeedingRearm(controller, ships, vehicles, requestingAircraft, respectPostDropCooldown: true, out target))
            {
                return true;
            }
            return TryFindUnitNeedingRearm(controller, ships, vehicles, requestingAircraft, respectPostDropCooldown: false, out target);
        }

        private static bool TryFindUnitNeedingRearm(RearmMissionController controller, bool ships, bool vehicles, Aircraft requestingAircraft, bool respectPostDropCooldown, out Unit target)
        {
            target = null;
            if (controller == null || controller.UnitsNeedingRearm == null) return false;

            float maxMissing = 0f;
            for (int i = controller.UnitsNeedingRearm.Count - 1; i >= 0; i--)
            {
                Unit unit = controller.UnitsNeedingRearm[i];
                if (unit == null || unit.disabled || unit.radarAlt > 10f)
                {
                    controller.UnitsNeedingRearm.RemoveAt(i);
                    continue;
                }

                // Never resupply airborne or grounded aircraft using this system
                if (unit is Aircraft)
                {
                    continue;
                }

                // Strictly separate naval units (Wet) from ground units/structures (Dry)
                bool isNaval = Plugin.IsNavalUnit(unit);
                if ((ships && isNaval) || (vehicles && !isNaval))
                {
                    if (IsUnitAssignedOrQueued(unit, requestingAircraft))
                    {
                        continue;
                    }
                    if (respectPostDropCooldown && IsInPostDropCooldown(unit))
                    {
                        continue;
                    }

                    float missing = unit.GetAmmoValue().Missing;
                    if (missing > maxMissing)
                    {
                        target = unit;
                        maxMissing = missing;
                    }
                }
            }

            return target != null;
        }

        /// <summary>
        /// Periodic maintenance loop pruning dead assignments and handling stuck deliveries.
        /// </summary>
        public static void Update(float currentTime)
        {
            // Prune assignments where the Chimera OR the target unit died. Dropping dead
            // keys matters: a Unit key left behind roots the destroyed unit for the session.
            PruneScratch.Clear();
            foreach (var kvp in AssignedChimeras)
            {
                Aircraft chimera = kvp.Value;
                if (kvp.Key == null || kvp.Key.disabled || chimera == null || chimera.disabled)
                {
                    PruneScratch.Add(kvp.Key);
                }
            }
            for (int i = 0; i < PruneScratch.Count; i++)
            {
                Unit unit = PruneScratch[i];
                if (unit != null && !unit.disabled)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Assigned Chimera for '{unit.unitName}' was destroyed or disabled! Removing assignment.");
                }
                AssignedChimeras.Remove(unit);
            }

            // Prune expired post-drop cooldown entries. This is purely a dispatch-side
            // cache (see IsInPostDropCooldown) - vanilla rearm state is never touched,
            // so pickup behaves exactly as vanilla intends.
            PruneScratch.Clear();
            foreach (var kvp in DroppedCargoTimes)
            {
                if (kvp.Key == null || kvp.Key.disabled || currentTime - kvp.Value > Plugin.UnitCooldown.Value)
                {
                    PruneScratch.Add(kvp.Key);
                }
            }
            for (int i = 0; i < PruneScratch.Count; i++) DroppedCargoTimes.Remove(PruneScratch[i]);
            PruneScratch.Clear();
        }
    }
}
