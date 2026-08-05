using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Unity.Profiling;
namespace SupplyBuffetMod
{
    public class AIFixedWingTransportState : PilotBaseState
    {
        public enum TransportMode
        {
            CombatVehicleApproach,
            CombatVehicleDrop,
            CombatVehicleExit,
            LandSupplyApproach,
            LandSupplyDrop,
            LandSupplyExit,
            NavalSupplyApproach,
            NavalSupplyDrop,
            NavalSupplyExit,
            Radar,
            Waiting
        }
        private static bool IsNaval(TransportMode m)
        {
            return m == TransportMode.NavalSupplyApproach || m == TransportMode.NavalSupplyDrop || m == TransportMode.NavalSupplyExit;
        }
        private static bool IsApproach(TransportMode m)
        {
            return m == TransportMode.CombatVehicleApproach || m == TransportMode.LandSupplyApproach || m == TransportMode.NavalSupplyApproach;
        }
        private static bool IsDrop(TransportMode m)
        {
            return m == TransportMode.CombatVehicleDrop || m == TransportMode.LandSupplyDrop || m == TransportMode.NavalSupplyDrop;
        }
        private static bool IsExit(TransportMode m)
        {
            return m == TransportMode.CombatVehicleExit || m == TransportMode.LandSupplyExit || m == TransportMode.NavalSupplyExit;
        }
        private static TransportMode ToApproach(TransportMode m)
        {
            switch (m)
            {
                case TransportMode.CombatVehicleApproach:
                case TransportMode.CombatVehicleDrop:
                case TransportMode.CombatVehicleExit:
                    return TransportMode.CombatVehicleApproach;
                case TransportMode.LandSupplyApproach:
                case TransportMode.LandSupplyDrop:
                case TransportMode.LandSupplyExit:
                    return TransportMode.LandSupplyApproach;
                case TransportMode.NavalSupplyApproach:
                case TransportMode.NavalSupplyDrop:
                case TransportMode.NavalSupplyExit:
                    return TransportMode.NavalSupplyApproach;
                default:
                    return m;
            }
        }
        private static TransportMode ToDrop(TransportMode m)
        {
            switch (m)
            {
                case TransportMode.CombatVehicleApproach:
                case TransportMode.CombatVehicleDrop:
                case TransportMode.CombatVehicleExit:
                    return TransportMode.CombatVehicleDrop;
                case TransportMode.LandSupplyApproach:
                case TransportMode.LandSupplyDrop:
                case TransportMode.LandSupplyExit:
                    return TransportMode.LandSupplyDrop;
                case TransportMode.NavalSupplyApproach:
                case TransportMode.NavalSupplyDrop:
                case TransportMode.NavalSupplyExit:
                    return TransportMode.NavalSupplyDrop;
                default:
                    return m;
            }
        }
        private static TransportMode ToExit(TransportMode m)
        {
            switch (m)
            {
                case TransportMode.CombatVehicleApproach:
                case TransportMode.CombatVehicleDrop:
                case TransportMode.CombatVehicleExit:
                    return TransportMode.CombatVehicleExit;
                case TransportMode.LandSupplyApproach:
                case TransportMode.LandSupplyDrop:
                case TransportMode.LandSupplyExit:
                    return TransportMode.LandSupplyExit;
                case TransportMode.NavalSupplyApproach:
                case TransportMode.NavalSupplyDrop:
                case TransportMode.NavalSupplyExit:
                    return TransportMode.NavalSupplyExit;
                default:
                    return m;
            }
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

                // Lead distance scales with actual target speed instead of a flat offset,
                // so slow/stationary units get a touchdown point near their real position.
                float leadTime = (unitToRearm is Ship) ? 30f : 15f;
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
                Vector2 vector = Random.insideUnitCircle * Mathf.Min(50 * touchdownPointAttempts, maxRadius);
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
        private TransportMode transportMode;
        private TransportDestination transportDestination;
        private AircraftParameters aircraftParameters;
        private float lastEjectionCheck;
        private float lastLandingSpotCheck;
        private float missileReactTime;
        private float countermeasuresLastSelected;
        private float targetDist;
        private float lastFiredTime;
        private float lastTargetAssessTime;
        private float lastLoSCheck;
        private float timeWithoutMission;
        private float lastCargoDroppedTime = -100f;
        // Weapon.hardpoint is protected; cached FieldRef so the bay doors can be opened
        // during the run-in instead of at Fire() time (vanilla only opens them on firing,
        // and RailLaunch then blocks until the ramp finishes hinging open).
        private static readonly AccessTools.FieldRef<Weapon, Hardpoint> HardpointRef =
            AccessTools.FieldRefAccess<Weapon, Hardpoint>("hardpoint");
        private float lastBayOpenPing;
        private GlobalPosition pointA;
        private GlobalPosition pointB;
        private GlobalPosition pointC;
        private Vector3 approachAxis;
        private float lastApproachRecalc;
        private string missionTargetLabel = "";
        private bool deployedCargo;
        private bool targetLoS;
        private List<Missile> missileAlerts = new List<Missile>();
        private Unit currentTarget;
        private TrackingInfo currentTargetTracking;
        private Vector3 approachDirection;
        private string countermeasureType;
        public Unit assignedTargetUnit;
        public AIFixedWingTransportState(Aircraft aircraft)
        {
            base.aircraft = aircraft;
            if (aircraft != null && aircraft.GetMissileWarningSystem() != null)
            {
                missileAlerts = aircraft.GetMissileWarningSystem().knownMissiles;
                aircraft.GetMissileWarningSystem().onMissileWarning += FixedWingTransport_OnMissileAlert;
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Instantiated AIFixedWingTransportState for {aircraft?.unitName}");
        }
        public override void EnterState(Pilot pilot)
        {
            stateDisplayName = "transporting cargo";
            transportMode = TransportMode.Waiting;
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            aircraftParameters = aircraft.GetAircraftParameters();
            deployedCargo = false;
            lastBayOpenPing = 0f;
            approachDirection = aircraft.transform.forward;
            timeWithoutMission = 0f;
            nearestAirbase = aircraft.NetworkHQ?.GetNearestAirbase(aircraft.transform.position);
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
        private void FixedWingTransport_OnMissileAlert(MissileWarning.OnMissileWarning e)
        {
            if (missileReactTime <= 0f)
            {
                missileReactTime = -0.5f / Mathf.Clamp(Vector3.Dot(FastMath.NormalizedDirection(aircraft.transform.position, e.missile.transform.position), aircraft.transform.forward), 0.2f, 1f);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Missile alert on {aircraft.unitName}! React time: {missileReactTime}");
            }
        }
        private void ChooseCountermeasures()
        {
            countermeasuresLastSelected = Time.timeSinceLevelLoad;
            if (missileAlerts.Count > 1)
            {
                missileAlerts.Sort((Missile a, Missile b) => Vector3.Distance(a.transform.position, aircraft.transform.position).CompareTo(Vector3.Distance(b.transform.position, aircraft.transform.position)));
            }
            if (aircraft.countermeasureManager != null && missileAlerts.Count > 0)
            {
                countermeasureType = aircraft.countermeasureManager.ChooseCountermeasure(missileAlerts[0]);
            }
        }
        private void Countermeasures()
        {
            if (missileAlerts == null || missileAlerts.Count == 0)
            {
                missileReactTime = 0f;
                if (pilot.aircraft.countermeasureTrigger)
                {
                    aircraft.Countermeasures(active: false, aircraft.countermeasureManager.activeIndex);
                }
                return;
            }
            if (Time.timeSinceLevelLoad - countermeasuresLastSelected > 2f)
            {
                ChooseCountermeasures();
            }
            missileReactTime += Time.deltaTime;
            if (countermeasureType == "IR")
            {
                if (missileReactTime > 0f && missileReactTime < 2f)
                {
                    if (!pilot.aircraft.countermeasureTrigger)
                    {
                        aircraft.Countermeasures(active: true, aircraft.countermeasureManager.activeIndex);
                    }
                }
                else if (pilot.aircraft.countermeasureTrigger)
                {
                    aircraft.Countermeasures(active: false, aircraft.countermeasureManager.activeIndex);
                }
            }
        }
        /// <summary>
        /// Derives the three run points along a stable approach axis:
        ///   A = target - axis * 5000       (approach / join point)
        ///   B = target - axis * 800 or 600 (drop point - the ramp/rail sequence after
        ///                                   Fire() is async and needs seconds, not metres,
        ///                                   of lead. 800 when the target is holding
        ///                                   position, for a pinpoint drop; 600 when it is
        ///                                   under way, because UpdateLZ has already led
        ///                                   the touchdown point ahead of it)
        ///   C = target - axis * 150        (exit point)
        /// The axis comes from the target's own facing/velocity (or the standoff approach
        /// direction for vehicle drops) rather than the aircraft's transient position, so
        /// it doesn't degenerate when the aircraft is already close.
        /// </summary>
        private void ComputeApproachPoints()
        {
            if (!transportDestination.validMission)
            {
                transportMode = TransportMode.Waiting;
                stateDisplayName = "Awaiting Cargo Mission";
                return;
            }

            GlobalPosition target = transportDestination.touchdownPoint;
            Vector3 axis;
            if (assignedTargetUnit != null)
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
            // Static target -> release earlier for a pinpoint drop. Moving target -> release
            // later, because UpdateLZ has already led the touchdown point ahead of the unit.
            bool hold = assignedTargetUnit == null || IsHoldingPosition(assignedTargetUnit);
            pointA = target - axis * 5000f;
            pointB = target - axis * (hold ? 800f : 600f);
            pointC = target - axis * 150f;

            transportMode = ToApproach(transportMode);

            lastApproachRecalc = Time.timeSinceLevelLoad;
            UpdateStateDisplayName();
        }
        /// <summary>
        /// Refreshes stateDisplayName from the current mission label and delivery phase,
        /// so the visible state genuinely reflects Approach vs Drop Run vs Exit.
        /// </summary>
        private void UpdateStateDisplayName()
        {
            string phase = IsApproach(transportMode) ? "Approaching"
                : IsDrop(transportMode) ? "Aligning"
                : IsExit(transportMode) ? "Supplying"
                : "";
            stateDisplayName = string.IsNullOrEmpty(missionTargetLabel) ? phase : $"{phase}: {missionTargetLabel}";
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
                    ResupplyMissionManager.AssignChimera(lowestAmmoUnit, aircraft);
                    transportDestination.validMission = true;
                    if (!hadValidMission) transportDestination.UpdateLZ(aircraft, lowestAmmoUnit);
                    if (rearmShip)
                    {
                        if (!hadValidMission) transportMode = TransportMode.NavalSupplyApproach;
                        missionTargetLabel = $"{lowestAmmoUnit.unitName}";
                    }
                    else
                    {
                        if (!hadValidMission) transportMode = TransportMode.LandSupplyApproach;
                        if (!hadValidMission) transportDestination.UpdateTouchdownPoint(100f, aircraft);
                        missionTargetLabel = $"{lowestAmmoUnit.unitName}";
                    }
                    UpdateStateDisplayName();
                    // Only re-derive Point A/B (and reset to Approach) on a genuinely new
                    // mission or a meaningfully-moved LZ - not on every 3s re-confirmation
                    // of the same still-valid target, which would keep resetting the phase
                    // and prevent ever reaching Final.
                    if (!hadValidMission || !FastMath.InRange(transportDestination.touchdownPoint, previousTouchdown, 500f))
                    {
                        ComputeApproachPoints();
                    }
                    // Only on a genuinely new assignment - this runs every 3s and would
                    // otherwise re-log the same still-valid target for the whole flight.
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
                    transportMode = TransportMode.Waiting;
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
            if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.TryGetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit) && nearestUnit.TryGetUnit(out var unit))
            {
                vehicleLabel = "Vehicles (contact)";
                targetPosition = nearestUnit.lastKnownPosition;
                range = FastMath.Distance(targetPosition.Value, aircraft.GlobalPosition());
                targetRadius = unit.maxRadius * 2f;
            }
            if (MissionPosition.TryGetClosestObjectivePosition(aircraft, out var result) && FastMath.InRange(aircraft.GlobalPosition(), result.Position, range))
            {
                targetPosition = result.Position;
                vehicleLabel = "Vehicles (objective)";
                targetRadius = 100f;
            }
            if (targetPosition.HasValue)
            {
                transportDestination.validMission = true;
                transportDestination.UpdateLZ(aircraft, targetPosition, targetRadius, ref approachDirection);
                if (!hadValidMission) transportDestination.UpdateTouchdownPoint(100f, aircraft);
                if (!hadValidMission) transportMode = TransportMode.CombatVehicleApproach;
                missionTargetLabel = vehicleLabel;
                UpdateStateDisplayName();
                if (!hadValidMission || !FastMath.InRange(transportDestination.touchdownPoint, previousTouchdown, 500f))
                {
                    ComputeApproachPoints();
                }
                if (!hadValidMission)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} assigned to drop vehicle at {targetPosition.Value} ({stateDisplayName}).");
                }
            }
            else
            {
                transportMode = TransportMode.Waiting;
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
        public override void FixedUpdateState(Pilot pilot)
        {
            if (aircraft == null || aircraft.rb == null) return;
            Countermeasures();
            if (IsNaval(transportMode) && assignedTargetUnit != null && !deployedCargo)
            {
                float distToTouchdown = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
                if (distToTouchdown > 1500f)
                {
                    transportDestination.UpdateLZ(aircraft, assignedTargetUnit);
                }
            }
            aircraft.SetFlightAssist(enabled: true);
            SearchForDropZone();
            GlobalPosition destination = transportDestination.touchdownPoint;
            bool followTerrain = false;
            float altitudeTarget = IsNaval(transportMode) ? 250f : 500f;
            if (transportMode == TransportMode.Waiting)
            {
                altitudeTarget = 250f;
            }
            if (transportDestination.validMission)
            {
                if (!deployedCargo && IsApproach(transportMode)
                    && Time.timeSinceLevelLoad - lastApproachRecalc > 1f)
                {
                    // Keep the run-in line reasonably fresh while still far out (e.g. a moving
                    // ship target), without recomputing every tick (which would reintroduce jitter).
                    ComputeApproachPoints();
                }
                if (IsApproach(transportMode))
                {
                    RunApproachPhase(altitudeTarget, followTerrain);
                }
                else if (IsDrop(transportMode))
                {
                    RunDropPhase(altitudeTarget, followTerrain);
                }
                else if (IsExit(transportMode))
                {
                    RunExitPhase(altitudeTarget);
                    return;
                }
            }
            else
            {
                GlobalPosition aimPos = destination;
                aimPos.y = destination.y + altitudeTarget;
                aircraft.autopilot.AutoAim(aimPos, true, true, false, 1f, 180f, followTerrain, altitudeTarget, Vector3.zero);
            }
            EjectionCheck();
            TargetSearch();
            DefendWithMissiles();
        }
        /// <summary>
        /// Head to Point A. Shaped after vanilla AIPilotLandingState.JoinPattern: the
        /// capture test is a turning-radius proximity plus "the point is now behind us",
        /// and the throttle uses the same distance-scaled speed target. No abort here.
        /// </summary>
        private void RunApproachPhase(float altitudeTarget, bool followTerrain)
        {
            float distToA = FastMath.Distance(aircraft.GlobalPosition(), pointA);
            float approachSpeed = aircraftParameters.cornerSpeed + distToA * 0.02f;
            controlInputs.throttle = Mathf.Clamp(0.5f - (aircraft.speed - approachSpeed) * 0.1f, 0f, aircraftParameters.cruiseThrottle);

            GlobalPosition aimPos = pointA;
            aimPos.y = pointA.y + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, true, false, 0.9f, 135f, followTerrain, altitudeTarget, Vector3.zero);

            float capture = aircraftParameters.turningRadius;
            Vector3 toA = pointA - aircraft.GlobalPosition();
            bool passedA = distToA < capture && Vector3.Dot(aircraft.transform.forward, toA) < 0f;
            if (passedA || distToA < capture * 0.5f)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} reached Point A, heading to Point B.");
                transportMode = ToDrop(transportMode);
                UpdateStateDisplayName();
            }
        }
        /// <summary>
        /// Head to Point B and drop. Steering aims at Point C, which is ~4850m out at
        /// the start of the leg and so stays rock-stable; release happens on crossing B,
        /// 1000m short of the target. The cargo bay is also pre-opened during the run,
        /// so the async ramp/rail sequence after Fire() has both the door already open
        /// and ~4.7s of travel to finish before the aircraft reaches the target.
        /// </summary>
        private void RunDropPhase(float altitudeTarget, bool followTerrain)
        {
            controlInputs.throttle = 1f;

            GlobalPosition aimPos = pointC;
            aimPos.y = pointC.y + altitudeTarget;
            aircraft.autopilot.AutoAim(aimPos, true, true, false, 1.1f, 135f, followTerrain, altitudeTarget, Vector3.zero);

            Vector3 toTarget = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            toTarget.y = 0f;
            float horizDist = toTarget.magnitude;

            // Open the cargo bay during the run-in. Vanilla only calls SpringOpenBayDoors()
            // inside MountedCargo.Fire(), and RailLaunch then blocks on
            // "while (!cargoRamp.IsOpen())" while the ramp physically hinges open - so a
            // 150m fire lead would be spent entirely on the door animation and the crate
            // would leave the rail well past the target. Pinging it early (the same call
            // vanilla makes, just sooner) means the ramp is already open when Fire() lands.
            // Re-pinged every 0.5s on purpose: BayDoor.OpenDoor only tops up openTimer
            // and the door starts closing again the moment it runs out (BayDoor.cs:37-48).
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

            // Release on crossing the plane through B perpendicular to the run axis -
            // exact for a point on the run line and unaffected by lateral offset.
            bool reachedB = Vector3.Dot(aircraft.GlobalPosition() - pointB, approachAxis) >= 0f;
            if (!deployedCargo && reachedB)
            {
                float horizToTarget = FastMath.Distance(aircraft.GlobalPosition(), transportDestination.touchdownPoint);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} at Point B: horiz-to-target={horizToTarget:F0}m alt={(aircraft.GlobalPosition().y - pointC.y):F0}m speed={aircraft.speed:F0}m/s");
                DeployCargo();
            }

            if (deployedCargo)
            {
                transportMode = ToExit(transportMode);
                UpdateStateDisplayName();
            }
            else if (Vector3.Dot(pointC - aircraft.GlobalPosition(), approachAxis) < 0f)
            {
                // Only recovery path: we sailed past C without ever dropping. Positional,
                // so it cannot misfire mid-run the way the old bearing abort did.
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} passed Point C without dropping - rejoining.");
                ComputeApproachPoints();
            }
        }
        /// <summary>
        /// True when the delivery target will not move: a Ship or GroundVehicle set to
        /// hold position, a Building, or a fixed objective point. Vanilla exposes both
        /// directly - Ship.holdPosition is a public field, GroundVehicle has a public
        /// GetHoldPosition() - so no reflection is needed.
        /// </summary>
        private static bool IsHoldingPosition(Unit unit)
        {
            if (unit is Ship ship) return ship.holdPosition;
            if (unit is GroundVehicle gv) return gv.GetHoldPosition();
            return true;
        }
        /// <summary>
        /// Finds the loaded cargo station. Single source of truth for both the bay
        /// pre-opener and DeployCargo: weaponManager.currentWeaponStation is NOT the
        /// cargo station until DeployCargo selects it, so anything reading it during
        /// the run-in resolves the wrong hardpoints and springs the wrong bay doors.
        /// </summary>
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
        /// <summary>
        /// Head to Point C and clear out: hold the current heading with a far aimpoint
        /// (2000m ahead, as the checkpoint build did) until the pass is complete, then
        /// hand control back to the normal combat AI.
        /// </summary>
        private void RunExitPhase(float altitudeTarget)
        {
            Vector3 velDir = aircraft.rb.velocity;
            velDir.y = 0f;
            GlobalPosition aimPos = aircraft.GlobalPosition() + velDir.normalized * 2000f;
            aimPos.y = aircraft.GlobalPosition().y;
            aircraft.autopilot.AutoAim(aimPos, true, true, false, 1f, 180f, false, altitudeTarget, Vector3.zero);
            controlInputs.throttle = 1f;

            if (Time.timeSinceLevelLoad - lastCargoDroppedTime > 3f)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} completed airdrop pass. Switching back to AICombatState.");
                if (pilot.AICombatState == null)
                {
                    pilot.AICombatState = new AIPilotCombatModes(pilot.aircraft);
                }
                pilot.SwitchState(pilot.AICombatState);
            }
        }
        /// <summary>
        /// Releases the cargo. Re-selects the cargo weapon station immediately before
        /// firing (as the checkpoint build did) so the release never depends on whatever
        /// currentWeaponStation happened to be set to elsewhere.
        /// </summary>
        private void DeployCargo()
        {
            if (deployedCargo)
            {
                return;
            }
            if (TryGetCargoStation(out WeaponStation cargoStation))
            {
                aircraft.weaponManager.currentWeaponStation = cargoStation;
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Executing airdrop Fire() for {aircraft.unitName}!");
            pilot.Fire();
            Plugin.TriggerControlNullifier(aircraft, 15.0f);
            pilot.flightInfo.LastCargoDelivery = Time.timeSinceLevelLoad;
            deployedCargo = true;
            lastCargoDroppedTime = Time.timeSinceLevelLoad;
            if (assignedTargetUnit != null)
            {
                ResupplyMissionManager.RegisterDrop(assignedTargetUnit, Time.timeSinceLevelLoad);
                ResupplyMissionManager.UnassignChimera(assignedTargetUnit);
                assignedTargetUnit = null;
            }
            pilot.flightInfo.EnemyContact = true;
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
            if (aircraft != null && aircraft.GetMissileWarningSystem() != null)
            {
                aircraft.GetMissileWarningSystem().onMissileWarning -= FixedWingTransport_OnMissileAlert;
            }
            if (aircraft != null && aircraft.NetworkHQ != null)
            {
                aircraft.NetworkHQ.DeregisterDropZone(transportDestination.touchdownPoint);
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft?.unitName} left AIFixedWingTransportState.");
        }
    }
}   