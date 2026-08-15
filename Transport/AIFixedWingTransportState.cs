using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public class AIFixedWingTransportState : PilotBaseState
    {
        public enum MissionKind
        {
            None,
            NavalSupply,
            LandSupply,
            CombatVehicle,
            RunwayRepair
        }
        public enum FlightPhase
        {
            Waiting,
            Climb,
            Cruise,
            Descent,
            Approach,
            Aligning,
            Drop,
            Exit,
            Returning
        }
        private struct TransportDestination
        {
            public bool validMission;
            public bool dropConditionsMet;
            public GlobalPosition touchdownPoint;
            public GlobalPosition targetPosition;
            public GlobalPosition LZ;
            public TrackingInfo nearesttarget;
            public float slope;
            public int touchdownPointAttempts;
            public TransportDestination(GlobalPosition landingPosition, GlobalPosition targetPos, float levelAmount)
            {
                validMission = false;
                dropConditionsMet = false;
                touchdownPoint = landingPosition;
                targetPosition = targetPos;
                LZ = targetPos;
                nearesttarget = null;
                slope = levelAmount;
                touchdownPointAttempts = 0;
            }
            public void UpdateLZ(Aircraft aircraft, GlobalPosition? targetPosition, float targetRadius, ref Vector3 approachDirection)
            {
                if (!targetPosition.HasValue)
                {
                    Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Target position is null.");
                    slope = 90f;
                    touchdownPointAttempts = 0;
                    return;
                }
                if (FastMath.InRange(aircraft.GlobalPosition(), touchdownPoint, 3000f))
                {
                    Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Within 3000m of touchdown point, committing to run.");
                    return;
                }
                approachDirection = FastMath.NormalizedDirection(targetPosition.Value, aircraft.GlobalPosition());
                approachDirection.y = 0f;
                GlobalPosition globalPosition = targetPosition.Value + approachDirection * (60f + targetRadius);
                float num = Mathf.Min(CombatAI.GetSafeStandoffDist(globalPosition, aircraft.NetworkHQ), 10000f);
                globalPosition += approachDirection * num;
                if (!FastMath.InRange(globalPosition, LZ, 100f))
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Generating new LZ at distance {(globalPosition - targetPosition.Value).magnitude}m");
                    LZ = globalPosition;
                    slope = 90f;
                    touchdownPointAttempts = 0;
                }
            }
            public void UpdateLZ(Aircraft aircraft, Unit unitToRearm)
            {
                Vector3 velocity = (unitToRearm.rb != null) ? unitToRearm.rb.velocity : Vector3.zero;
                float unitSpeed = velocity.magnitude;
                Vector3 forwardDir = (unitSpeed > 1f) ? velocity.normalized : unitToRearm.transform.forward;
                Vector3 toUnit = unitToRearm.GlobalPosition() - aircraft.GlobalPosition();
                toUnit.y = 0f;
                float leadTime = 30f;
                if (toUnit.sqrMagnitude > 1f && aircraft.rb != null)
                {
                    Vector3 ownVel = aircraft.rb.velocity;
                    ownVel.y = 0f;
                    Vector3 targetVel = velocity;
                    targetVel.y = 0f;
                    float closing = Vector3.Dot(ownVel - targetVel, toUnit.normalized);
                    if (closing > 1f) leadTime = Mathf.Min(toUnit.magnitude / closing, 30f);
                }
                float leadDistance = unitSpeed * leadTime;
                if (unitToRearm is Ship ship)
                {
                    touchdownPoint = ship.GlobalPosition() + forwardDir * leadDistance;
                    slope = 0f;
                }
                else
                {
                    touchdownPoint = unitToRearm.GlobalPosition() + forwardDir * leadDistance;
                    slope = 90f;
                }
                LZ = touchdownPoint;
                touchdownPointAttempts = 0;
            }
            public void UpdateTouchdownPoint(float maxRadius, Aircraft aircraft)
            {
                if (slope < 3f)
                {
                    return;
                }
                if (slope < 20f && FastMath.SquareDistance(aircraft.GlobalPosition(), touchdownPoint) < 1000000f)
                {
                    return;
                }
                Vector2 vector = UnityEngine.Random.insideUnitCircle * Mathf.Min(50 * touchdownPointAttempts, maxRadius);
                GlobalPosition position = LZ + new Vector3(vector.x, 0f, vector.y);
                if (!Physics.Linecast(position.ToLocalPosition() + Vector3.up * 4000f, position.ToLocalPosition() - Vector3.up * 4000f, out var hitInfo, PhysicsLayers.StaticsMask))
                {
                    return;
                }
                touchdownPointAttempts++;
                float num = Vector3.Angle(hitInfo.normal, Vector3.up);
                if (!(num < 20f) || !(hitInfo.point.y > Datum.LocalSeaY) || !(num < slope))
                {
                    return;
                }
                aircraft.NetworkHQ.DeregisterDropZone(touchdownPoint);
                if (!aircraft.NetworkHQ.IsDropZoneClear(hitInfo.point.ToGlobalPosition()))
                {
                    Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateTouchdownPoint: Drop zone not clear.");
                    return;
                }
                slope = num;
                touchdownPoint = hitInfo.point.ToGlobalPosition();
                aircraft.NetworkHQ.RegisterDropZone(touchdownPoint);
                Plugin.Log.LogInfo($"[SupplyBuffetMod][AIFixedWingTransportState] Found touchdown point slope {num:F1} for {aircraft.unitName}");
            }
        }
        private MissionKind missionKind;
        private FlightPhase phase;
        private TransportDestination transportDestination;
        private AircraftParameters aircraftParameters;
        private ChimeraDefense defense;
        private float lastEjectionCheck;
        private float lastLandingSpotCheck;
        private float targetDist;
        private float lastFiredTime;
        private float lastTargetAssessTime;
        private float lastLoSCheck;
        private float timeWithoutMission;
        private float lastCargoDroppedTime = -100f;
        private static readonly AccessTools.FieldRef<Weapon, Hardpoint> HardpointRef =
            AccessTools.FieldRefAccess<Weapon, Hardpoint>("hardpoint");
        private float lastBayOpenPing;
        private GlobalPosition pointA;
        private const float LOOKAHEAD_BASE = 200f;
        private const float LOOKAHEAD_SCALE = 0.2f;
        private GlobalPosition runEntry;
        private bool reachedFinal;
        private GlobalPosition pointB;
        private GlobalPosition pointC;
        private Vector3 approachAxis;
        private float runFloorY;
        private float lastApproachRecalc;
        private const float DRY_DROP_ALT = 700f;
        private const float NAVAL_DROP_ALT = 200f;
        private const float RUNWAY_DROP_ALT_MIN = 2f;
        private const float RUNWAY_DROP_ALT_MAX = 8f;
        private Airbase.Runway repairRunway;
        private bool repairRunwayReverse;
        private GlobalPosition repairRunwayPoint;
        private bool gearDownForDrop;
        private const float ALIGN_TOLERANCE = 10f;
        private const float ALIGN_HOLD = 2f;
        private const float STAGE_TIMEOUT = 45f;
        private const float RUN_ABORT_TOLERANCE = 40f;
        private const float LZ_ABORT_SHIFT = 1000f;
        private const int RUN_ATTEMPT_WARN_INTERVAL = 5;
        private const float DROP_ABORT_INTERVAL = 2f;
        private float alignedTime;
        private int runAttempts;
        private int dropAborts;
        private float lastDropAbortTime;
        private GlobalPosition runStartTouchdown;
        private const float POST_DROP_BASE = 4f;
        private const float POST_DROP_PER_ITEM = 9f;
        private float postDropHold = POST_DROP_BASE;
        private float stageStartedAt;
        private const int RUN_TERRAIN_SAMPLES = 11;
        private int itemsToRelease;
        private int itemsReleased;
        private float nextReleaseAt;
        private string missionTargetLabel = "";
        private bool deployedCargo;
        private bool targetLoS;
        private Unit currentTarget;
        private TrackingInfo currentTargetTracking;
        private Vector3 approachDirection;
        public Unit assignedTargetUnit;
        public AIFixedWingTransportState(Aircraft aircraft)
        {
            base.aircraft = aircraft;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Instantiated AIFixedWingTransportState for {aircraft.unitName}");
        }
        public override void EnterState(Pilot pilot)
        {
            stateDisplayName = "transporting cargo";
            phase = FlightPhase.Waiting;
            missionKind = MissionKind.None;
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            aircraftParameters = aircraft.GetAircraftParameters();
            defense = new ChimeraDefense(aircraft);
            deployedCargo = false;
            itemsToRelease = 0;
            itemsReleased = 0;
            lastBayOpenPing = 0f;
            approachDirection = aircraft.transform.forward;
            timeWithoutMission = 0f;
            nearestAirbase = (aircraft.NetworkHQ != null)
                ? aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position)
                : null;
            aircraft.SetFlightAssistToDefault();
            controlInputs = aircraft.GetInputs();
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} entered AIFixedWingTransportState.");
            if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.TryGetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit))
            {
                Vector3 vector = nearestUnit.lastKnownPosition - aircraft.GlobalPosition();
                vector.y = 0f;
                transportDestination = new TransportDestination(nearestUnit.lastKnownPosition - vector.normalized * 50f, nearestUnit.lastKnownPosition - vector.normalized * 50f, 90f);
            }
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
            else if (assignedTargetUnit != null)
            {
                Vector3 vel = (assignedTargetUnit.rb != null) ? assignedTargetUnit.rb.velocity : Vector3.zero;
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
            bool hold = assignedTargetUnit == null || IsHoldingPosition(assignedTargetUnit);
            pointA = target - axis * 5000f;
            pointB = target - axis * (hold ? 800f : 600f);
            pointC = target - axis * 150f;
            runEntry = pointC - axis * (aircraftParameters.turningRadius * 3f);
            if (missionKind == MissionKind.NavalSupply)
            {
                runFloorY = Datum.LocalSeaY;
            }
            else
            {
                runFloorY = (missionKind == MissionKind.RunwayRepair)
                    ? SampleRunFloor(pointB, pointC)
                    : SampleRunFloor(pointA, pointC);
            }
            if (itemsReleased > 0)
            {
                lastApproachRecalc = Time.timeSinceLevelLoad;
                return;
            }
            lastApproachRecalc = Time.timeSinceLevelLoad;
            if (!restartRun) return;
            phase = (FastMath.Distance(aircraft.GlobalPosition(), pointA) <= DescentDistance())
                ? FlightPhase.Approach
                : (aircraft.radarAlt >= CruiseAltitude() * 0.9f ? FlightPhase.Cruise : FlightPhase.Climb);
            EnterStage(phase);
        }
        private static float SampleRunFloor(GlobalPosition from, GlobalPosition to)
        {
            float highest = Datum.LocalSeaY;
            Vector3 start = from.ToLocalPosition();
            Vector3 end = to.ToLocalPosition();
            int mask = PhysicsLayers.StaticsMask | PhysicsLayers.ExclusionZonesMask;
            for (int i = 0; i < RUN_TERRAIN_SAMPLES; i++)
            {
                Vector3 p = Vector3.Lerp(start, end, i / (float)(RUN_TERRAIN_SAMPLES - 1));
                p.y = Datum.LocalSeaY;
                if (Physics.Linecast(p + Vector3.up * 5000f, p - Vector3.up * 5000f, out RaycastHit hit, mask)
                    && hit.point.y > highest)
                {
                    highest = hit.point.y;
                }
            }
            return highest;
        }
        private bool TrySelectRepairRunway(Airbase airbase)
        {
            repairRunway = null;
            if (airbase == null || airbase.runways == null) return false;
            if (Plugin.ChimeraUseRunwayDelivery != null && !Plugin.ChimeraUseRunwayDelivery.Value) return false;
            Vector3 approach = aircraft.transform.forward;
            approach.y = 0f;
            Airbase.Runway best = null;
            bool bestReverse = false;
            float bestLength = 0f;
            foreach (Airbase.Runway runway in airbase.runways)
            {
                if (runway == null || runway.Start == null || runway.End == null) continue;
                if (!runway.Landing && !runway.Takeoff) continue;
                if (runway.Length <= bestLength) continue;
                Vector3 forward = runway.GetDirection(false);
                forward.y = 0f;
                if (forward.sqrMagnitude < 1f) continue;
                bool reverse = runway.Reversable && Vector3.Dot(forward, approach) < 0f;
                best = runway;
                bestReverse = reverse;
                bestLength = runway.Length;
            }
            if (best == null) return false;
            repairRunway = best;
            repairRunwayReverse = bestReverse;
            repairRunwayPoint = new Airbase.Runway.RunwayUsage(best, bestReverse).GetTouchdownPoint();
            return true;
        }
        private Vector3 RepairRunwayAxis()
        {
            Vector3 axis = repairRunway.GetDirection(repairRunwayReverse);
            axis.y = 0f;
            return (axis.sqrMagnitude > 0.01f) ? axis.normalized : aircraft.transform.forward;
        }
        private float ClampAltitude(float altitude)
        {
            float floor = Mathf.Max(aircraft.maxRadius, aircraftParameters != null ? aircraftParameters.minimumRadarAlt : 0f);
            return Mathf.Clamp(altitude, floor, 8000f);
        }
        private float DropAltitude()
        {
            if (missionKind == MissionKind.RunwayRepair)
            {
                float configured = Plugin.ChimeraRunwayDropAltitude != null ? Plugin.ChimeraRunwayDropAltitude.Value : 5f;
                return Mathf.Clamp(configured, RUNWAY_DROP_ALT_MIN, RUNWAY_DROP_ALT_MAX);
            }
            return ClampAltitude(missionKind == MissionKind.NavalSupply ? NAVAL_DROP_ALT : DRY_DROP_ALT);
        }
        private float CruiseAltitude()
        {
            float configured = Plugin.ChimeraCruiseAltitude != null ? Plugin.ChimeraCruiseAltitude.Value : 2500f;
            return ClampAltitude(Mathf.Max(DropAltitude() + 300f, configured));
        }
        private float DescentDistance()
        {
            if (missionKind == MissionKind.RunwayRepair)
            {
                float runway = Plugin.ChimeraRunwayDescentDistance != null ? Plugin.ChimeraRunwayDescentDistance.Value : 6000f;
                return Mathf.Max(1000f, runway);
            }
            float configured = Plugin.ChimeraDescentDistance != null ? Plugin.ChimeraDescentDistance.Value : 8000f;
            return Mathf.Max(1000f, configured);
        }
        private void UpdateStateDisplayName()
        {
            string label;
            switch (phase)
            {
                case FlightPhase.Climb:     label = "Climbing";    break;
                case FlightPhase.Cruise:    label = "En Route";    break;
                case FlightPhase.Descent:   label = "Descending";  break;
                case FlightPhase.Approach:  label = "Joining";     break;
                case FlightPhase.Aligning:  label = "Aligning";    break;
                case FlightPhase.Drop:      label = "Approaching"; break;
                case FlightPhase.Exit:      label = "Dropping";    break;
                case FlightPhase.Returning: label = "Returning";   break;
                default:                    label = "";            break;
            }
            stateDisplayName = string.IsNullOrEmpty(missionTargetLabel) ? label : $"{label}: {missionTargetLabel}";
        }
        private void SearchForDropZone()
        {
            if (Time.timeSinceLevelLoad - lastLandingSpotCheck < 3f)
            {
                return;
            }
            lastLandingSpotCheck = Time.timeSinceLevelLoad;
            if (aircraft.weaponStations == null) return;
            foreach (WeaponStation weaponStation in aircraft.weaponStations)
            {
                if (weaponStation != null && weaponStation.WeaponInfo != null && weaponStation.WeaponInfo.cargo && weaponStation.Ammo > 0)
                {
                    aircraft.weaponManager.currentWeaponStation = weaponStation;
                    break;
                }
            }
            if (aircraft.weaponManager.currentWeaponStation == null || aircraft.weaponManager.currentWeaponStation.WeaponInfo == null)
            {
                return;
            }
            pilot.flightInfo.EnemyContact = true;
            if (RunInProgress())
            {
                if (missionKind == MissionKind.NavalSupply && itemsReleased == 0
                    && (assignedTargetUnit == null || assignedTargetUnit.disabled || !assignedTargetUnit.HasRequestedRearm))
                {
                    string why = (assignedTargetUnit == null) ? "target is gone"
                        : assignedTargetUnit.disabled ? $"{assignedTargetUnit.unitName} was destroyed"
                        : $"{assignedTargetUnit.unitName} no longer needs rearm";
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} releasing its lock mid-run ({why}); looking for another target.");
                    if (assignedTargetUnit != null) ResupplyMissionManager.UnassignChimera(assignedTargetUnit);
                    assignedTargetUnit = null;
                    transportDestination.validMission = false;
                    runAttempts = 0;
                }
                else
                {
                    transportDestination.UpdateLZ(aircraft, assignedTargetUnit);
                    return;
                }
            }
            bool rearmShip = aircraft.weaponManager.currentWeaponStation.WeaponInfo.rearmShip;
            bool rearmGround = aircraft.weaponManager.currentWeaponStation.WeaponInfo.rearmGround;
            GlobalPosition previousTouchdown = transportDestination.touchdownPoint;
            bool hadValidMission = transportDestination.validMission;
            Unit previousTarget = assignedTargetUnit;
            if (rearmShip || rearmGround)
            {
                if (aircraft.NetworkHQ != null && ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(aircraft.NetworkHQ.RearmMissionController, rearmShip, rearmGround, aircraft, out var lowestAmmoUnit))
                {
                    assignedTargetUnit = lowestAmmoUnit;
                    if (previousTarget != lowestAmmoUnit) runAttempts = 0;
                    ResupplyMissionManager.AssignChimera(lowestAmmoUnit, aircraft);
                    transportDestination.validMission = true;
                    timeWithoutMission = 0f;
                    if (!hadValidMission) transportDestination.UpdateLZ(aircraft, lowestAmmoUnit);
                    missionKind = rearmShip ? MissionKind.NavalSupply : MissionKind.LandSupply;
                    missionTargetLabel = $"{lowestAmmoUnit.unitName}";
                    if (!rearmShip && !hadValidMission)
                    {
                        transportDestination.UpdateTouchdownPoint(100f, aircraft);
                    }
                    UpdateStateDisplayName();
                    if (!hadValidMission)
                    {
                        ComputeApproachPoints();
                    }
                    else if (!FastMath.InRange(transportDestination.touchdownPoint, previousTouchdown, 500f))
                    {
                        ComputeApproachPoints(restartRun: false);
                    }
                    if (!hadValidMission || previousTarget != lowestAmmoUnit)
                    {
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} assigned to resupply {lowestAmmoUnit.unitName} ({stateDisplayName}). LZ: {transportDestination.touchdownPoint}");
                    }
                }
                else
                {
                    if (assignedTargetUnit != null)
                    {
                        ResupplyMissionManager.UnassignChimera(assignedTargetUnit);
                        assignedTargetUnit = null;
                    }
                    phase = FlightPhase.Waiting;
                    missionKind = MissionKind.None;
                    transportDestination.validMission = false;
                    OrbitAirbase();
                    bool wasWaiting = stateDisplayName == "Awaiting Cargo Mission";
                    stateDisplayName = "Awaiting Cargo Mission";
                    if (!wasWaiting)
                    {
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} awaiting cargo mission.");
                    }
                }
                return;
            }
            GlobalPosition? targetPosition = null;
            float range = float.MaxValue;
            float targetRadius = 0f;
            string vehicleLabel = "";
            bool runwayMission = false;
            if (AirbaseRepairManager.AssignedRepairs.TryGetValue(aircraft, out Unit repairTarget) && repairTarget != null)
            {
                Airbase ab = repairTarget.GetAirbase();
                vehicleLabel = $"Repair: {repairTarget.unitName}";
                targetRadius = 50f;
                if (TrySelectRepairRunway(ab))
                {
                    targetPosition = repairRunwayPoint;
                    targetRadius = 0f;
                    runwayMission = true;
                }
                else
                {
                    targetPosition = ab != null ? ab.center.GlobalPosition() : repairTarget.GlobalPosition();
                }
            }
            else if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.TryGetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit) && nearestUnit.TryGetUnit(out var unit))
            {
                vehicleLabel = "Vehicles (contact)";
                targetPosition = nearestUnit.lastKnownPosition;
                range = FastMath.Distance(targetPosition.Value, aircraft.GlobalPosition());
                targetRadius = unit.maxRadius * 2f;
            }
            if (!runwayMission && MissionPosition.TryGetClosestObjectivePosition(aircraft, out var result) && FastMath.InRange(aircraft.GlobalPosition(), result.Position, range))
            {
                targetPosition = result.Position;
                vehicleLabel = "Vehicles (objective)";
                targetRadius = 100f;
            }
            if (targetPosition.HasValue)
            {
                transportDestination.validMission = true;
                if (runwayMission)
                {
                    transportDestination.touchdownPoint = targetPosition.Value;
                }
                else
                {
                    transportDestination.UpdateLZ(aircraft, targetPosition, targetRadius, ref approachDirection);
                    if (!hadValidMission) transportDestination.UpdateTouchdownPoint(100f, aircraft);
                }
                if (!hadValidMission) missionKind = runwayMission ? MissionKind.RunwayRepair : MissionKind.CombatVehicle;
                timeWithoutMission = 0f;
                missionTargetLabel = vehicleLabel;
                UpdateStateDisplayName();
                if (!hadValidMission)
                {
                    ComputeApproachPoints();
                }
                else if (!FastMath.InRange(transportDestination.touchdownPoint, previousTouchdown, 500f))
                {
                    ComputeApproachPoints(restartRun: false);
                }
                if (!hadValidMission)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} assigned to drop vehicle at {targetPosition.Value} ({stateDisplayName}).");
                }
            }
            else
            {
                phase = FlightPhase.Waiting;
                missionKind = MissionKind.None;
                transportDestination.validMission = false;
                OrbitAirbase();
                bool wasWaiting = stateDisplayName == "Awaiting Cargo Mission";
                stateDisplayName = "Awaiting Cargo Mission";
                if (!wasWaiting)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} no valid drop zone found, awaiting mission.");
                }
            }
        }
        public void OrbitAirbase()
        {
            if (nearestAirbase == null)
            {
                transportDestination.touchdownPoint = aircraft.GlobalPosition();
                return;
            }
            timeWithoutMission += 3f;
            if (timeWithoutMission > 45f)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} without mission for >45s. Returning to land.");
                if (pilot.AILandingState != null)
                {
                    pilot.SwitchState(pilot.AILandingState);
                }
                else
                {
                    pilot.AILandingState = new AIPilotLandingState();
                    pilot.SwitchState(pilot.AILandingState);
                }
                return;
            }
            int num = 2000;
            Vector3 vector = nearestAirbase.center.GlobalPosition() - aircraft.GlobalPosition();
            vector.y = 0f;
            Vector3 current = Vector3.Cross(vector, Vector3.up);
            if (Vector3.Dot(vector, aircraft.transform.right) < 0f)
            {
                current *= -1f;
            }
            float num2 = vector.magnitude / (float)num;
            Vector3 vector2 = Vector3.RotateTowards(maxRadiansDelta: (!(Mathf.Abs(num2) > -0.4f) || !(num2 < 0.4f)) ? (num2 * 3f) : (num2 * 0.5f), current: current, target: vector, maxMagnitudeDelta: 1f);
            transportDestination.touchdownPoint = aircraft.GlobalPosition() + vector2.normalized * 4000f;
        }
        private void UpdateDropGear()
        {
            if (missionKind != MissionKind.RunwayRepair) return;
            bool wantGear = gearDownForDrop;
            LandingGear.GearState gearState = aircraft.gearState;
            if (gearState == LandingGear.GearState.Extending || gearState == LandingGear.GearState.Retracting) return;
            LandingGear.GearState settled = wantGear
                ? LandingGear.GearState.LockedExtended
                : LandingGear.GearState.LockedRetracted;
            if (gearState == settled) return;
            aircraft.SetGear(wantGear);
        }
        public override void FixedUpdateState(Pilot pilot)
        {
            try
            {
                FixedUpdateStateCore(pilot);
            }
            catch (Exception ex)
            {
                TransportFaultGuard.Report(aircraft, "AIFixedWingTransportState", ex);
                if (pilot != null && pilot.AICombatState != null)
                {
                    pilot.SwitchState(pilot.AICombatState);
                }
            }
        }
        private void FixedUpdateStateCore(Pilot pilot)
        {
            if (aircraft == null || aircraft.rb == null) return;
            defense.Update();
            UpdateDropGear();
            if (phase == FlightPhase.Returning)
            {
                RunReturnPhase();
                return;
            }
            if (missionKind == MissionKind.NavalSupply && assignedTargetUnit != null && !deployedCargo)
            {
                float distToTouchdown = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
                if (distToTouchdown > 1500f)
                {
                    transportDestination.UpdateLZ(aircraft, assignedTargetUnit);
                }
            }
            aircraft.SetFlightAssist(enabled: true);
            SearchForDropZone();
            if (transportDestination.validMission)
            {
                bool transiting = phase == FlightPhase.Climb || phase == FlightPhase.Cruise;
                float altitudeTarget = transiting ? CruiseAltitude() : DropAltitude();
                if (!deployedCargo && (phase == FlightPhase.Approach || phase == FlightPhase.Aligning)
                    && Time.timeSinceLevelLoad - lastApproachRecalc > 1f)
                {
                    ComputeApproachPoints(restartRun: false);
                }
                switch (phase)
                {
                    case FlightPhase.Approach:
                        RunApproachPhase(altitudeTarget);
                        break;
                    case FlightPhase.Aligning:
                        RunAligningPhase(altitudeTarget);
                        break;
                    case FlightPhase.Drop:
                        RunDropPhase(altitudeTarget);
                        break;
                    case FlightPhase.Exit:
                        RunExitPhase(altitudeTarget);
                        return;
                    default:
                        RunTransitPhase(altitudeTarget);
                        break;
                }
            }
            else
            {
                float altitudeTarget = CruiseAltitude();
                GlobalPosition aimPos = transportDestination.touchdownPoint;
                aimPos.y = aimPos.y + altitudeTarget;
                aircraft.autopilot.AutoAim(aimPos, true, false, false, 1f, 180f, true, altitudeTarget, Vector3.zero);
            }
            EjectionCheck();
            TargetSearch();
            DefendWithMissiles();
        }
        private void RunTransitPhase(float altitudeTarget)
        {
            controlInputs.throttle = aircraftParameters.cruiseThrottle;
            GlobalPosition aimPos = pointA;
            aimPos.y = pointA.y + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, false, false, 0.85f, 135f, true, altitudeTarget, Vector3.zero);
            float distToA = FastMath.Distance(aircraft.GlobalPosition(), pointA);
            if (distToA <= DescentDistance())
            {
                if (phase != FlightPhase.Descent)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} descending for the drop run ({distToA:F0}m to Point A).");
                    phase = FlightPhase.Descent;
                    UpdateStateDisplayName();
                }
                if (distToA <= aircraftParameters.turningRadius * 3f && phase != FlightPhase.Approach)
                {
                    EnterStage(FlightPhase.Approach);
                }
                return;
            }
            if (phase == FlightPhase.Climb && aircraft.radarAlt >= altitudeTarget * 0.9f)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} reached cruise altitude ({aircraft.radarAlt:F0}m).");
                phase = FlightPhase.Cruise;
                UpdateStateDisplayName();
            }
            else if (phase == FlightPhase.Descent)
            {
                phase = FlightPhase.Cruise;
                UpdateStateDisplayName();
            }
        }
        private void RunApproachPhase(float altitudeTarget)
        {
            GlobalPosition joinPoint = ComputeJoinPoint();
            float distToJoin = FastMath.Distance(aircraft.GlobalPosition(), joinPoint);
            float approachSpeed = Mathf.Max(aircraftParameters.cornerSpeed + distToJoin * 0.02f,
                                            aircraftParameters.landingSpeed * 1.9f);
            controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - approachSpeed) * 0.1f, 0f, aircraftParameters.cruiseThrottle);
            GlobalPosition aimPos = joinPoint;
            aimPos.y = pointA.y + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, false, false, 0.9f, 135f, true, altitudeTarget, Vector3.zero);
            float capture = aircraftParameters.turningRadius;
            Vector3 toJoin = joinPoint - aircraft.GlobalPosition();
            bool passedJoin = distToJoin < capture && Vector3.Dot(aircraft.transform.forward, toJoin) < 0f;
            if (passedJoin || distToJoin < capture * 0.5f)
            {
                float joinOffset = FastMath.Distance(joinPoint, runEntry);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} reached the join point, turning onto the run (join offset {joinOffset:F0}m).");
                EnterStage(FlightPhase.Aligning);
                return;
            }
            if (Time.timeSinceLevelLoad - stageStartedAt > STAGE_TIMEOUT)
            {
                RestartRun($"could not reach the join point within {STAGE_TIMEOUT:F0}s");
            }
        }
        private bool RunInProgress()
        {
            if (assignedTargetUnit == null || assignedTargetUnit.disabled) return false;
            if (!transportDestination.validMission) return false;
            return phase == FlightPhase.Approach
                || phase == FlightPhase.Aligning
                || phase == FlightPhase.Drop;
        }
        private void EnterStage(FlightPhase newPhase)
        {
            phase = newPhase;
            stageStartedAt = Time.timeSinceLevelLoad;
            alignedTime = 0f;
            reachedFinal = false;
            UpdateStateDisplayName();
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
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} abandoning the run ({reason}) - re-flying the join on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "the target")}, attempt {runAttempts}.");
            if (runAttempts % RUN_ATTEMPT_WARN_INTERVAL == 0)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] {aircraft.unitName} is still re-flying the join after {runAttempts} attempts on {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "its target")}.");
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
            float approachSpeed = Mathf.Max(aircraftParameters.cornerSpeed + distToC * 0.02f,
                                            aircraftParameters.landingSpeed * 1.9f);
            controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - approachSpeed) * 0.1f, 0f, aircraftParameters.cruiseThrottle);
            if (!reachedFinal && FastMath.InRange(runEntry, aircraft.GlobalPosition(), aircraftParameters.turningRadius * 0.5f))
            {
                reachedFinal = true;
            }
            GlobalPosition aimPos = reachedFinal ? RunLineAimPoint() : runEntry;
            aimPos.y = (reachedFinal ? pointC.y : runEntry.y) + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, false, false, 1f, 135f, true, altitudeTarget, Vector3.zero);
            float bearingError = BearingErrorToRun();
            float crossTrack = CrossTrackOffset();
            float crossTrackLimit = aircraftParameters.turningRadius * 0.25f;
            bool onLine = bearingError < ALIGN_TOLERANCE && crossTrack < crossTrackLimit;
            alignedTime = onLine ? alignedTime + Time.fixedDeltaTime : 0f;
            if (reachedFinal && alignedTime > ALIGN_HOLD)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} settled on the run line (bearing error {bearingError:F0}deg, cross-track {crossTrack:F0}m); starting the drop run.");
                BeginDropPhase();
                return;
            }
            float pastEntry = Vector3.Dot(aircraft.GlobalPosition() - runEntry, approachAxis);
            if (!reachedFinal && pastEntry > aircraftParameters.turningRadius)
            {
                RestartRun($"overshot the roll-out point by {pastEntry:F0}m without establishing on the run line");
            }
            else if (Vector3.Dot(pointC - aircraft.GlobalPosition(), approachAxis) < 0f)
            {
                RestartRun("passed Point C before settling on the run line");
            }
            else if (Time.timeSinceLevelLoad - stageStartedAt > STAGE_TIMEOUT)
            {
                RestartRun($"could not line up within {STAGE_TIMEOUT:F0}s (bearing error {bearingError:F0}deg, cross-track {crossTrack:F0}m)");
            }
        }
        private void BeginDropPhase()
        {
            itemsReleased = 0;
            nextReleaseAt = 0f;
            int aboard = CargoDemand.ItemsAboard(aircraft);
            if (assignedTargetUnit == null)
            {
                itemsToRelease = Mathf.Min(1, aboard);
            }
            else
            {
                string cargoKey = CurrentCargoMountKey();
                if (SupplyFullRestore.IsFullRestore(cargoKey))
                {
                    itemsToRelease = Mathf.Min(1, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} dropping 1 for {assignedTargetUnit.unitName}: '{cargoKey}' is a full-restore supply item.");
                }
                else if (missionKind == MissionKind.NavalSupply)
                {
                    float sensitivity = (Plugin.RearmRequestSensitivity != null) ? Plugin.RearmRequestSensitivity.Value : 0.999f;
                    float maxCapacity = assignedTargetUnit.TryGetComponent(out Rearmer targetRearmer)
                        ? targetRearmer.GetMaxCapacity()
                        : CargoDemand.FullLoadMass(assignedTargetUnit);
                    float threshold = (1f - sensitivity) * maxCapacity;
                    float perItem = CargoDemand.ItemCapacity(true, cargoKey);
                    itemsToRelease = (threshold > perItem) ? aboard : Mathf.Min(1, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} sizing wet drop for {assignedTargetUnit.unitName}: threshold {threshold:F0} (maxCapacity {maxCapacity:F0}) vs {perItem:F0} per crate -> {itemsToRelease} of {aboard} aboard.");
                }
                else
                {
                    float demand = CargoDemand.FullLoadMass(assignedTargetUnit);
                    float perItem = CargoDemand.ItemCapacity(false, cargoKey);
                    itemsToRelease = CargoDemand.ItemsToRelease(demand, perItem, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} sizing drop for {assignedTargetUnit.unitName}: demand {demand:F0} / {perItem:F0} per item -> {itemsToRelease} of {aboard} aboard.");
                }
            }
            dropAborts = 0;
            lastDropAbortTime = 0f;
            gearDownForDrop = (missionKind == MissionKind.RunwayRepair);
            runStartTouchdown = transportDestination.touchdownPoint;
            EnterStage(FlightPhase.Drop);
        }
        private string CurrentCargoMountKey()
        {
            if (!TryGetCargoStation(out WeaponStation station)) return null;
            foreach (Weapon w in station.Weapons)
            {
                if (w is MountedCargo cargo && cargo.cargo != null) return cargo.cargo.jsonKey;
            }
            return null;
        }
        private void RunDropPhase(float altitudeTarget)
        {
            if (itemsToRelease <= 0)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} has no cargo to release; ending the run.");
                gearDownForDrop = false;
                phase = FlightPhase.Exit;
                lastCargoDroppedTime = Time.timeSinceLevelLoad;
                UpdateStateDisplayName();
                return;
            }
            controlInputs.throttle = (missionKind == MissionKind.RunwayRepair) ? 0.72f : 1f;
            Vector3 toC = pointC - aircraft.GlobalPosition();
            toC.y = 0f;
            Vector3 runVel = aircraft.rb.velocity;
            runVel.y = 0f;
            bool followTerrain = toC.sqrMagnitude < 1f || runVel.sqrMagnitude < 1f
                || Vector3.Angle(runVel, toC) > 20f;
            GlobalPosition aimPos = RunLineAimPoint();
            aimPos.y = (followTerrain ? pointC.y : Mathf.Max(pointC.y, runFloorY)) + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, false, false, 1.1f, 135f, followTerrain, altitudeTarget, Vector3.zero);
            Vector3 toTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTarget.y = 0f;
            float horizDist = toTarget.magnitude;
            if (!deployedCargo && horizDist < 3000f && Time.timeSinceLevelLoad - lastBayOpenPing > 0.5f
                && TryGetCargoStation(out WeaponStation cargoStation))
            {
                lastBayOpenPing = Time.timeSinceLevelLoad;
                foreach (Weapon w in cargoStation.Weapons)
                {
                    Hardpoint hp = (w != null) ? HardpointRef(w) : null;
                    if (hp != null) hp.SpringOpenBayDoors();
                }
            }
            bool reachedB = Vector3.Dot(aircraft.GlobalPosition() - pointB, approachAxis) >= 0f;
            bool outOfAttempts = dropAborts >= MaxAirdropAttempts();
            bool releaseReady = (missionKind == MissionKind.RunwayRepair)
                ? RunwayReleaseReady()
                : (reachedB && (outOfAttempts || AirdropReleaseReady(out _)));
            if (itemsReleased == 0 && releaseReady)
            {
                float horizToTarget = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
                float sink = (aircraft.rb != null) ? aircraft.rb.velocity.y : 0f;
                float roll = Mathf.Abs(Mathf.DeltaAngle(aircraft.transform.eulerAngles.z, 0f));
                float lead = (assignedTargetUnit != null)
                    ? FastMath.Distance(transportDestination.touchdownPoint, assignedTargetUnit.GlobalPosition())
                    : 0f;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} releasing {itemsToRelease} item(s): horiz-to-target={horizToTarget:F0}m alt={(aircraft.GlobalPosition().y - pointC.y):F0}m radarAlt={aircraft.radarAlt:F1}m speed={aircraft.speed:F0}m/s roll={roll:F0}deg vs={sink:F1}m/s lead={lead:F0}m forced={outOfAttempts}");
            }
            if (itemsReleased > 0 || releaseReady)
            {
                ReleaseCargoStep();
            }
            if (itemsReleased > 0 && (itemsReleased >= itemsToRelease || !TryGetCargoStation(out _)))
            {
                gearDownForDrop = false;
                phase = FlightPhase.Exit;
                UpdateStateDisplayName();
            }
            else if (itemsReleased == 0)
            {
                if (reachedB && missionKind != MissionKind.RunwayRepair
                    && Time.timeSinceLevelLoad - lastDropAbortTime >= DROP_ABORT_INTERVAL
                    && !AirdropReleaseReady(out string releaseFault))
                {
                    dropAborts++;
                    lastDropAbortTime = Time.timeSinceLevelLoad;
                    RestartRun($"unstable at the release point ({releaseFault}), abort {dropAborts} of {MaxAirdropAttempts()}");
                }
                else if (Vector3.Dot(pointC - aircraft.GlobalPosition(), approachAxis) < 0f)
                {
                    RestartRun("passed Point C without dropping");
                }
                else if (BearingErrorToRun() > RUN_ABORT_TOLERANCE)
                {
                    RestartRun($"lost the run line (bearing error {BearingErrorToRun():F0}deg)");
                }
                else if (FastMath.Distance(transportDestination.touchdownPoint, runStartTouchdown) > LZ_ABORT_SHIFT)
                {
                    RestartRun($"the drop zone moved {FastMath.Distance(transportDestination.touchdownPoint, runStartTouchdown):F0}m mid-run");
                }
            }
        }
        private int MaxAirdropAttempts()
        {
            return (Plugin.ChimeraAirdropMaxAttempts != null) ? Plugin.ChimeraAirdropMaxAttempts.Value : 3;
        }
        private bool AirdropReleaseReady(out string fault)
        {
            float maxRoll = (Plugin.ChimeraAirdropMaxRoll != null) ? Plugin.ChimeraAirdropMaxRoll.Value : 10f;
            float roll = Mathf.Abs(Mathf.DeltaAngle(aircraft.transform.eulerAngles.z, 0f));
            if (roll > maxRoll)
            {
                fault = $"roll {roll:F0}deg > {maxRoll:F0}";
                return false;
            }
            float maxVertical = (Plugin.ChimeraAirdropMaxVerticalSpeed != null) ? Plugin.ChimeraAirdropMaxVerticalSpeed.Value : 10f;
            float sink = (aircraft.rb != null) ? Mathf.Abs(aircraft.rb.velocity.y) : 0f;
            if (sink > maxVertical)
            {
                fault = $"vertical speed {sink:F0}m/s > {maxVertical:F0}";
                return false;
            }
            float maxCrossTrack = (Plugin.ChimeraAirdropMaxCrossTrack != null) ? Plugin.ChimeraAirdropMaxCrossTrack.Value : 150f;
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
        private bool RunwayReleaseReady()
        {
            float targetAlt = DropAltitude();
            float tolerance = Plugin.ChimeraRunwayDropTolerance != null ? Plugin.ChimeraRunwayDropTolerance.Value : 2f;
            if (Mathf.Abs(aircraft.radarAlt - targetAlt) > tolerance) return false;
            float minSpeed = Plugin.ChimeraRunwayMinReleaseSpeed != null ? Plugin.ChimeraRunwayMinReleaseSpeed.Value : 75f;
            float maxSpeed = Plugin.ChimeraRunwayMaxReleaseSpeed != null ? Plugin.ChimeraRunwayMaxReleaseSpeed.Value : 190f;
            if (aircraft.speed < minSpeed || aircraft.speed > maxSpeed) return false;
            float maxRoll = Plugin.ChimeraRunwayMaxRoll != null ? Plugin.ChimeraRunwayMaxRoll.Value : 18f;
            if (Mathf.Abs(Mathf.DeltaAngle(aircraft.transform.eulerAngles.z, 0f)) > maxRoll) return false;
            float maxVertical = Plugin.ChimeraRunwayMaxVerticalSpeed != null ? Plugin.ChimeraRunwayMaxVerticalSpeed.Value : 30f;
            if (aircraft.rb != null && Mathf.Abs(aircraft.rb.velocity.y) > maxVertical) return false;
            Vector3 toTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTarget.y = 0f;
            Vector3 horizVel = (aircraft.rb != null) ? aircraft.rb.velocity : Vector3.zero;
            horizVel.y = 0f;
            if (toTarget.sqrMagnitude <= 1f || horizVel.sqrMagnitude <= 1f) return false;
            return Vector3.Angle(toTarget, horizVel) < 20f;
        }
        private void ReleaseCargoStep()
        {
            if (itemsReleased >= itemsToRelease) return;
            float now = Time.timeSinceLevelLoad;
            if (now < nextReleaseAt) return;
            if (!TryGetCargoStation(out WeaponStation station)) return;
            aircraft.weaponManager.currentWeaponStation = station;
            station.LaunchMount(aircraft, null, transportDestination.touchdownPoint);
            itemsReleased++;
            nextReleaseAt = now + (Plugin.ChimeraReleaseInterval != null ? Plugin.ChimeraReleaseInterval.Value : 0.35f);
            lastCargoDroppedTime = now;
            if (itemsReleased == 1) OnFirstRelease();
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} released cargo {itemsReleased}/{itemsToRelease}.");
        }
        private static bool IsHoldingPosition(Unit unit)
        {
            if (unit is Ship ship) return ship.holdPosition;
            if (unit is GroundVehicle gv) return gv.GetHoldPosition();
            return true;
        }
        private bool TryGetCargoStation(out WeaponStation station)
        {
            station = null;
            if (aircraft.weaponStations == null) return false;
            foreach (WeaponStation ws in aircraft.weaponStations)
            {
                if (ws != null && ws.Weapons != null && ws.WeaponInfo != null && ws.WeaponInfo.cargo && ws.Ammo > 0)
                {
                    station = ws;
                    return true;
                }
            }
            return false;
        }
        private void RunExitPhase(float altitudeTarget)
        {
            Vector3 velDir = aircraft.rb.velocity;
            velDir.y = 0f;
            GlobalPosition aimPos = aircraft.GlobalPosition() + velDir.normalized * 2000f;
            aimPos.y = aircraft.GlobalPosition().y + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, false, false, 1f, 180f, true, altitudeTarget, Vector3.zero);
            controlInputs.throttle = 1f;
            if (Time.timeSinceLevelLoad - lastCargoDroppedTime <= postDropHold) return;
            if (CargoDemand.ItemsAboard(aircraft) > 0 && aircraft.NetworkHQ != null
                && ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(
                       aircraft.NetworkHQ.RearmMissionController,
                       missionKind == MissionKind.NavalSupply,
                       missionKind != MissionKind.NavalSupply,
                       aircraft, out Unit nextTarget))
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} has cargo remaining; re-tasking to {nextTarget.unitName}.");
                assignedTargetUnit = nextTarget;
                runAttempts = 0;
                ResupplyMissionManager.AssignChimera(nextTarget, aircraft);
                missionTargetLabel = nextTarget.unitName;
                deployedCargo = false;
                itemsReleased = 0;
                itemsToRelease = 0;
                timeWithoutMission = 0f;
                transportDestination.validMission = true;
                transportDestination.UpdateLZ(aircraft, nextTarget);
                ComputeApproachPoints();
                return;
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} completed airdrop pass. Returning to base.");
            phase = FlightPhase.Returning;
            UpdateStateDisplayName();
        }
        private void RunReturnPhase()
        {
            if (pilot.AILandingState == null)
            {
                pilot.AILandingState = new AIPilotLandingState();
            }
            pilot.SwitchState(pilot.AILandingState);
        }
        private void OnFirstRelease()
        {
            int remaining = CargoDemand.ItemsAboard(aircraft);
            float holdBase = (Plugin.PostDropHoldBase != null) ? Plugin.PostDropHoldBase.Value : POST_DROP_BASE;
            float holdPerCrate = (Plugin.PostDropHoldPerCrate != null) ? Plugin.PostDropHoldPerCrate.Value : POST_DROP_PER_ITEM;
            postDropHold = holdBase + holdPerCrate * remaining;
            Plugin.TriggerControlNullifier(aircraft, postDropHold);
            if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} holding the drop line for {postDropHold:F0}s ({remaining} crate(s) still aboard).");
            }
            defense.TriggerDropBurst();
            pilot.flightInfo.LastCargoDelivery = Time.timeSinceLevelLoad;
            pilot.flightInfo.EnemyContact = true;
            deployedCargo = true;
            if (assignedTargetUnit != null)
            {
                ResupplyDispatcher.MarkDropped(assignedTargetUnit);
                ResupplyMissionManager.UnassignChimera(assignedTargetUnit);
                assignedTargetUnit = null;
            }
        }
        private void EjectionCheck()
        {
            if (Time.timeSinceLevelLoad - lastEjectionCheck < 5f)
            {
                return;
            }
            lastEjectionCheck = Time.timeSinceLevelLoad;
            bool flag = false;
            if (aircraft.cockpit.xform.position.y < Datum.LocalSeaY)
            {
                flag = true;
            }
            if (aircraft.radarAlt > 40f && (Vector3.Dot(aircraft.cockpit.xform.forward, aircraft.rb.velocity) < 0f || aircraft.partDamageTracker.GetDetachedRatio() > 0.12f))
            {
                flag = true;
            }
            if (flag)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Ejection condition met for {aircraft.unitName}. Ejecting!");
                pilot.aircraft.StartEjectionSequence();
            }
        }
        private void TargetSearch()
        {
            if (Time.timeSinceLevelLoad - lastTargetAssessTime < 5f || (aircraft.weaponManager.currentWeaponStation != null && aircraft.weaponManager.currentWeaponStation.SalvoInProgress))
            {
                return;
            }
            if (aircraft.radarAlt < 2f)
            {
                if (aircraft.weaponStations != null)
                {
                    foreach (WeaponStation weaponStation in aircraft.weaponStations)
                    {
                        if (weaponStation != null && weaponStation.WeaponInfo != null && weaponStation.WeaponInfo.cargo)
                        {
                            aircraft.weaponManager.currentWeaponStation = weaponStation;
                            break;
                        }
                    }
                }
                aircraft.weaponManager.ClearTargetList();
                return;
            }
            lastTargetAssessTime = Time.timeSinceLevelLoad;
            Unit unit = currentTarget;
            var targetSearchResults = CombatAI.ChooseHQTarget(aircraft, 1f, aircraft.weaponStations);
            if (targetSearchResults.target != null)
            {
                targetDist = FastMath.Distance(targetSearchResults.target.GlobalPosition(), aircraft.GlobalPosition());
            }
            if (targetSearchResults.chosenWeaponStation != null)
            {
                aircraft.weaponManager.currentWeaponStation = targetSearchResults.chosenWeaponStation;
            }
            if (targetSearchResults.target != unit)
            {
                currentTarget = targetSearchResults.target;
                aircraft.weaponManager.ClearTargetList();
                if (currentTarget != null && aircraft.NetworkHQ != null)
                {
                    pilot.flightInfo.EnemyContact = true;
                    currentTargetTracking = aircraft.NetworkHQ.GetTrackingData(currentTarget.persistentID);
                    aircraft.weaponManager.AddTargetList(currentTarget);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} target changed to {currentTarget.unitName}");
                }
            }
        }
        private void LoSCheck()
        {
            if (currentTarget != null && !(Time.timeSinceLevelLoad - lastLoSCheck < 1f))
            {
                lastLoSCheck = Time.timeSinceLevelLoad;
                targetLoS = currentTarget.LineOfSight(aircraft.transform.position - Vector3.up * aircraft.definition.spawnOffset.y, 1000f);
            }
        }
        private void DefendWithMissiles()
        {
            if (currentTarget == null || aircraft.radarAlt < 10f)
            {
                return;
            }
            WeaponStation currentWeaponStation = aircraft.weaponManager.currentWeaponStation;
            if (currentWeaponStation == null || currentWeaponStation.WeaponInfo == null)
            {
                return;
            }
            WeaponInfo weaponInfo = currentWeaponStation.WeaponInfo;
            if (weaponInfo.bomb || weaponInfo.gun || weaponInfo.cargo || targetDist < weaponInfo.targetRequirements.minRange || targetDist > weaponInfo.targetRequirements.maxRange || currentWeaponStation.Ammo <= 0)
            {
                return;
            }
            LoSCheck();
            float num = Vector3.Angle(currentTarget.transform.position - aircraft.transform.position, aircraft.transform.forward);
            if (!targetLoS || num > 30f)
            {
                return;
            }
            float num2 = currentWeaponStation.WeaponInfo.CalcAttacksNeeded(currentTarget);
            if (currentTargetTracking != null && (float)currentTargetTracking.missileAttacks > num2)
            {
                return;
            }
            if (!currentWeaponStation.SalvoInProgress && num < weaponInfo.targetRequirements.minAlignment && Time.timeSinceLevelLoad - lastFiredTime > 2.5f && aircraft.NetworkHQ != null && aircraft.NetworkHQ.TryGetKnownPosition(currentTarget, out var knownPosition) && FastMath.InRange(knownPosition, currentTarget.GlobalPosition(), 500f))
            {
                List<Unit> targetList = aircraft.weaponManager.GetTargetList();
                targetList.Clear();
                int num3 = CombatAI.LookForMissileTargets(aircraft, currentTarget, currentWeaponStation, targetList);
                aircraft.weaponManager.TargetListChanged();
                if (num3 > 0)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} defending with missiles against {currentTarget.unitName}!");
                    pilot.Fire();
                    lastTargetAssessTime = Time.timeSinceLevelLoad - 2f;
                    lastFiredTime = Time.timeSinceLevelLoad;
                }
            }
        }
        public override void UpdateState(Pilot pilot)
        {
        }
        public override void LeaveState()
        {
            if (defense != null) defense.Stop();
            if (aircraft != null && aircraft.NetworkHQ != null)
            {
                aircraft.NetworkHQ.DeregisterDropZone(transportDestination.touchdownPoint);
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} left AIFixedWingTransportState.");
        }
    }
}   