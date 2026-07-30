using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
namespace SupplyBuffetMod
{
    public static class ResupplyMissionManager
    {
        public static Dictionary<Unit, Aircraft> AssignedChimeras = new Dictionary<Unit, Aircraft>();
        public static Dictionary<Unit, float> DroppedCargoTimes = new Dictionary<Unit, float>();
        public static void Reset()
        {
            AssignedChimeras.Clear();
            DroppedCargoTimes.Clear();
        }
        public static bool IsUnitAssignedOrQueued(Unit unit, Aircraft ignoreAircraft = null)
        {
            if (unit == null || unit.disabled) return false;
            foreach (var req in Plugin.SpawnQueue)
            {
                if (req != null && req.Requester == unit)
                {
                    return true;
                }
            }
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
        public static void RegisterDrop(Unit target, float time)
        {
            if (target == null) return;
            DroppedCargoTimes[target] = time;
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
                if (!(unit is Aircraft) && (ships || !(unit is Ship)) && (vehicles || !(unit is GroundVehicle)))
                {
                    if (IsUnitAssignedOrQueued(unit, requestingAircraft))
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
        public static void Update(float currentTime)
        {
            var keys = new List<Unit>(AssignedChimeras.Keys);
            foreach (var unit in keys)
            {
                var chimera = AssignedChimeras[unit];
                if (chimera == null || chimera.disabled)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Assigned Chimera for '{unit?.unitName}' was destroyed or disabled! Removing assignment.");
                    AssignedChimeras.Remove(unit);
                }
            }
            var dropKeys = new List<Unit>(DroppedCargoTimes.Keys);
            foreach (var unit in dropKeys)
            {
                if (unit == null || unit.disabled)
                {
                    DroppedCargoTimes.Remove(unit);
                    continue;
                }
                float dropTime = DroppedCargoTimes[unit];
                if (currentTime - dropTime > 45.0f)
                {
                    DroppedCargoTimes.Remove(unit);
                    if (unit.HasRequestedRearm && unit.NetworkHQ != null && unit.NetworkHQ.RearmMissionController != null)
                    {
                        Plugin.Log.LogWarning($"[SupplyBuffetMod] Delivery failed to rearm '{unit.unitName}' after 45 seconds! Closing stuck rearm mission and refreshing request.");
                        unit.NetworkHQ.RearmMissionController.DeregisterNeedsRearm(unit);
                        unit.HasRequestedRearm = false;
                        if (unit.GetAmmoValue().Missing > 0)
                        {
                            unit.RequestRearm();
                        }
                    }
                }
            }
        }
    }
}