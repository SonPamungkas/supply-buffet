using System.Runtime.CompilerServices;
using UnityEngine;
namespace SupplyBuffetMod
{
    internal static class TerrainGuard
    {
        private const float GUARD_SECONDS = 6f;
        private const float REFERENCE_G = 9f;
        private const float CLEARANCE = 200f;
        private const float LOOKAHEAD_MIN = 800f;
        private const float LOOKAHEAD_MAX = 6000f;
        private const float CHECK_INTERVAL = 0.25f;
        private const float RELEASE_RATE = 120f;
        private sealed class GuardState
        {
            internal float NextCheck;
            internal float Bias;
            internal bool Reported;
        }
        private static readonly ConditionalWeakTable<Aircraft, GuardState> States =
            new ConditionalWeakTable<Aircraft, GuardState>();
        internal static GlobalPosition Raise(Aircraft aircraft, AircraftParameters parameters, GlobalPosition aim)
        {
            if (aircraft == null || aircraft.rb == null || aircraft.cockpit == null) return aim;
            GuardState state = States.GetOrCreateValue(aircraft);
            float now = Time.timeSinceLevelLoad;
            if (now >= state.NextCheck)
            {
                state.NextCheck = now + CHECK_INTERVAL;
                float wanted = Probe(aircraft, parameters, aim);
                if (wanted > state.Bias) state.Bias = wanted;
                else state.Bias = Mathf.Max(wanted, state.Bias - RELEASE_RATE * CHECK_INTERVAL);
                Report(aircraft, state);
            }
            if (state.Bias > 0f) aim.y += state.Bias;
            return aim;
        }
        private static float Probe(Aircraft aircraft, AircraftParameters parameters, GlobalPosition aim)
        {
            Vector3 velocity = aircraft.rb.velocity;
            if (velocity.sqrMagnitude < 1f) return 0f;
            float gLimit = (parameters != null && parameters.aircraftGLimit > 0.1f) ? parameters.aircraftGLimit : REFERENCE_G;
            float lookAhead = Mathf.Clamp(aircraft.speed * GUARD_SECONDS * (REFERENCE_G / gLimit),
                                          LOOKAHEAD_MIN, LOOKAHEAD_MAX);
            Vector3 start = aircraft.cockpit.xform.position - Vector3.up * aircraft.maxRadius;
            Vector3 travel = velocity.normalized * lookAhead;
            int mask = PhysicsLayers.StaticsMask | PhysicsLayers.ExclusionZonesMask;
            if (!Physics.Linecast(start, start + travel, out RaycastHit hit, mask)) return 0f;
            float wantedY = hit.point.ToGlobalPosition().y + CLEARANCE;
            return Mathf.Max(0f, wantedY - aim.y);
        }
        private static void Report(Aircraft aircraft, GuardState state)
        {
            if (state.Bias > 1f && !state.Reported)
            {
                state.Reported = true;
                Plugin.Log.LogInfo($"[SB|T1] {aircraft.unitName} terrain ahead: aim raised {state.Bias:F0}m.");
            }
            else if (state.Bias <= 1f && state.Reported)
            {
                state.Reported = false;
                Plugin.Log.LogInfo($"[SB|T1] {aircraft.unitName} terrain clear: aim released.");
            }
        }
    }
}