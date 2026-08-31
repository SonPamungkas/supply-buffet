using UnityEngine;
namespace SupplyBuffetMod
{
    public partial class AIFixedWingTransportState
    {
        private Vector3 dryRunInDirection;
        private float dryLastBlockedLog;
        private float dryLastTrace;
        private bool dryRunInArmed;
        private bool dryDescending;
        private bool dryLoggedDescent;
        private float dryFailsafeB;
        private float dryFailsafeRollAtDecision;
        private bool dryTargetIsRouteEnd;
        private const float DRY_DESCENT_SETTLE = 1000f;
        private static float DryPreferredRadarAlt => Plugin.Cfg(Plugin.DryPreferredRadarAltitude, 250f);
        private static float DryRunInHandoffSetting => Plugin.Cfg(Plugin.DryRunInHandoff, 6000f);
        private const float DRY_APPROACH_DISTANCE = 5000f;
        private const float DRY_DESCENT_SLOPE = 0.3f;
        private const float DRY_CORNER_SPEED_FRACTION = 0.75f;
        private const float DRY_MIN_DROP_SPEED_MULTIPLIER = 1.35f;
        private bool UseDirectDrop()
        {
            return missionKind == MissionKind.LandSupply
                || missionKind == MissionKind.CombatVehicle;
        }
        private void RunDryMission()
        {
            if (phase != FlightPhase.Drop)
            {
                dryRunInArmed = false;
                dryDescending = false;
                dryLoggedDescent = false;
                dryFailsafeB = 0f;
                dryFailsafeRollAtDecision = 0f;
                dryTargetIsRouteEnd = false;
                BeginDropPhase();
                phase = FlightPhase.Drop;
                UpdateStateDisplayName();
                Plugin.Log.LogInfo($"[SB|D1] {LogName} ground delivery for {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "a drop point")}: {itemsToRelease} item(s), {FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint):F0}m out.");
                Plugin.Log.LogInfo($"[SB|D0] {LogName} airframe: cornerSpeed={aircraftParameters.cornerSpeed:F0} landingSpeed={aircraftParameters.landingSpeed:F0} cruiseThrottle={aircraftParameters.cruiseThrottle:F2} | derived: dropSpeed={DryDropSpeed():F0} | config: b={CargoRelease.Distance(BuildReleaseInputs(false, dryRunInDirection)):F0} windOff={transportDestination.windOffset.magnitude:F0}m@roll{(Plugin.Cfg(Plugin.DryFailsafeRollLimit, 5f)):F0} leadCap={(Plugin.Cfg(Plugin.DryMovingTargetLeadCap, 2000f)):F0} pref={DryPreferredRadarAlt:F0} handoff={DryRunInHandoff():F0} descentStart={DryDescentStart():F0}");
            }
            UpdateDryMovingTarget();
            OpenCargoBayForDryRun();
            if (!dryRunInArmed && itemsReleased == 0)
            {
                float toTargetNow = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
                if (toTargetNow > DryRunInHandoff())
                {
                    FlyDryTransit(toTargetNow);
                    return;
                }
                SetDryRunInFromCurrentPosition();
                dryRunInArmed = true;
                UpdateStateDisplayName();
                Plugin.Log.LogInfo($"[SB|D3] {LogName} starting a ground run-in on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "its drop point")} ({toTargetNow:F0}m out, {aircraft.radarAlt:F0}m AGL).");
            }
            bool releasing = itemsReleased > 0;
            FlyDryDropRun();
            if (phase != FlightPhase.Drop) return;
            if (releasing || DryReleaseReady(out _))
            {
                ReleaseCargoStep();
            }
            else if (!TryGetCargoStation(out _))
            {
                AbandonDryDelivery("no cargo station left to release from");
            }
            if (itemsReleased > 0 && (itemsReleased >= itemsToRelease || !TryGetCargoStation(out _)))
            {
                gearDownForDrop = false;
                phase = FlightPhase.Exit;
                UpdateStateDisplayName();
            }
        }
        private void UpdateDryMovingTarget()
        {
            if (dryRunInArmed) return;
            if (assignedTargetUnit == null || assignedTargetUnit.disabled) return;
            transportDestination.touchdownPoint =
                GroundRoutePoint.For(assignedTargetUnit, out dryTargetIsRouteEnd)
                + transportDestination.windOffset;
        }
        private Vector3 DryTargetLead()
        {
            return dryTargetIsRouteEnd ? Vector3.zero : AssignedTargetVelocity();
        }
        private void SetDryRunInFromCurrentPosition()
        {
            Vector3 toDrop = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toDrop.y = 0f;
            dryRunInDirection = (toDrop.sqrMagnitude > 1f)
                ? toDrop.normalized
                : Vector3.ProjectOnPlane(aircraft.transform.forward, Vector3.up).normalized;
            if (dryRunInDirection.sqrMagnitude < 0.5f) dryRunInDirection = Vector3.forward;
        }
        private float DryDropSpeed()
        {
            return Mathf.Max(aircraftParameters.cornerSpeed * DRY_CORNER_SPEED_FRACTION,
                             aircraftParameters.landingSpeed * DRY_MIN_DROP_SPEED_MULTIPLIER);
        }
        private float DryRunInHandoff()
        {
            return DryRunInHandoffSetting;
        }
        private float DryDescentStart()
        {
            float toLose = Mathf.Max(0f, CruiseAltitude() - DryPreferredRadarAlt);
            return DryRunInHandoff() + Mathf.Max(toLose / Mathf.Max(DRY_DESCENT_SLOPE, 0.05f), 1f) + DRY_DESCENT_SETTLE;
        }
        private void FlyDryTransit(float distanceToTarget)
        {
            aircraft.SetFlightAssist(true);
            if (aircraft.gearDeployed) aircraft.SetGear(false);
            controlInputs.throttle = aircraftParameters.cruiseThrottle;
            if (!dryDescending && distanceToTarget <= DryDescentStart()) dryDescending = true;
            bool descending = dryDescending;
            float levelBy = descending ? DryRunInHandoff() + DRY_DESCENT_SETTLE : 0f;
            float wantAlt = descending
                ? Mathf.Lerp(CruiseAltitude(), DryPreferredRadarAlt,
                             Mathf.InverseLerp(DryDescentStart(), levelBy, distanceToTarget))
                : CruiseAltitude();
            float aboveTarget = aircraft.GlobalPosition().y - transportDestination.touchdownPoint.y;
            bool climbing = !descending && aboveTarget < CruiseAltitude() * 0.9f;
            if (descending && !dryLoggedDescent)
            {
                dryLoggedDescent = true;
                UpdateStateDisplayName();
                Plugin.Log.LogInfo($"[SB|D2] {LogName} descending for the ground run ({distanceToTarget:F0}m out, {aircraft.radarAlt:F0}m AGL, level by {levelBy:F0}m).");
            }
            GlobalPosition aim = transportDestination.touchdownPoint;
            aim.y = transportDestination.touchdownPoint.y + wantAlt;
            if (Plugin.Dbg && Time.timeSinceLevelLoad - dryLastTrace >= 1f)
            {
                dryLastTrace = Time.timeSinceLevelLoad;
                Vector3 toAim = aim - aircraft.GlobalPosition();
                float numDbg = (toAim.sqrMagnitude > 1f)
                    ? Vector3.Angle(aircraft.transform.forward, toAim) : 0f;
                float pitchDbg = Mathf.DeltaAngle(aircraft.transform.eulerAngles.x, 0f);
                string leg = descending ? "descent" : (climbing ? "climb" : "cruise");
                Plugin.Log.LogInfo($"[SB|D2] {LogName} transit {leg} {distanceToTarget:F0}m out, radarAlt {aircraft.radarAlt:F0}m aboveTgt {aboveTarget:F0}m (want {wantAlt:F0}m) vs {aircraft.rb.velocity.y:F1}m/s pitch {pitchDbg:F1}deg num {numDbg:F1}deg, {aircraft.speed:F0}m/s, handoff {DryRunInHandoff():F0}m.");
            }
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aim),
                                       true, false, false, 0.85f, TransitBankLimit(),
                                       false, 0f, AssignedTargetVelocity());
        }
        private void FlyDryDropRun()
        {
            Vector3 targetVelocity = DryTargetLead();
            GlobalPosition aim = transportDestination.touchdownPoint + dryRunInDirection * DRY_APPROACH_DISTANCE;
            aircraft.SetFlightAssist(true);
            if (aircraft.gearDeployed) aircraft.SetGear(false);
            aircraft.autopilot.AutoAim(aim, true, false, false, 0.85f, 45f, true, DryAltitudeHold(), targetVelocity);
            SetDryDropRunThrottle();
            if (Plugin.Dbg && Time.timeSinceLevelLoad - dryLastTrace >= 1f)
            {
                dryLastTrace = Time.timeSinceLevelLoad;
                Vector3 toDropDbg = transportDestination.touchdownPoint - aircraft.GlobalPosition();
                toDropDbg.y = 0f;
                Plugin.Log.LogInfo($"[SB|D4] {LogName} run-in {toDropDbg.magnitude:F0}m out, along {Vector3.Dot(toDropDbg, dryRunInDirection):F0}m, radarAlt {aircraft.radarAlt:F0}m, {aircraft.speed:F0}m/s.");
            }
        }
        private void SetDryDropRunThrottle()
        {
            float target = DryDropSpeed();
            controlInputs.throttle = Mathf.Clamp(aircraftParameters.cruiseThrottle + (target - aircraft.speed) * 0.015f, 0.2f, 1f);
            controlInputs.brake = 0f;
        }
        private const float MIN_RUN_RADAR_ALT = 120f;
        private float DryAltitudeHold()
        {
            float aboveTarget = aircraft.GlobalPosition().y - transportDestination.touchdownPoint.y;
            float targetAboveGround = aircraft.radarAlt - aboveTarget;
            float extra = Mathf.Clamp(targetAboveGround,
                                      MIN_RUN_RADAR_ALT - DryPreferredRadarAlt,
                                      DryPreferredRadarAlt * 2f);
            return DryPreferredRadarAlt + extra;
        }
        private bool DryReleaseReady(out string veto)
        {
            ReleaseInputs inputs = BuildReleaseInputs(wet: false, runAxis: dryRunInDirection);
            float liveB = CargoRelease.Distance(inputs);
            if (dryFailsafeB <= 0f && CargoRelease.RingReached(inputs, liveB, out _, out _))
            {
                dryFailsafeB = liveB;
                dryFailsafeRollAtDecision = inputs.RollDegrees;
                Plugin.Log.LogInfo($"[SB|D6] {LogName} release ring latched: {inputs.Crate} b={dryFailsafeB:F0}m at {inputs.Speed:F0}m/s, roll {inputs.RollDegrees:F1}deg.");
            }
            float releaseB = (dryFailsafeB > 0f) ? dryFailsafeB : liveB;
            bool reached = CargoRelease.RingReached(inputs, releaseB, out float slantRange, out float tripAt);
            bool passedTarget = CargoRelease.PastTarget(inputs);
            float relHeight = inputs.ToTarget.y;
            if (!reached && !passedTarget)
            {
                veto = $"{slantRange - tripAt:F0}m short of the release ring (slant {slantRange:F0}m of {tripAt:F0}m, b {releaseB:F0}m, aboveTarget {relHeight:F0}m)";
                if (Plugin.Dbg && Time.timeSinceLevelLoad - dryLastBlockedLog >= 1f)
                {
                    dryLastBlockedLog = Time.timeSinceLevelLoad;
                    Plugin.Log.LogInfo($"[SB|D5] {LogName} release blocked: {veto} ({inputs.Speed:F0}m/s).");
                }
                return false;
            }
            veto = string.Empty;
            Plugin.Log.LogInfo($"[SB|D7] {LogName} releasing {itemsToRelease} item(s): {inputs.Crate} slant={slantRange:F0}m of {tripAt:F0}m (b{releaseB:F0}@roll{dryFailsafeRollAtDecision:F1}) aboveTarget={relHeight:F0}m windOff={transportDestination.windOffset.magnitude:F0}m radarAlt={aircraft.radarAlt:F0}m speed={inputs.Speed:F0}m/s along={CargoRelease.AlongTrack(inputs):F0}m by={(passedTarget ? "passed" : "ring")}");
            return true;
        }
        private void AbandonDryDelivery(string reason)
        {
            Plugin.Log.LogWarning($"[SB|D9] {LogName} abandoning {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "its delivery")} ({reason}); returning to base.");
            if (assignedTargetUnit != null)
            {
                ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
                assignedTargetUnit = null;
            }
            gearDownForDrop = false;
            phase = FlightPhase.Returning;
            UpdateStateDisplayName();
        }
        private void OpenCargoBayForDryRun()
        {
            PingCargoBayDoors(FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint));
        }
    }
}