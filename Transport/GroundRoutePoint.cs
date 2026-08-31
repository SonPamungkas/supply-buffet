using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    internal static class GroundRoutePoint
    {
        private static readonly AccessTools.FieldRef<GroundVehicle, PathfindingAgent> PathfinderRef =
            AccessTools.FieldRefAccess<GroundVehicle, PathfindingAgent>("pathfinder");
        private static readonly AccessTools.FieldRef<PathfindingAgent, List<GlobalPosition>> WaypointsRef =
            AccessTools.FieldRefAccess<PathfindingAgent, List<GlobalPosition>>("waypoints");
        internal static GlobalPosition For(Unit unit, out bool isRouteEnd)
        {
            isRouteEnd = false;
            if (unit == null) return default(GlobalPosition);
            GlobalPosition here = unit.GlobalPosition();
            GroundVehicle gv = unit as GroundVehicle;
            if (gv == null) return here;
            if (gv.GetHoldPosition() || unit.speed <= 1f) return here;
            float cap = Plugin.Cfg(Plugin.DryMovingTargetLeadCap, 2000f);
            if (cap <= 0f) return here;
            List<GlobalPosition> waypoints;
            try
            {
                PathfindingAgent agent = PathfinderRef(gv);
                if (agent == null) return here;
                waypoints = WaypointsRef(agent);
            }
            catch
            {
                return here;
            }
            if (waypoints == null || waypoints.Count == 0) return here;
            GlobalPosition prev = here;
            float travelled = 0f;
            isRouteEnd = true;
            for (int i = 0; i < waypoints.Count; i++)
            {
                GlobalPosition wp = waypoints[i];
                Vector3 step = wp - prev;
                step.y = 0f;
                travelled += step.magnitude;
                if (travelled >= cap) return wp;
                prev = wp;
            }
            return waypoints[waypoints.Count - 1];
        }
    }
}