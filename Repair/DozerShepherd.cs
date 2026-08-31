using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class DozerShepherd
    {
        private class DozerAssignment
        {
            public GroundVehicle Vehicle;
            public Repairer Repairer;
            public Airbase HomeAirbase;
            public Unit LastCommandedTarget;
        }
        private static readonly List<DozerAssignment> Assignments = new List<DozerAssignment>();
        internal static void ResetForNewLevel()
        {
            Assignments.Clear();
        }
        private static readonly AccessTools.FieldRef<GroundVehicle, bool> NavigateToObjectivesRef =
            AccessTools.FieldRefAccess<GroundVehicle, bool>("navigateToObjectives");
        private static readonly AccessTools.FieldRef<Repairer, Unit> UnitToRepairRef =
            AccessTools.FieldRefAccess<Repairer, Unit>("unitToRepair");
        public static void Register(GroundVehicle vehicle, Airbase homeAirbase)
        {
            if (vehicle == null) return;
            if (!vehicle.TryGetComponent(out Repairer repairer)) return;
            for (int i = 0; i < Assignments.Count; i++)
            {
                if (Assignments[i].Vehicle == vehicle) return;
            }
            NavigateToObjectivesRef(vehicle) = false;
            Assignments.Add(new DozerAssignment
            {
                Vehicle = vehicle,
                Repairer = repairer,
                HomeAirbase = homeAirbase
            });
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Repair vehicle '{vehicle.unitName}' delivered; objective navigation disabled, home airbase '{(homeAirbase != null ? homeAirbase.gameObject.name : "none (outpost)")}'.");
        }
        public static void Update()
        {
            for (int i = Assignments.Count - 1; i >= 0; i--)
            {
                DozerAssignment assignment = Assignments[i];
                if (assignment.Vehicle == null || assignment.Vehicle.disabled || assignment.Repairer == null)
                {
                    Assignments.RemoveAt(i);
                    continue;
                }
                if (UnitToRepairRef(assignment.Repairer) != null)
                {
                    assignment.LastCommandedTarget = null;
                    continue;
                }
                if (assignment.HomeAirbase == null) continue;
                Unit target = NextAirbaseTarget(assignment);
                if (target == null || target == assignment.LastCommandedTarget) continue;
                assignment.LastCommandedTarget = target;
                assignment.Vehicle.UnitCommand.SetDestination(target.GlobalPosition(), playerCommand: false);
                if (Plugin.Dbg)
                {
                    float dist = Vector3.Distance(assignment.Vehicle.transform.position, target.transform.position);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Repair vehicle '{assignment.Vehicle.unitName}' heading to '{target.unitName}' ({dist:F0}m) at {assignment.HomeAirbase.gameObject.name}.");
                }
            }
        }
        private static Unit NextAirbaseTarget(DozerAssignment assignment)
        {
            List<Building> buildings = assignment.HomeAirbase.buildings;
            if (buildings == null) return null;
            Vector3 from = assignment.Vehicle.transform.position;
            Unit best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < buildings.Count; i++)
            {
                Building building = buildings[i];
                if (!AirbaseRepairManager.IsValidRepairTarget(building)) continue;
                float dist = Vector3.SqrMagnitude(building.transform.position - from);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = building;
                }
            }
            return best;
        }
    }
}