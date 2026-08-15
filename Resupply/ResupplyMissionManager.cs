using System.Collections.Generic;
namespace SupplyBuffetMod
{
    public static class ResupplyMissionManager
    {
        public static Dictionary<Unit, Aircraft> AssignedChimeras = new Dictionary<Unit, Aircraft>();
        private static readonly List<Unit> PruneScratch = new List<Unit>();
        public static bool IsUnitAssignedOrQueued(Unit unit, Aircraft ignoreAircraft = null)
        {
            if (unit == null || unit.disabled) return false;
            if (ChimeraSpawnQueue.Contains(unit)) return true;
            if (AssignedChimeras.TryGetValue(unit, out Aircraft assigned))
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
            foreach (var kvp in AssignedChimeras)
            {
                if (kvp.Value == aircraft) return true;
            }
            return false;
        }
        public static void AssignChimera(Unit target, Aircraft chimera)
        {
            if (target == null || chimera == null) return;
            AssignedChimeras[target] = chimera;
        }
        public static void UnassignChimera(Unit target)
        {
            if (target == null) return;
            AssignedChimeras.Remove(target);
        }
        public static bool TryGetUnassignedUnitNeedingRearm(RearmMissionController controller, bool ships, bool vehicles, Aircraft requestingAircraft, out Unit target)
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
                    if (ResupplyDispatcher.IsRecentlyServed(unit))
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
                if (ResupplyDispatcher.IsRecentlyServed(unit)) continue;
                target = unit;
                return true;
            }
            return false;
        }
        public static void Update()
        {
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
            PruneScratch.Clear();
        }
    }
}