using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class ResupplyMissionManager
    {
        public static Dictionary<Unit, Aircraft> AssignedTransports = new Dictionary<Unit, Aircraft>();
        internal static void ResetForNewLevel()
        {
            AssignedTransports.Clear();
        }
        private static readonly List<Unit> PruneScratch = new List<Unit>();
        private static readonly ConditionalWeakTable<Unit, StrongBox<float>> CoveredSince =
            new ConditionalWeakTable<Unit, StrongBox<float>>();
        private static bool CoveredByExistingRearmer(RearmMissionController controller, Unit unit)
        {
            if (Plugin.ClusterCoverageSuppressionEnabled != null && !Plugin.ClusterCoverageSuppressionEnabled.Value) return false;
            if (controller == null || unit == null) return false;
            if (!controller.TryGetRearmer(unit, out _))
            {
                CoveredSince.Remove(unit);
                return false;
            }
            float now = Time.timeSinceLevelLoad;
            StrongBox<float> since = CoveredSince.GetValue(unit, _ => new StrongBox<float>(now));
            float grace = Plugin.Cfg(Plugin.ClusterCoverageGrace, 90f);
            if (grace > 0f && now - since.Value >= grace) return false;
            return true;
        }
        public static bool IsUnitAssignedOrQueued(Unit unit, Aircraft ignoreAircraft = null)
        {
            if (unit == null || unit.disabled) return false;
            if (ChimeraSpawnQueue.Contains(unit)) return true;
            if (AssignedTransports.TryGetValue(unit, out Aircraft assigned))
            {
                if (assigned != null && !assigned.disabled && assigned != ignoreAircraft)
                {
                    return true;
                }
            }
            return false;
        }
        public static bool IsAssignedToResupply(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            foreach (var kvp in AssignedTransports)
            {
                if (kvp.Value == aircraft) return true;
            }
            return false;
        }
        public static void AssignTransport(Unit target, Aircraft transport)
        {
            if (target == null || transport == null) return;
            AssignedTransports[target] = transport;
        }
        public static void UnassignTransport(Unit target)
        {
            if (target == null) return;
            AssignedTransports.Remove(target);
        }
        public static bool TryGetUnassignedUnitNeedingRearm(RearmMissionController controller, bool ships, bool vehicles, Aircraft requestingAircraft, out Unit target, SortieCategory? requiredDryCategory = null, GlobalPosition? homeBase = null, float minRangeFromHome = 0f)
        {
            target = null;
            if (controller == null || controller.UnitsNeedingRearm == null) return false;
            float maxMissing = 0f;
            for (int i = controller.UnitsNeedingRearm.Count - 1; i >= 0; i--)
            {
                Unit unit = controller.UnitsNeedingRearm[i];
                if (unit == null || unit.disabled)
                {
                    controller.UnitsNeedingRearm.RemoveAt(i);
                    continue;
                }
                if (unit.radarAlt > 10f) continue;
                if (unit is Aircraft)
                {
                    continue;
                }
                bool isNaval = Plugin.IsNavalUnit(unit);
                if ((ships && isNaval) || (vehicles && !isNaval))
                {
                    if (IsUnitAssignedOrQueued(unit, requestingAircraft))
                    {
                        continue;
                    }
                    if (CoveredByExistingRearmer(controller, unit))
                    {
                        continue;
                    }
                    bool blocked = (requestingAircraft != null)
                        ? ResupplyDispatcher.IsRecentlyServed(unit)
                        : ResupplyDispatcher.IsOnCooldown(unit);
                    if (blocked)
                    {
                        continue;
                    }
                    if (requiredDryCategory.HasValue && !isNaval
                        && ChimeraHelper.DryCategoryFor(unit) != requiredDryCategory.Value)
                    {
                        if (Plugin.Dbg)
                        {
                            Plugin.Log.LogInfo($"[SB|P8] {unit.unitName} skipped: needs {ChimeraHelper.DryCategoryFor(unit)} loadout, transport carries {requiredDryCategory.Value}.");
                        }
                        continue;
                    }
                    if (homeBase.HasValue && minRangeFromHome > 0f && !isNaval
                        && FastMath.Distance(unit.GlobalPosition(), homeBase.Value) < minRangeFromHome)
                    {
                        if (Plugin.Dbg)
                        {
                            Plugin.Log.LogInfo($"[SB|P8] {unit.unitName} skipped: {FastMath.Distance(unit.GlobalPosition(), homeBase.Value):F0}m from the transport's base, inside the {minRangeFromHome:F0}m minimum.");
                        }
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
            if (target != null && Plugin.Dbg)
            {
                Plugin.Log.LogInfo($"[SB|P3] ResupplyMissionManager picked needy unit: {target.unitName} (Missing ammo fraction: {maxMissing:F2})");
            }
            return target != null;
        }
        public static bool TryGetUnassignedUnitsNeedingRearm(RearmMissionController controller, out Unit shipTarget, out Unit groundTarget)
        {
            shipTarget = null;
            groundTarget = null;
            if (controller == null || controller.UnitsNeedingRearm == null) return false;
            float maxMissingShip = 0f;
            float maxMissingGround = 0f;
            for (int i = controller.UnitsNeedingRearm.Count - 1; i >= 0; i--)
            {
                Unit unit = controller.UnitsNeedingRearm[i];
                if (unit == null || unit.disabled)
                {
                    controller.UnitsNeedingRearm.RemoveAt(i);
                    continue;
                }
                if (unit.radarAlt > 10f) continue;
                if (unit is Aircraft) continue;
                if (IsUnitAssignedOrQueued(unit, null)) continue;
                if (CoveredByExistingRearmer(controller, unit)) continue;
                if (ResupplyDispatcher.IsOnCooldown(unit)) continue;
                bool isNaval = Plugin.IsNavalUnit(unit);
                float missing = unit.GetAmmoValue().Missing;
                if (isNaval)
                {
                    if (missing > maxMissingShip) { shipTarget = unit; maxMissingShip = missing; }
                }
                else
                {
                    if (missing > maxMissingGround) { groundTarget = unit; maxMissingGround = missing; }
                }
            }
            if (Plugin.Dbg)
            {
                if (shipTarget != null) Plugin.Log.LogInfo($"[SB|P3] ResupplyMissionManager picked needy ship: {shipTarget.unitName} (Missing ammo fraction: {maxMissingShip:F2})");
                if (groundTarget != null) Plugin.Log.LogInfo($"[SB|P3] ResupplyMissionManager picked needy ground unit: {groundTarget.unitName} (Missing ammo fraction: {maxMissingGround:F2})");
            }
            return shipTarget != null || groundTarget != null;
        }
        public static bool TryGetRestockingSupplyVehicle(RearmMissionController controller, Aircraft requestingAircraft, out Unit target)
        {
            target = null;
            if (controller == null || controller.Rearmers == null) return false;
            for (int i = 0; i < controller.Rearmers.Count; i++)
            {
                Rearmer rearmer = controller.Rearmers[i];
                if (rearmer == null) continue;
                Unit unit = rearmer.Unit;
                if (!ChimeraHelper.IsDrivingToRestock(unit)) continue;
                if (IsUnitAssignedOrQueued(unit, requestingAircraft)) continue;
                if ((requestingAircraft != null)
                    ? ResupplyDispatcher.IsRecentlyServed(unit)
                    : ResupplyDispatcher.IsOnCooldown(unit)) continue;
                target = unit;
                return true;
            }
            return false;
        }
        public static void Update()
        {
            PruneScratch.Clear();
            foreach (var kvp in AssignedTransports)
            {
                Aircraft transport = kvp.Value;
                if (kvp.Key == null || kvp.Key.disabled || transport == null || transport.disabled)
                {
                    PruneScratch.Add(kvp.Key);
                }
            }
            for (int i = 0; i < PruneScratch.Count; i++)
            {
                Unit unit = PruneScratch[i];
                if (unit != null && !unit.disabled)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Assigned transport for '{unit.unitName}' was destroyed or disabled! Removing assignment.");
                }
                AssignedTransports.Remove(unit);
            }
            PruneScratch.Clear();
        }
    }
}