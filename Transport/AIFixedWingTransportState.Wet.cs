using System;
using System.Collections.Generic;
using UnityEngine;
namespace SupplyBuffetMod
{
    public partial class AIFixedWingTransportState
    {
        private float NavalDropAltitude()
        {
            float dropAlt = (Plugin.NavalDropAltitude != null) ? Plugin.NavalDropAltitude.Value : NAVAL_DROP_ALT;
            return ClampAltitude(dropAlt);
        }
        private float NavalCruiseAltitude()
        {
            float configured = Plugin.Cfg(Plugin.ChimeraCruiseAltitude, 2500f);
            return ClampAltitude(Mathf.Max(NavalDropAltitude() + 300f, configured));
        }
        private bool NavalReleaseReady(out string fault)
        {
            if (!AttitudeReleaseReady(out fault)) return false;
            float maxCrossTrack = AirdropCrossTrackLimit();
            float crossTrack = CrossTrackOffset();
            if (crossTrack > maxCrossTrack)
            {
                fault = $"cross-track {crossTrack:F0}m > {maxCrossTrack:F0}";
                return false;
            }
            Vector3 toTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTarget.y = 0f;
            Vector3 horizVel = (aircraft.rb != null) ? aircraft.rb.velocity : Vector3.zero;
            horizVel.y = 0f;
            if (toTarget.sqrMagnitude > 1f && horizVel.sqrMagnitude > 1f)
            {
                float track = Vector3.Angle(toTarget, horizVel);
                if (track >= 20f)
                {
                    fault = $"track angle {track:F0}deg >= 20";
                    return false;
                }
            }
            fault = string.Empty;
            return true;
        }
        private void ComputeApproachPoints(bool restartRun = true)
        {
            if (!transportDestination.validMission)
            {
                phase = FlightPhase.Waiting;
                stateDisplayName = "Awaiting Cargo Mission";
                return;
            }
            GlobalPosition target = transportDestination.touchdownPoint;
            Vector3 axis;
            if (missionKind == MissionKind.RunwayRepair && repairRunway != null)
            {
                axis = RepairRunwayAxis();
            }
            else if (UseDirectDrop())
            {
                axis = target - aircraft.GlobalPosition();
            }
            else if (assignedTargetUnit != null)
            {
                Vector3 vel = (assignedTargetUnit.rb != null) ? assignedTargetUnit.rb.velocity : Vector3.zero;
                if (transportDestination.arrivalHeading.sqrMagnitude > 0.01f)
                    axis = transportDestination.arrivalHeading;
                else
                    axis = (vel.sqrMagnitude > 1f) ? vel.normalized : assignedTargetUnit.transform.forward;
            }
            else
            {
                axis = approachDirection;
            }
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f)
            {
                axis = aircraft.transform.forward;
            }
            axis.Normalize();
            approachAxis = axis;
            pointA = target - axis * 5000f;
            pointB = target - axis * RUN_LINE_B_DISTANCE;
            pointC = target - axis * 150f;
            runEntry = pointC - axis * (aircraftParameters.turningRadius * 3f);
            if (missionKind == MissionKind.NavalSupply)
            {
                runFloorY = Datum.SeaLevel.y;
            }
            else if (Plugin.Dbg)
            {
                runFloorY = (missionKind == MissionKind.RunwayRepair)
                    ? SampleRunFloor(pointB, pointC)
                    : SampleRunFloor(pointA, pointC);
            }
            else
            {
                runFloorY = Datum.SeaLevel.y;
            }
            if (itemsReleased > 0)
            {
                lastApproachRecalc = Time.timeSinceLevelLoad;
                return;
            }
            lastApproachRecalc = Time.timeSinceLevelLoad;
            if (!restartRun) return;
            EnterTransitOrRun();
        }
        private static float SampleRunFloor(GlobalPosition from, GlobalPosition to)
        {
            float highest = Datum.SeaLevel.y;
            Vector3 start = from.ToLocalPosition();
            Vector3 end = to.ToLocalPosition();
            int mask = PhysicsLayers.StaticsMask | PhysicsLayers.ExclusionZonesMask;
            for (int i = 0; i < RUN_TERRAIN_SAMPLES; i++)
            {
                Vector3 p = Vector3.Lerp(start, end, i / (float)(RUN_TERRAIN_SAMPLES - 1));
                p.y = Datum.LocalSeaY;
                if (Physics.Linecast(p + Vector3.up * 5000f, p - Vector3.up * 5000f, out RaycastHit hit, mask))
                {
                    float hitGlobalY = hit.point.ToGlobalPosition().y;
                    if (hitGlobalY > highest) highest = hitGlobalY;
                }
            }
            return highest;
        }
        private void RunApproachPhase(float altitudeTarget)
        {
            GlobalPosition joinPoint = ComputeJoinPoint();
            float distToJoin = FastMath.Distance(aircraft.GlobalPosition(), joinPoint);
            float approachSpeed = Mathf.Max(aircraftParameters.cornerSpeed + distToJoin * 0.02f,
                                            aircraftParameters.landingSpeed * 1.9f);
            controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - approachSpeed) * 0.1f, 0f, aircraftParameters.cruiseThrottle);
            GlobalPosition aimPos = joinPoint;
            aimPos.y = RunAimAltitude(altitudeTarget);
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), false, false, false, 0.9f, PatternBankLimit(), false, 0f, Vector3.zero);
            float capture = aircraftParameters.turningRadius;
            Vector3 toJoin = joinPoint - aircraft.GlobalPosition();
            bool passedJoin = distToJoin < capture && Vector3.Dot(aircraft.transform.forward, toJoin) < 0f;
            if (passedJoin || distToJoin < capture * 0.5f)
            {
                float joinOffset = FastMath.Distance(joinPoint, runEntry);
                Plugin.Log.LogInfo($"[SB|N3] {LogName} reached the join point, turning onto the run (join offset {joinOffset:F0}m).");
                EnterStage(FlightPhase.Aligning);
                return;
            }
            if (!ModeCheckDue()) return;
            if (Time.timeSinceLevelLoad - stageStartedAt > STAGE_TIMEOUT)
            {
                RestartRun($"could not reach the join point within {STAGE_TIMEOUT:F0}s");
            }
        }
        private static float AirdropCrossTrackLimit()
        {
            return (Plugin.ChimeraAirdropMaxCrossTrack != null) ? Plugin.ChimeraAirdropMaxCrossTrack.Value : 150f;
        }
        private float ApproachSpeedFloor()
        {
            float multiplier = (Plugin.ApproachSpeedFloor != null) ? Plugin.ApproachSpeedFloor.Value : 1.25f;
            return aircraftParameters.landingSpeed * multiplier;
        }
        private float PatternBankLimit()
        {
            return (Plugin.PatternBankLimit != null) ? Plugin.PatternBankLimit.Value : 135f;
        }
        private float BearingErrorToRun()
        {
            Vector3 toAimpoint = pointC - aircraft.GlobalPosition();
            toAimpoint.y = 0f;
            if (toAimpoint.sqrMagnitude < 1f) return 0f;
            Vector3 nose = aircraft.transform.forward;
            nose.y = 0f;
            if (nose.sqrMagnitude < 0.01f) return 0f;
            return Vector3.Angle(toAimpoint, nose);
        }
        private Vector3 RunLineError()
        {
            if (approachAxis.sqrMagnitude < 0.01f) return Vector3.zero;
            Vector3 fromC = aircraft.GlobalPosition() - pointC;
            fromC.y = 0f;
            Vector3 onLine = approachAxis * Vector3.Dot(fromC, approachAxis);
            return fromC - onLine;
        }
        private void UpdateRunLineCorrection(float horizDist)
        {
            float closing = horizDist;
            if (aircraft.rb != null)
            {
                float speed = Vector3.Dot(aircraft.rb.velocity, approachAxis);
                closing = (speed > 1f) ? horizDist / speed : 30f;
            }
            if (closing > 10f)
            {
                runLineCorrection = Vector3.zero;
                return;
            }
            Vector3 err = RunLineError();
            runLineCorrection += new Vector3(Mathf.Clamp(err.x, -4f, 4f) * 0.2f,
                                             0f,
                                             Mathf.Clamp(err.z, -4f, 4f) * 0.2f) * Time.fixedDeltaTime;
        }
        private GlobalPosition RunLineAimPoint()
        {
            if (approachAxis.sqrMagnitude < 0.01f) return pointC;
            Vector3 toC = pointC - aircraft.GlobalPosition();
            toC.y = 0f;
            float range = toC.magnitude;
            Vector3 nose = aircraft.transform.forward;
            nose.y = 0f;
            if (nose.sqrMagnitude < 0.01f) return pointC;
            GlobalPosition probe = aircraft.GlobalPosition() + nose.normalized * (LOOKAHEAD_BASE + range * LOOKAHEAD_SCALE);
            Vector3 fromC = probe - pointC;
            fromC.y = 0f;
            return pointC + approachAxis * Vector3.Dot(fromC, approachAxis);
        }
        private float CrossTrackOffset()
        {
            if (approachAxis.sqrMagnitude < 0.01f) return 0f;
            Vector3 fromEntry = aircraft.GlobalPosition() - runEntry;
            fromEntry.y = 0f;
            Vector3 lateral = fromEntry - approachAxis * Vector3.Dot(fromEntry, approachAxis);
            return lateral.magnitude;
        }
        private void RestartRun(string reason)
        {
            runAttempts++;
            Plugin.Log.LogInfo($"[SB|N6] {LogName} abandoning the run ({reason}) - re-flying the join on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "the target")}, attempt {runAttempts}.");
            int maxAttempts = (Plugin.MaxRunAttempts != null) ? Plugin.MaxRunAttempts.Value : 5;
            if (maxAttempts > 0 && runAttempts >= maxAttempts)
            {
                Plugin.Log.LogWarning($"[SB|N6] {LogName} gave up on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "its target")} after {runAttempts} attempts (last fault: {reason}); returning to base.");
                if (assignedTargetUnit != null)
                {
                    ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
                    assignedTargetUnit = null;
                }
                itemsReleased = 0;
                gearDownForDrop = false;
                phase = FlightPhase.Returning;
                UpdateStateDisplayName();
                return;
            }
            if (runAttempts % RUN_ATTEMPT_WARN_INTERVAL == 0)
            {
                Plugin.Log.LogWarning($"[SB|N6] {LogName} is still re-flying the join after {runAttempts} attempts on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "its target")}.");
            }
            itemsReleased = 0;
            gearDownForDrop = false;
            ComputeApproachPoints();
        }
        private GlobalPosition ComputeJoinPoint()
        {
            if (approachAxis.sqrMagnitude < 0.01f) return runEntry;
            Vector3 toAim = runEntry - aircraft.GlobalPosition();
            toAim.y = 0f;
            if (toAim.sqrMagnitude < 1f) return runEntry;
            float misalign = Vector3.Angle(toAim, approachAxis);
            float offset = (Mathf.Sin((misalign - 90f) * Mathf.Deg2Rad) + 1f) * aircraftParameters.turningRadius * 2f;
            if (offset <= 0.01f) return runEntry;
            return runEntry + Vector3.RotateTowards(-approachAxis * offset, -toAim, Mathf.PI / 2f, 0f);
        }
        private void RunAligningPhase(float altitudeTarget)
        {
            float distToC = FastMath.Distance(aircraft.GlobalPosition(), pointC);
            float adjustedLandingSpeed = aircraftParameters.landingSpeed;
            if (aircraft.definition != null && aircraft.definition.aircraftInfo != null
                && aircraft.definition.aircraftInfo.maxWeight > 0f)
            {
                adjustedLandingSpeed *= Mathf.Sqrt(aircraft.GetMass() / aircraft.definition.aircraftInfo.maxWeight);
            }
            float scheduledSpeed = adjustedLandingSpeed + 0.015f * Mathf.Max(distToC - 500f, 0f);
            float speedFloor = ApproachSpeedFloor();
            float approachSpeed = Mathf.Max(scheduledSpeed, speedFloor);
            controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - approachSpeed) * 0.1f, 0f, aircraftParameters.cruiseThrottle);
            if (!reachedFinal && FastMath.InRange(runEntry, aircraft.GlobalPosition(), aircraftParameters.turningRadius * 0.5f))
            {
                reachedFinal = true;
            }
            GlobalPosition aimPos = reachedFinal ? RunLineAimPoint() : runEntry;
            aimPos.y = RunAimAltitude(altitudeTarget);
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), false, false, false, 1.1f, PatternBankLimit(), false, 0f, Vector3.zero);
            float bearingError = BearingErrorToRun();
            float crossTrack = CrossTrackOffset();
            float crossTrackLimit = Mathf.Min(aircraftParameters.turningRadius * 0.25f, AirdropCrossTrackLimit());
            bool onLine = bearingError < ALIGN_TOLERANCE && crossTrack < crossTrackLimit;
            alignedTime = onLine ? alignedTime + Time.fixedDeltaTime : 0f;
            if (reachedFinal && alignedTime > ALIGN_HOLD)
            {
                Plugin.Log.LogInfo($"[SB|N4] {LogName} settled on the run line (bearing error {bearingError:F0}deg, cross-track {crossTrack:F0}m); starting the drop run.");
                BeginDropPhase();
                return;
            }
            if (!ModeCheckDue()) return;
            LogSpeedGovernor($"Aligning {AltitudeTrace(altitudeTarget)}", scheduledSpeed, speedFloor, approachSpeed, distToC);
            float pastEntry = Vector3.Dot(aircraft.GlobalPosition() - runEntry, approachAxis);
            if (!reachedFinal && pastEntry > aircraftParameters.turningRadius)
            {
                RestartRun($"overshot the roll-out point by {pastEntry:F0}m without establishing on the run line");
            }
            else if (!directDrop && Vector3.Dot(pointC - aircraft.GlobalPosition(), approachAxis) < 0f)
            {
                RestartRun("passed Point C before settling on the run line");
            }
            else if (Time.timeSinceLevelLoad - stageStartedAt > STAGE_TIMEOUT)
            {
                RestartRun($"could not line up within {STAGE_TIMEOUT:F0}s (bearing error {bearingError:F0}deg, cross-track {crossTrack:F0}m)");
            }
        }
    }
}