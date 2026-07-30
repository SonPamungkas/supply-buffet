using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
namespace SupplyBuffetMod
{
    public class AIFixedWingTransportState : PilotBaseState
    {
        public enum TransportMode
        {
            CombatVehicle,
            LandSupply,
            NavalSupply,
            Radar,
            Waiting
        }
        private struct TransportDestination
        {
            public bool validMission;
            public bool dropConditionsMet;
            public GlobalPosition touchdownPoint;
            public GlobalPosition enemyPosition;
            public GlobalPosition LZ;
            public TrackingInfo nearestEnemy;
            public float slope;
            public int touchdownPointAttempts;
            public TransportDestination(GlobalPosition landingPosition, GlobalPosition enemyPos, float levelAmount)
            {
                validMission = false;
                dropConditionsMet = false;
                touchdownPoint = landingPosition;
                enemyPosition = enemyPos;
                LZ = enemyPos;
                nearestEnemy = null;
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
                if (!FastMath.InRange(globalPosition, LZ, 1000f))
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod][AIFixedWingTransportState] UpdateLZ: Generating new LZ at distance {(globalPosition - targetPosition.Value).magnitude}m");
                    LZ = globalPosition;
                    slope = 90f;
                    touchdownPointAttempts = 0;
                }
            }
            public void UpdateLZ(Aircraft aircraft, Unit unitToRearm)
            {
                Vector3 forwardDir = unitToRearm.transform.forward;
                if (unitToRearm.rb != null && unitToRearm.rb.velocity.sqrMagnitude > 1f)
                {
                    forwardDir = unitToRearm.rb.velocity.normalized;
                }
                if (unitToRearm is Ship ship)
                {
                    touchdownPoint = ship.GlobalPosition() + forwardDir * 2000f;
                    slope = 0f;
                }
                else
                {
                    touchdownPoint = unitToRearm.GlobalPosition() + forwardDir * 2500f;
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
        private float lastAirbaseSearch;
        private float timeWithoutMission;
        private float lastCargoDroppedTime = -100f;
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
            approachDirection = aircraft.transform.forward;
            timeWithoutMission = 0f;
            nearestAirbase = aircraft.NetworkHQ?.GetNearestAirbase(aircraft.transform.position);
            aircraft.SetFlightAssistToDefault();
            controlInputs = aircraft.GetInputs();
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} entered AIFixedWingTransportState.");
            if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.GetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit))
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
            if (rearmShip || rearmGround)
            {
                if (aircraft.NetworkHQ != null && ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(aircraft.NetworkHQ.RearmMissionController, rearmShip, rearmGround, aircraft, out var lowestAmmoUnit))
                {
                    assignedTargetUnit = lowestAmmoUnit;
                    ResupplyMissionManager.AssignChimera(lowestAmmoUnit, aircraft);
                    transportDestination.validMission = true;
                    transportDestination.UpdateLZ(aircraft, lowestAmmoUnit);
                    if (rearmShip)
                    {
                        transportMode = TransportMode.NavalSupply;
                        stateDisplayName = "Delivering Naval Supplies";
                    }
                    else
                    {
                        transportMode = TransportMode.LandSupply;
                        transportDestination.UpdateTouchdownPoint(100f, aircraft);
                        stateDisplayName = "Delivering Supplies";
                    }
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} assigned to resupply {lowestAmmoUnit.unitName} ({stateDisplayName}). LZ: {transportDestination.touchdownPoint}");
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
                    stateDisplayName = "Awaiting Cargo Mission";
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} awaiting cargo mission.");
                }
                return;
            }
            GlobalPosition? targetPosition = null;
            float range = float.MaxValue;
            float targetRadius = 0f;
            if (aircraft.NetworkHQ != null && aircraft.NetworkHQ.GetNearestGroundEnemy(aircraft.GlobalPosition(), out var nearestUnit) && nearestUnit.TryGetUnit(out var unit))
            {
                stateDisplayName = "Transporting Vehicles (contact)";
                targetPosition = nearestUnit.lastKnownPosition;
                range = FastMath.Distance(targetPosition.Value, aircraft.GlobalPosition());
                targetRadius = unit.maxRadius * 2f;
            }
            if (MissionPosition.TryGetClosestObjectivePosition(aircraft, out var result) && FastMath.InRange(aircraft.GlobalPosition(), result.Position, range))
            {
                targetPosition = result.Position;
                stateDisplayName = "Transporting Vehicles (objective)";
                targetRadius = 100f;
            }
            if (targetPosition.HasValue)
            {
                transportDestination.validMission = true;
                transportDestination.UpdateLZ(aircraft, targetPosition, targetRadius, ref approachDirection);
                transportDestination.UpdateTouchdownPoint(1000f, aircraft);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} assigned to drop vehicle at {targetPosition.Value} ({stateDisplayName}).");
            }
            else
            {
                transportMode = TransportMode.Waiting;
                transportDestination.validMission = false;
                OrbitAirbase();
                stateDisplayName = "Awaiting Cargo Mission";
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} no valid drop zone found, awaiting mission.");
            }
        }
        private Airbase GetNearestAirbase()
        {
            if (Time.timeSinceLevelLoad - lastAirbaseSearch > 3f)
            {
                lastAirbaseSearch = Time.timeSinceLevelLoad;
                if (aircraft.NetworkHQ != null)
                {
                    nearestAirbase = aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position);
                }
            }
            return nearestAirbase;
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
            Vector3 vector = transportDestination.touchdownPoint - aircraft.GlobalPosition();
            vector.y = 0f;
            float magnitude = vector.magnitude;
            aircraft.SetFlightAssist(enabled: true);
            SearchForDropZone();
            GlobalPosition destination = transportDestination.touchdownPoint;
            bool followTerrain = false; 
            float altitudeTarget = (transportMode == TransportMode.NavalSupply) ? 250f : 500f;
            if (transportMode == TransportMode.Waiting)
            {
                altitudeTarget = 500f;
            }
            if (Time.timeSinceLevelLoad - lastCargoDroppedTime < 4f)
            {
                Vector3 velDir = aircraft.rb.velocity;
                velDir.y = 0f;
                GlobalPosition clearAimPos = aircraft.GlobalPosition() + velDir.normalized * 2000f;
                clearAimPos.y = aircraft.GlobalPosition().y;
                aircraft.autopilot.AutoAim(clearAimPos, true, true, false, 1f, 180f, false, altitudeTarget, Vector3.zero);
                if (Time.timeSinceLevelLoad - lastCargoDroppedTime > 3f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} completed bombing airdrop pass. Switching back to AICombatState.");
                    if (pilot.AICombatState == null)
                    {
                        pilot.AICombatState = new AIPilotCombatModes(pilot.aircraft);
                    }
                    pilot.SwitchState(pilot.AICombatState);
                }
                return;
            }
            if (transportDestination.validMission)
            {
                float initialHeight = aircraft.transform.position.y - destination.y;
                float fallTime = Kinematics.FallTime(initialHeight, aircraft.rb.velocity.y);
                float horizontalSpeed = Mathf.Max(Vector3.Dot(aircraft.rb.velocity, vector.normalized), 1f);
                float timeToTarget = magnitude / horizontalSpeed;
                float timeDiff = timeToTarget - fallTime;
                float dropLeadTime = 7.5f;
                if (!deployedCargo && timeDiff <= dropLeadTime && timeDiff > -1.5f && Vector3.Angle(vector, new Vector3(aircraft.rb.velocity.x, 0f, aircraft.rb.velocity.z)) < 25f)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} at release point (timeDiff={timeDiff:F2}s, lead={dropLeadTime}s, alt={initialHeight:F1}m, speed={aircraft.speed:F1}m/s). Deploying cargo!");
                    DeployCargo();
                    lastCargoDroppedTime = Time.timeSinceLevelLoad;
                }
                GlobalPosition aimPos = destination;
                aimPos.y = destination.y + 500f;
                if (timeDiff < 15f && initialHeight > 200f)
                {
                    aimPos = aircraft.GlobalPosition() + vector.normalized * 2000f;
                    aimPos.y = aircraft.GlobalPosition().y;
                }
                aircraft.autopilot.AutoAim(aimPos, true, true, false, 1f, 180f, followTerrain, altitudeTarget, Vector3.zero);
            }
            else
            {
                GlobalPosition aimPos = destination;
                aimPos.y = destination.y + 500f;
                aircraft.autopilot.AutoAim(aimPos, true, true, false, 1f, 180f, followTerrain, altitudeTarget, Vector3.zero);
            }
            EjectionCheck();
            TargetSearch();
            DefendWithMissiles();
        }
        private void DeployCargo()
        {
            if (deployedCargo)
            {
                return;
            }
            if (aircraft.weaponStations != null)
            {
                foreach (WeaponStation weaponStation in aircraft.weaponStations)
                {
                    if (weaponStation != null && weaponStation.WeaponInfo != null && weaponStation.WeaponInfo.cargo && weaponStation.Ammo > 0)
                    {
                        aircraft.weaponManager.currentWeaponStation = weaponStation;
                        break;
                    }
                }
            }
            if (!deployedCargo)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Executing airdrop Fire() for {aircraft.unitName}!");
                pilot.Fire();
                pilot.flightInfo.LastCargoDelivery = Time.timeSinceLevelLoad;
                deployedCargo = true;
                if (assignedTargetUnit != null)
                {
                    ResupplyMissionManager.RegisterDrop(assignedTargetUnit, Time.timeSinceLevelLoad);
                    ResupplyMissionManager.UnassignChimera(assignedTargetUnit);
                    assignedTargetUnit = null;
                }
                pilot.flightInfo.EnemyContact = true;
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