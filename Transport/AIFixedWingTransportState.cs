using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public partial class AIFixedWingTransportState : PilotBaseState
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
            public Vector3 arrivalHeading;
            public Vector3 windOffset;
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
                arrivalHeading = Vector3.zero;   
                windOffset = Vector3.zero;
            }
            public void UpdateLZ(Aircraft aircraft, GlobalPosition? targetPosition, float targetRadius, ref Vector3 approachDirection)
            {
                if (!targetPosition.HasValue)
                {
                    if (Plugin.Dbg) Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Target position is null.");
                    slope = 90f;
                    touchdownPointAttempts = 0;
                    return;
                }
                if (FastMath.InRange(aircraft.GlobalPosition(), touchdownPoint, 3000f))
                {
                    if (Plugin.Dbg) Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Within 3000m of touchdown point, committing to run.");
                    return;
                }
                approachDirection = FastMath.NormalizedDirection(targetPosition.Value, aircraft.GlobalPosition());
                approachDirection.y = 0f;
                GlobalPosition globalPosition = targetPosition.Value + approachDirection * (60f + targetRadius);
                float num = Mathf.Min(CombatAI.GetSafeStandoffDist(globalPosition, aircraft.NetworkHQ), 10000f);
                globalPosition += approachDirection * num;
                if (!FastMath.InRange(globalPosition, LZ, 100f))
                {
                    if (Plugin.Dbg) Plugin.Log.LogInfo($"[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Generating new LZ at distance {(globalPosition - targetPosition.Value).magnitude}m");
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
                Vector3 leadOffset = forwardDir * (unitSpeed * leadTime);
                Vector3 arrivalDir = forwardDir;
                arrivalHeading = arrivalDir;
                if (unitToRearm is Ship ship)
                {
                    touchdownPoint = ship.GlobalPosition() + leadOffset + windOffset;
                    slope = 0f;
                }
                else
                {
                    touchdownPoint = unitToRearm.GlobalPosition() + leadOffset + windOffset;
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
                    if (Plugin.Dbg) Plugin.Log.LogInfo("[SupplyBuffetMod][AIFixedWingTransportState] UpdateTouchdownPoint: Drop zone not clear.");
                    return;
                }
                slope = num;
                touchdownPoint = hitInfo.point.ToGlobalPosition();
                aircraft.NetworkHQ.RegisterDropZone(touchdownPoint);
                if (Plugin.Dbg) Plugin.Log.LogInfo($"[SupplyBuffetMod][AIFixedWingTransportState] Found touchdown point slope {num:F1} for {aircraft.unitName}");
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
        private const float DESCENT_ABORT_MARGIN = 1.5f;
        private const float DESCENT_HANDOFF_ALT = 1000f;
        private const float DRY_DROP_ALT = 700f;
        private const float NAVAL_DROP_ALT = 150f;   
        private const float AIRDROP_FAULT_GRACE = 1f;
        private const float RUN_LINE_B_DISTANCE = 800f;
        private const float RUNWAY_DROP_ALT_MIN = 2f;
        private const float RUNWAY_DROP_ALT_MAX = 8f;
        private Airbase.Runway repairRunway;
        private bool repairRunwayReverse;
        private GlobalPosition repairRunwayPoint;
        private bool gearDownForDrop;
        private const float ALIGN_TOLERANCE = 10f;
        private const float ALIGN_HOLD = 2f;
        private const float STAGE_TIMEOUT = 180f;
        private const float MODE_CHECK_INTERVAL = 2f;
        private const float RUN_ABORT_TOLERANCE = 40f;
        private const float LZ_ABORT_SHIFT = 1000f;
        private const int RUN_ATTEMPT_WARN_INTERVAL = 5;
        private const float DROP_ABORT_INTERVAL = 2f;
        private float alignedTime;
        private int runAttempts;
        private int dropAborts;
        private int dropPassesReleased;
        private float dropThrottleHold;
        private static readonly ConditionalWeakTable<Aircraft, StrongBox<float>> LastLandingHandoff =
            new ConditionalWeakTable<Aircraft, StrongBox<float>>();
        private float LandingHandoffStamp
        {
            get
            {
                return (aircraft != null && LastLandingHandoff.TryGetValue(aircraft, out StrongBox<float> box))
                    ? box.Value : 0f;
            }
            set
            {
                if (aircraft != null) LastLandingHandoff.GetOrCreateValue(aircraft).Value = value;
            }
        }
        private float lastDropAbortTime;
        private float releaseFaultSince;
        private readonly TransportDamageWatch damageWatch = new TransportDamageWatch();
        private bool directDrop;
        private bool jettisoning;
        private float nextJettisonAt;
        private GlobalPosition runStartTouchdown;
        private const float POST_DROP_BASE = 6f;
        private const float POST_DROP_PER_ITEM = 6f;   
        private float postDropHold = POST_DROP_BASE;
        private float stageStartedAt;
        private float lastModeCheck;
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
        private string LogName
        {
            get
            {
                if (aircraft == null) return "Chimera";
                string name = aircraft.unitName;
                uint id = aircraft.persistentID.Id;
                return (id != 0) ? name + "#" + id : name;
            }
        }
        public AIFixedWingTransportState(Aircraft aircraft)
        {
            base.aircraft = aircraft;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Instantiated AIFixedWingTransportState for {LogName}");
        }
        public override void EnterState(Pilot pilot)
        {
            stateDisplayName = "transporting cargo";
            phase = FlightPhase.Waiting;
            missionKind = MissionKind.None;
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            aircraftParameters = aircraft.GetAircraftParameters();
            Plugin.StampResupplyFlightStart(aircraft);
            defense = new ChimeraDefense(aircraft);
            deployedCargo = false;
            dropPassesReleased = 0;
            directDrop = false;
            jettisoning = false;
            nextJettisonAt = 0f;
            damageWatch.Attach(aircraft);
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
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} entered AIFixedWingTransportState.");
            if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.TryGetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit))
            {
                Vector3 vector = nearestUnit.lastKnownPosition - aircraft.GlobalPosition();
                vector.y = 0f;
                transportDestination = new TransportDestination(nearestUnit.lastKnownPosition - vector.normalized * 50f, nearestUnit.lastKnownPosition - vector.normalized * 50f, 90f);
            }
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
        private float RunAimAltitude(float altitudeTarget)
        {
            return transportDestination.touchdownPoint.y + altitudeTarget;
        }
        private float ClampAltitude(float altitude)
        {
            float floor = Mathf.Max(aircraft.maxRadius, aircraftParameters != null ? aircraftParameters.minimumRadarAlt : 0f);
            return Mathf.Clamp(altitude, floor, 8000f);
        }
        private bool IsRunwayRepair => missionKind == MissionKind.RunwayRepair;
        private float DropAltitude()
        {
            if (IsRunwayRepair) return RunwayDropAltitude();
            return NavalDropAltitude();
        }
        private float RunwayDropAltitude()
        {
            float configured = Plugin.Cfg(Plugin.ChimeraRunwayDropAltitude, 5f);
            return Mathf.Clamp(configured, RUNWAY_DROP_ALT_MIN, RUNWAY_DROP_ALT_MAX);
        }
        private float CruiseAltitude()
        {
            return NavalCruiseAltitude();
        }
        private float DescentDistance()
        {
            if (missionKind == MissionKind.RunwayRepair)
            {
                float runway = Plugin.Cfg(Plugin.ChimeraRunwayDescentDistance, 6000f);
                return Mathf.Max(1000f, runway);
            }
            float configured = Plugin.Cfg(Plugin.ChimeraDescentDistance, 5000f);
            return Mathf.Max(1000f, configured);
        }
        private void UpdateStateDisplayName()
        {
            stateDisplayName = PhaseLabel;
        }
        public string PhaseLabel
        {
            get
            {
                if (directDrop && phase == FlightPhase.Drop)
                {
                    if (!dryRunInArmed)
                    {
                        if (dryDescending) return "Transiting to Drop";
                        float above = aircraft.GlobalPosition().y - transportDestination.touchdownPoint.y;
                        return (above < CruiseAltitude() * 0.9f) ? "Climbing" : "En Route";
                    }
                    return "Running In";
                }
                switch (phase)
                {
                    case FlightPhase.Climb:     return "Climbing";
                    case FlightPhase.Cruise:    return "En Route";
                    case FlightPhase.Descent:   return "Descending";
                    case FlightPhase.Approach:  return "Joining";
                    case FlightPhase.Aligning:  return "Aligning";
                    case FlightPhase.Drop:      return "Approaching";
                    case FlightPhase.Exit:      return "Dropping";
                    case FlightPhase.Returning: return "Returning";
                    default:                    return "";
                }
            }
        }
        public string MissionTargetLabel => missionTargetLabel;
        public Unit AssignedTarget => assignedTargetUnit;
        public bool HasMission => missionKind != MissionKind.None;
        public string MissionKindLabel
        {
            get
            {
                switch (missionKind)
                {
                    case MissionKind.NavalSupply:   return "Naval Resupply";
                    case MissionKind.LandSupply:    return "Ground Resupply";
                    case MissionKind.CombatVehicle: return "Vehicle Delivery";
                    case MissionKind.RunwayRepair:  return "Runway Repair";
                    default:                        return "No Mission";
                }
            }
        }
        public int CargoAboard => CargoDemand.ItemsAboard(aircraft);
        public float DistanceToTarget
        {
            get
            {
                if (assignedTargetUnit == null || aircraft == null) return -1f;
                Vector3 flat = assignedTargetUnit.GlobalPosition() - aircraft.GlobalPosition();
                flat.y = 0f;
                return flat.magnitude;
            }
        }
        private void PingCargoBayDoors(float distanceToTarget)
        {
            if (distanceToTarget >= 3000f) return;
            if (Time.timeSinceLevelLoad - lastBayOpenPing <= 0.5f) return;
            if (!TryGetCargoStation(out WeaponStation cargoStation)) return;
            lastBayOpenPing = Time.timeSinceLevelLoad;
            foreach (Weapon w in cargoStation.Weapons)
            {
                Hardpoint hp = (w != null) ? HardpointRef(w) : null;
                if (hp != null) hp.SpringOpenBayDoors();
            }
        }
        private float RollDegrees => Mathf.Abs(Mathf.DeltaAngle(aircraft.transform.eulerAngles.z, 0f));
        private Vector3 FlatNose()
        {
            Vector3 nose = aircraft.transform.forward;
            nose.y = 0f;
            return nose;
        }
        private float ReleaseDropHeight()
        {
            if (IsRunwayRepair) return RunwayDropAltitude();
            if (missionKind == MissionKind.LandSupply || missionKind == MissionKind.CombatVehicle)
                return Plugin.Cfg(Plugin.DryPreferredRadarAltitude, 250f);
            return NavalDropAltitude();
        }
        private void CommitMissionGeometry(bool hadValidMission, GlobalPosition previousTouchdown)
        {
            UpdateStateDisplayName();
            Vector3 wind = MeanWind();
            transportDestination.windOffset = CargoRelease.WindOffset(wind, ReleaseDropHeight());
            if (Plugin.Dbg && transportDestination.windOffset.sqrMagnitude > 1f)
            {
                Plugin.Log.LogInfo($"[SB|W1] {LogName} wind {wind.magnitude:F1}m/s -> aim shifted {transportDestination.windOffset.magnitude:F0}m upwind from {ReleaseDropHeight():F0}m.");
            }
            if (!hadValidMission)
            {
                ComputeApproachPoints();
            }
            else if (!FastMath.InRange(transportDestination.touchdownPoint, previousTouchdown, 500f))
            {
                ComputeApproachPoints(restartRun: false);
            }
        }
        private void EnterAwaitingMission(string reason)
        {
            phase = FlightPhase.Waiting;
            missionKind = MissionKind.None;
            transportDestination.validMission = false;
            OrbitAirbase();
            bool wasWaiting = stateDisplayName == "Awaiting Cargo Mission";
            stateDisplayName = "Awaiting Cargo Mission";
            if (!wasWaiting)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} {reason}.");
            }
        }
        private void SearchForDropZone()
        {
            if (Time.timeSinceLevelLoad - lastLandingSpotCheck < 3f)
            {
                return;
            }
            lastLandingSpotCheck = Time.timeSinceLevelLoad;
            if (aircraft.weaponStations == null) return;
            if ((missionKind == MissionKind.LandSupply || missionKind == MissionKind.NavalSupply)
                && phase != FlightPhase.Returning
                && phase != FlightPhase.Exit
                && CargoDemand.ItemsAboard(aircraft) == 0)
            {
                if (assignedTargetUnit != null)
                {
                    ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
                    assignedTargetUnit = null;
                }
                transportDestination.validMission = false;
                phase = FlightPhase.Returning;
                UpdateStateDisplayName();
                Plugin.Log.LogInfo($"[SB|D9] {LogName} cargo hold is empty; returning to base.");
                return;
            }
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
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} releasing its lock mid-run ({why}); looking for another target.");
                    if (assignedTargetUnit != null) ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
                    assignedTargetUnit = null;
                    transportDestination.validMission = false;
                    runAttempts = 0;
                    dropAborts = 0;   
                }
                else
                {
                    transportDestination.UpdateLZ(aircraft, assignedTargetUnit);
                    return;
                }
            }
            bool rearmShip, rearmGround;
            if (ResupplyCensus.TryGetIsWet(aircraft, out bool taggedWet))
            {
                rearmShip = taggedWet;
                rearmGround = !taggedWet;
            }
            else
            {
                rearmShip = aircraft.weaponManager.currentWeaponStation.WeaponInfo.rearmShip;
                rearmGround = aircraft.weaponManager.currentWeaponStation.WeaponInfo.rearmGround;
            }
            GlobalPosition previousTouchdown = transportDestination.touchdownPoint;
            bool hadValidMission = transportDestination.validMission;
            Unit previousTarget = assignedTargetUnit;
            if (rearmShip || rearmGround)
            {
                if (aircraft.NetworkHQ != null && ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(aircraft.NetworkHQ.RearmMissionController, rearmShip, rearmGround, aircraft, out var lowestAmmoUnit, LoadedDryCategory(), DryHomeBase(), DryMinDeliveryRange()))
                {
                    assignedTargetUnit = lowestAmmoUnit;
                    if (previousTarget != lowestAmmoUnit) { runAttempts = 0; dropAborts = 0; }
                    ResupplyMissionManager.AssignTransport(lowestAmmoUnit, aircraft);
                    transportDestination.validMission = true;
                    timeWithoutMission = 0f;
                    if (!hadValidMission) transportDestination.UpdateLZ(aircraft, lowestAmmoUnit);
                    missionKind = rearmShip ? MissionKind.NavalSupply : MissionKind.LandSupply;
                    missionTargetLabel = $"{lowestAmmoUnit.unitName}";
                    if (!rearmShip && !hadValidMission)
                    {
                        transportDestination.UpdateTouchdownPoint(100f, aircraft);
                    }
                    CommitMissionGeometry(hadValidMission, previousTouchdown);
                    if (!hadValidMission || previousTarget != lowestAmmoUnit)
                    {
                        Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} assigned to resupply {lowestAmmoUnit.unitName} ({stateDisplayName}). LZ: {transportDestination.touchdownPoint}");
                    }
                }
                else
                {
                    if (assignedTargetUnit != null)
                    {
                        ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
                        assignedTargetUnit = null;
                    }
                    EnterAwaitingMission("awaiting cargo mission");
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
                CommitMissionGeometry(hadValidMission, previousTouchdown);
                if (!hadValidMission)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} assigned to drop vehicle at {targetPosition.Value} ({stateDisplayName}).");
                }
            }
            else
            {
                EnterAwaitingMission("no valid drop zone found, awaiting mission");
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
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} without mission for >45s. Returning to land.");
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
            TransportGear.Apply(aircraft, gearDownForDrop);
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
            CheckBattleDamage();
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
                directDrop = UseDirectDrop();
                if (directDrop && phase != FlightPhase.Exit)
                {
                    RunDryMission();
                    EjectionCheck();
                    TargetSearch();
                    DefendWithMissiles();
                    return;
                }
                float descentHold = Plugin.Cfg(Plugin.DescentHandoffAltitude, DESCENT_HANDOFF_ALT);
                float altitudeTarget;
                if (transiting) altitudeTarget = CruiseAltitude();
                else if (phase == FlightPhase.Descent) altitudeTarget = descentHold;
                else altitudeTarget = DropAltitude();
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
                aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), true, false, false, 1f, TransitBankLimit(), false, 0f, Vector3.zero);
            }
            EjectionCheck();
            TargetSearch();
            DefendWithMissiles();
        }
        private void RunTransitPhase(float altitudeTarget)
        {
            controlInputs.throttle = aircraftParameters.cruiseThrottle;
            GlobalPosition aimPos = pointA;
            aimPos.y = RunAimAltitude(altitudeTarget);
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), true, false, false, 0.85f, TransitBankLimit(), false, 0f, AssignedTargetVelocity());
            float distToA = FastMath.Distance(aircraft.GlobalPosition(), pointA);
            float distToTarget = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
            float gateDist = distToA;
            if (gateDist <= DescentDistance())
            {
                if (phase != FlightPhase.Descent)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} descending for the drop run ({gateDist:F0}m to Point A, {distToTarget:F0}m to the target).");
                    phase = FlightPhase.Descent;
                    UpdateStateDisplayName();
                }
                float handoff = aircraftParameters.turningRadius * 3f;
                if (gateDist <= handoff)
                {
                    if (phase != FlightPhase.Approach)
                    {
                        EnterStage(FlightPhase.Approach);
                    }
                }
                return;
            }
            float heightAboveTarget = aircraft.GlobalPosition().y - transportDestination.touchdownPoint.y;
            if (phase == FlightPhase.Climb && heightAboveTarget >= altitudeTarget * 0.9f)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} reached cruise altitude ({heightAboveTarget:F0}m above the target).");
                phase = FlightPhase.Cruise;
                UpdateStateDisplayName();
            }
            else if (phase == FlightPhase.Descent)
            {
                GlobalPosition gate = pointA;
                Vector3 toGate = gate - aircraft.GlobalPosition();
                bool passedGate = Vector3.Dot(aircraft.transform.forward, toGate) < 0f;
                if (!passedGate && gateDist > DescentDistance() * DESCENT_ABORT_MARGIN)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} climbing back to cruise; Point A is now {gateDist:F0}m away.");
                    phase = FlightPhase.Cruise;
                    UpdateStateDisplayName();
                }
            }
        }
        private Vector3 runLineCorrection;
        private float lastGovernorLog;
        private string AltitudeTrace(float altitudeTarget)
        {
            float aim = RunAimAltitude(altitudeTarget);
            return $"aim {aim:F0}m (floor {runFloorY:F0}, pointC {pointC.y:F0}) hold {altitudeTarget:F0}m";
        }
        private float TransitBankLimit()
        {
            return Plugin.Cfg(Plugin.TransitBankLimit, 110f);
        }
        private Vector3 MeanWind()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level == null) return Vector3.zero;
            Vector3 wind = level.GetWind();
            wind.y = 0f;
            return wind;
        }
        private float CrabAngle()
        {
            if (aircraft.rb == null) return 0f;
            Vector3 vel = aircraft.rb.velocity; vel.y = 0f;
            if (vel.sqrMagnitude < 1f) return 0f;
            Vector3 nose = FlatNose();
            if (nose.sqrMagnitude < 0.01f) return 0f;
            return Vector3.SignedAngle(nose, vel, Vector3.up);
        }
        private float AlphaAngle()
        {
            if (aircraft.rb == null) return 0f;
            Vector3 local = aircraft.transform.InverseTransformDirection(aircraft.rb.velocity);
            if (local.sqrMagnitude < 1f) return 0f;
            return -Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
        }
        private string FlowTrace()
        {
            Vector3 wind = MeanWind();
            Vector3 across = Vector3.Cross(Vector3.up, approachAxis);
            return $"wind {wind.magnitude:F0}m/s (along {Vector3.Dot(wind, approachAxis):F0}, cross {Vector3.Dot(wind, across):F0}) crab {CrabAngle():F0}deg alpha {AlphaAngle():F0}deg";
        }
        private void LogSpeedGovernor(string phase, float scheduled, float floor, float target, float dist)
        {
            if (!Plugin.Dbg) return;
            if (Time.timeSinceLevelLoad - lastGovernorLog < MODE_CHECK_INTERVAL) return;
            lastGovernorLog = Time.timeSinceLevelLoad;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} {phase}: speed {aircraft.speed:F0} target {target:F0} (sched {scheduled:F0}, floor {floor:F0}) throttle {controlInputs.throttle:F2} {FlowTrace()} dist {dist:F0}m");
        }
        private bool ModeCheckDue()
        {
            if (Time.timeSinceLevelLoad - lastModeCheck < MODE_CHECK_INTERVAL) return false;
            lastModeCheck = Time.timeSinceLevelLoad;
            return true;
        }
        private bool RunInProgress()
        {
            if (assignedTargetUnit == null || assignedTargetUnit.disabled) return false;
            if (!transportDestination.validMission) return false;
            return phase == FlightPhase.Approach
                || phase == FlightPhase.Aligning
                || phase == FlightPhase.Drop;
        }
        private void EnterTransitOrRun()
        {
            bool high = aircraft.radarAlt >= CruiseAltitude() * 0.9f;
            if (UseDirectDrop())
            {
                EnterStage(FlightPhase.Cruise);
                return;
            }
            EnterStage(FastMath.Distance(aircraft.GlobalPosition(), pointA) <= DescentDistance()
                ? FlightPhase.Approach
                : (high ? FlightPhase.Cruise : FlightPhase.Climb));
        }
        private void EnterStage(FlightPhase newPhase)
        {
            phase = newPhase;
            runLineCorrection = Vector3.zero;
            stageStartedAt = Time.timeSinceLevelLoad;
            lastModeCheck = Time.timeSinceLevelLoad;
            alignedTime = 0f;
            reachedFinal = false;
            UpdateStateDisplayName();
        }
        private void BeginDropPhase()
        {
            itemsReleased = 0;
            nextReleaseAt = 0f;
            int aboard = CargoDemand.ItemsAboard(aircraft);
            if (directDrop)
            {
                itemsToRelease = aboard;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} dry delivery for {(assignedTargetUnit != null ? assignedTargetUnit.unitName : "a drop point")}: releasing the whole load, {aboard} item(s).");
            }
            else if (assignedTargetUnit == null)
            {
                itemsToRelease = Mathf.Min(1, aboard);
            }
            else
            {
                string cargoKey = CurrentCargoMountKey();
                if (SupplyFullRestore.IsFullRestore(cargoKey))
                {
                    itemsToRelease = Mathf.Min(1, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} dropping 1 for {assignedTargetUnit.unitName}: '{cargoKey}' is a full-restore supply item.");
                }
                else if (missionKind == MissionKind.NavalSupply)
                {
                    float sensitivity = Plugin.Cfg(Plugin.RearmRequestSensitivity, 0.5f);
                    float maxCapacity = assignedTargetUnit.TryGetComponent(out Rearmer targetRearmer)
                        ? targetRearmer.GetMaxCapacity()
                        : CargoDemand.FullLoadMass(assignedTargetUnit);
                    float threshold = (1f - sensitivity) * maxCapacity;
                    float perItem = CargoDemand.ItemCapacity(true, cargoKey);
                    itemsToRelease = (threshold > perItem) ? aboard : Mathf.Min(1, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} sizing wet drop for {assignedTargetUnit.unitName}: threshold {threshold:F0} (maxCapacity {maxCapacity:F0}) vs {perItem:F0} per crate -> {itemsToRelease} of {aboard} aboard.");
                }
                else if (CargoDemand.IsPalletStick(cargoKey))
                {
                    itemsToRelease = aboard;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} dropping the whole stick for {assignedTargetUnit.unitName}: '{cargoKey}' is a pallet stick -> {itemsToRelease} of {aboard} aboard.");
                }
                else
                {
                    float demand = CargoDemand.FullLoadMass(assignedTargetUnit);
                    float perItem = CargoDemand.ItemCapacity(false, cargoKey);
                    itemsToRelease = CargoDemand.ItemsToRelease(demand, perItem, aboard);
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} sizing drop for {assignedTargetUnit.unitName}: demand {demand:F0} / {perItem:F0} per item -> {itemsToRelease} of {aboard} aboard.");
                }
            }
            lastDropAbortTime = 0f;
            releaseFaultSince = 0f;
            gearDownForDrop = (missionKind == MissionKind.RunwayRepair);
            runStartTouchdown = transportDestination.touchdownPoint;
            EnterStage(FlightPhase.Drop);
        }
        private SortieCategory? LoadedDryCategory()
        {
            if (missionKind == MissionKind.NavalSupply || IsRunwayRepair) return null;
            if (!TryGetCargoStation(out WeaponStation station) || station == null) return null;
            if (station.FullAmmo <= 0) return null;
            return (station.FullAmmo <= 1) ? SortieCategory.DryStatic : SortieCategory.DryMoving;
        }
        private GlobalPosition? DryHomeBase()
        {
            if (missionKind == MissionKind.NavalSupply || IsRunwayRepair) return null;
            if (!ResupplyCensus.TryGetHomeBase(aircraft, out Airbase home) || home == null) return null;
            return home.center.GlobalPosition();
        }
        private float DryMinDeliveryRange()
        {
            if (missionKind == MissionKind.NavalSupply || IsRunwayRepair) return 0f;
            return Plugin.Cfg(Plugin.ThresholdB, 15000f);
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
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} has no cargo to release; ending the run.");
                gearDownForDrop = false;
                phase = FlightPhase.Exit;
                lastCargoDroppedTime = Time.timeSinceLevelLoad;
                UpdateStateDisplayName();
                return;
            }
            if (missionKind == MissionKind.RunwayRepair)
            {
                controlInputs.throttle = 0.72f;
            }
            else
            {
                float runSpeed = ApproachSpeedFloor();
                controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - runSpeed) * 0.1f, 0f, 1f);
                if (Plugin.Dbg)
                {
                    LogSpeedGovernor($"Dropping corr {runLineCorrection.magnitude:F1}m {AltitudeTrace(altitudeTarget)}", runSpeed, runSpeed, runSpeed,
                                     FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint));
                }
            }
            Vector3 toC = pointC - aircraft.GlobalPosition();
            toC.y = 0f;
            Vector3 runVel = aircraft.rb.velocity;
            runVel.y = 0f;
            Vector3 toTouchdownNow = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTouchdownNow.y = 0f;
            UpdateRunLineCorrection(toTouchdownNow.magnitude);
                GlobalPosition aimPos = RunLineAimPoint() - runLineCorrection;
                aimPos.y = RunAimAltitude(altitudeTarget);
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), true, false, false, 1.01f, 65f, false, 0f, Vector3.zero);
            Vector3 toTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTarget.y = 0f;
            float horizDist = toTarget.magnitude;
            if (!deployedCargo) PingCargoBayDoors(horizDist);
            ReleaseInputs release = BuildReleaseInputs(wet: true, runAxis: approachAxis);
            float navalB = CargoRelease.Distance(release);
            bool reachedB = CargoRelease.RingReached(release, navalB, out float navalSlant, out float navalTrip)
                            || CargoRelease.PastTarget(release);
            float navalHeight = Mathf.Max(-release.ToTarget.y, 0f);
            bool outOfAttempts = dropAborts >= MaxAirdropAttempts();
            bool atReleasePoint = reachedB;
            string releaseFault = null;
            bool airdropReady = (missionKind == MissionKind.RunwayRepair) || AirdropReleaseReady(out releaseFault);
            if (airdropReady || !atReleasePoint) releaseFaultSince = 0f;
            else if (releaseFaultSince == 0f) releaseFaultSince = Time.timeSinceLevelLoad;
            bool releaseReady = (missionKind == MissionKind.RunwayRepair)
                ? CargoRelease.RunwayReady(release, DropAltitude())
                : (atReleasePoint && (outOfAttempts || airdropReady));
            if (itemsReleased == 0 && releaseReady)
            {
                Vector3 horizVec = transportDestination.touchdownPoint - aircraft.GlobalPosition();
                horizVec.y = 0f;
                float horizToTarget = horizVec.magnitude;
                float sink = (aircraft.rb != null) ? aircraft.rb.velocity.y : 0f;
                float roll = RollDegrees;
                float lead = (assignedTargetUnit != null)
                    ? FastMath.Distance(transportDestination.touchdownPoint, assignedTargetUnit.GlobalPosition())
                    : 0f;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} releasing {itemsToRelease} item(s): {release.Crate} horiz-to-target={horizToTarget:F0}m alt={(aircraft.GlobalPosition().y - pointC.y):F0}m radarAlt={aircraft.radarAlt:F1}m speed={aircraft.speed:F0}m/s roll={roll:F0}deg vs={sink:F1}m/s lead={lead:F0}m wind={MeanWind().magnitude:F1}m/s windOff={transportDestination.windOffset.magnitude:F0}m slant={navalSlant:F0}m trip={navalTrip:F0}m(b={navalB:F0} h={navalHeight:F0}) forced={outOfAttempts}");
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
                const float faultGrace = AIRDROP_FAULT_GRACE;
                bool faultHeld = !airdropReady && releaseFaultSince > 0f
                    && Time.timeSinceLevelLoad - releaseFaultSince >= faultGrace;
                bool faultIsHeight = !string.IsNullOrEmpty(releaseFault) && releaseFault.StartsWith("height ");
                if (atReleasePoint && missionKind != MissionKind.RunwayRepair
                    && Time.timeSinceLevelLoad - lastDropAbortTime >= DROP_ABORT_INTERVAL
                    && faultHeld && !faultIsHeight)
                {
                    dropAborts++;
                    lastDropAbortTime = Time.timeSinceLevelLoad;
                    string why = $"unstable at the release point ({releaseFault}), abort {dropAborts} of {MaxAirdropAttempts()}";
                    RestartRun(why);
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
        private Vector3 AssignedTargetVelocity()
        {
            if (assignedTargetUnit == null || assignedTargetUnit.rb == null) return Vector3.zero;
            return assignedTargetUnit.rb.velocity;
        }
        private int MaxAirdropAttempts()
        {
            return Plugin.Cfg(Plugin.ChimeraAirdropMaxAttempts, 3);
        }
        private bool AirdropReleaseReady(out string fault)
        {
            return NavalReleaseReady(out fault);
        }
        private bool AttitudeReleaseReady(out string fault)
        {
            float maxRoll = Plugin.Cfg(Plugin.ChimeraAirdropMaxRoll, 20f);
            float roll = RollDegrees;
            if (roll > maxRoll)
            {
                fault = $"roll {roll:F0}deg > {maxRoll:F0}";
                return false;
            }
            float maxVertical = Plugin.Cfg(Plugin.ChimeraAirdropMaxVerticalSpeed, 10f);
            float sink = (aircraft.rb != null) ? Mathf.Abs(aircraft.rb.velocity.y) : 0f;
            if (sink > maxVertical)
            {
                fault = $"vertical speed {sink:F0}m/s > {maxVertical:F0}";
                return false;
            }
            fault = string.Empty;
            return true;
        }
        private void ReleaseCargoStep()
        {
            if (itemsReleased >= itemsToRelease) return;
            float now = Time.timeSinceLevelLoad;
            if (now < nextReleaseAt) return;
            if (!TryGetCargoStation(out WeaponStation station)) return;
            aircraft.weaponManager.currentWeaponStation = station;
            int ammoBefore = station.Ammo;
            station.LaunchMount(aircraft, null, transportDestination.touchdownPoint);
            if (station.Ammo >= ammoBefore)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] {LogName} released a crate but the station ammo did not drop ({ammoBefore}); counting it anyway.");
            }
            itemsReleased++;
            nextReleaseAt = now + Plugin.Cfg(Plugin.ChimeraReleaseInterval, 0.2f);
            lastCargoDroppedTime = now;
            if (itemsReleased == 1) OnFirstRelease();
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} released cargo {itemsReleased}/{itemsToRelease}.");
        }
        private static bool IsHoldingPosition(Unit unit)
        {
            if (unit is Ship ship) return ship.holdPosition;
            if (unit is GroundVehicle gv) return gv.GetHoldPosition();
            return true;
        }
        private void CheckBattleDamage()
        {
            if (jettisoning)
            {
                JettisonStep();
                return;
            }
            if (!damageWatch.ShouldReturn()) return;
            jettisoning = true;
            nextJettisonAt = 0f;
            assignedTargetUnit = null;      
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} battle damage: {damageWatch.Describe()}; jettisoning cargo and returning to base.");
            JettisonStep();
        }
        private void JettisonStep()
        {
            if (!TryGetCargoStation(out WeaponStation station))
            {
                jettisoning = false;
                if (phase != FlightPhase.Returning)
                {
                    phase = FlightPhase.Returning;
                    UpdateStateDisplayName();
                }
                return;
            }
            float now = Time.timeSinceLevelLoad;
            if (now < nextJettisonAt) return;
            nextJettisonAt = now + Plugin.Cfg(Plugin.ChimeraReleaseInterval, 0.2f);
            aircraft.weaponManager.currentWeaponStation = station;
            station.LaunchMount(aircraft, null, aircraft.GlobalPosition());
        }
        private ReleaseInputs BuildReleaseInputs(bool wet, Vector3 runAxis)
        {
            TryGetCargoStation(out WeaponStation station);
            return new ReleaseInputs
            {
                ToTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition(),
                Velocity = (aircraft.rb != null) ? aircraft.rb.velocity : Vector3.zero,
                RunAxis = runAxis,
                Speed = (aircraft.rb != null) ? aircraft.rb.velocity.magnitude : aircraft.speed,
                RollDegrees = RollDegrees,
                RadarAlt = aircraft.radarAlt,
                VerticalSpeed = (aircraft.rb != null) ? aircraft.rb.velocity.y : 0f,
                Wet = wet,
                Crate = CargoRelease.CrateOf(station),
                CargoKey = CurrentCargoMountKey()
            };
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
            bool moreToDeliver = CargoDemand.ItemsAboard(aircraft) > 0;
            aimPos.y = aircraft.GlobalPosition().y + (moreToDeliver ? 0f : altitudeTarget);
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aimPos), true, false, false, 1f, TransitBankLimit(), false, 0f, Vector3.zero);
            if (moreToDeliver)
            {
                if (directDrop)
                {
                    controlInputs.throttle = Mathf.Max(dropThrottleHold, 0.05f);
                }
                else
                {
                    float turnSpeed = Mathf.Max(aircraftParameters.cornerSpeed
                        * (Plugin.Cfg(Plugin.DryTurnCornerSpeedFraction, 0.85f)),
                        aircraftParameters.landingSpeed * 1.2f);
                    if (aircraft.speed > turnSpeed * 1.15f)
                    {
                        controlInputs.throttle = 0f;
                    }
                    else
                    {
                        controlInputs.throttle = Mathf.Clamp(aircraftParameters.cruiseThrottle + (turnSpeed - aircraft.speed) * 0.015f, 0.05f, 1f);
                    }
                }
                controlInputs.brake = 0f;
            }
            else if (directDrop)
            {
                controlInputs.throttle = Mathf.Max(dropThrottleHold, 0.05f);
                controlInputs.brake = 0f;
            }
            else
            {
                controlInputs.throttle = 1f;
                controlInputs.brake = 0f;
            }
            if (Time.timeSinceLevelLoad - lastCargoDroppedTime <= postDropHold) return;
            if (!directDrop && CargoDemand.ItemsAboard(aircraft) > 0 && aircraft.NetworkHQ != null
                && ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(
                       aircraft.NetworkHQ.RearmMissionController,
                       missionKind == MissionKind.NavalSupply,
                       missionKind != MissionKind.NavalSupply,
                       aircraft, out Unit nextTarget))
            {
                Plugin.Log.LogInfo($"[SB|D8] {LogName} has cargo remaining; re-tasking to {nextTarget.unitName}.");
                assignedTargetUnit = nextTarget;
                runAttempts = 0;
                dropAborts = 0;   
                ResupplyMissionManager.AssignTransport(nextTarget, aircraft);
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
            Plugin.Log.LogInfo($"[SB|D9] {LogName} completed airdrop pass. Returning to base.");
            phase = FlightPhase.Returning;
            UpdateStateDisplayName();
        }
        private void RunReturnPhase()
        {
            bool threat = IncomingMissile();
            float retry = Plugin.Cfg(Plugin.LandingRetrySeconds, 10f);
            float stamp = LandingHandoffStamp;
            bool cooling = stamp > 0f && Time.timeSinceLevelLoad - stamp < retry;
            if (threat || cooling)
            {
                FlyEgressToBase(threat);
                return;
            }
            LandingHandoffStamp = Time.timeSinceLevelLoad;
            if (pilot.AILandingState == null)
            {
                pilot.AILandingState = new AIPilotLandingState();
            }
            pilot.SwitchState(pilot.AILandingState);
        }
        private bool IncomingMissile()
        {
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> missiles = (warning != null) ? warning.knownMissiles : null;
            if (missiles == null) return false;
            for (int i = 0; i < missiles.Count; i++)
            {
                Missile m = missiles[i];
                if (m == null || m.disabled) continue;
                if (m.targetID.Equals(aircraft.persistentID)) return true;
            }
            return false;
        }
        private void FlyEgressToBase(bool threat)
        {
            aircraft.SetFlightAssist(true);
            if (aircraft.gearDeployed) aircraft.SetGear(false);
            controlInputs.throttle = threat ? 1f : aircraftParameters.cruiseThrottle;
            controlInputs.brake = 0f;
            if (nearestAirbase == null || nearestAirbase.center == null) return;
            GlobalPosition aim = nearestAirbase.center.GlobalPosition();
            aim.y += CruiseAltitude();
            aircraft.autopilot.AutoAim(TerrainGuard.Raise(aircraft, aircraftParameters, aim), true, false, false, 1f, 60f, false, 0f, Vector3.zero);
        }
        private void OnFirstRelease()
        {
            dropThrottleHold = controlInputs.throttle;
            int remaining = CargoDemand.ItemsAboard(aircraft);
            float holdBase = Plugin.Cfg(Plugin.PostDropHoldBase, POST_DROP_BASE);
            float holdPerCrate = Plugin.Cfg(Plugin.PostDropHoldPerCrate, POST_DROP_PER_ITEM);
            postDropHold = (remaining > 0) ? holdBase : holdBase + holdPerCrate * remaining;
            Plugin.TriggerControlNullifier(aircraft, postDropHold);
            if (Plugin.Dbg)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} holding the drop line for {postDropHold:F0}s ({remaining} crate(s) still aboard).");
            }
            defense.TriggerDropBurst();
            pilot.flightInfo.LastCargoDelivery = Time.timeSinceLevelLoad;
            pilot.flightInfo.EnemyContact = true;
            deployedCargo = true;
            dropPassesReleased++;
            if (assignedTargetUnit != null)
            {
                ResupplyDispatcher.MarkDropped(assignedTargetUnit);
                ResupplyMissionManager.UnassignTransport(assignedTargetUnit);
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
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Ejection condition met for {LogName}. Ejecting!");
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
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} target changed to {currentTarget.unitName}");
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
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} defending with missiles against {currentTarget.unitName}!");
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
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {LogName} left AIFixedWingTransportState (phase={phase}, jettisoning={jettisoning}, aircraft.disabled={(aircraft != null ? aircraft.disabled.ToString() : "null")}).");
            if (defense != null) defense.Stop();
            damageWatch.Detach();
            if (aircraft != null && aircraft.NetworkHQ != null)
            {
                aircraft.NetworkHQ.DeregisterDropZone(transportDestination.touchdownPoint);
            }
        }
    }
}   