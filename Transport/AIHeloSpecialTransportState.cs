using System;
using UnityEngine;
namespace SupplyBuffetMod
{
    public class AIHeloSpecialTransportState : PilotBaseState
    {
        private const float SLOPE_SATISFIED = 3f;
        private const float SLOPE_ACCEPTABLE = 20f;
        private const float COMMIT_RANGE_SQR = 1000000f;
        private const int MAX_TOUCHDOWN_ATTEMPTS = 12;
        private const float HOVER_HEIGHT = 20f;
        private const float TOUCHDOWN_HEIGHT = -1f;
        private const float DESCENT_RATE = 2f;
        private const float SETTLED_SINK_RATE = 1f;
        private const float LZ_MARGIN = 25f;
        private Unit repairTarget;
        private AircraftParameters aircraftParameters;
        private GlobalPosition landingZone;
        private GlobalPosition touchdownPoint;
        private float slope;
        private int touchdownPointAttempts;
        private bool dropZoneRegistered;
        private Vector3 targetAxis;
        private float lzMargin;
        private bool gearCommandLogged;
        private bool airdrop;
        private bool deployedCargo;
        private float touchedDownTime;
        private float targetHeight;
        private float lastLandingSpotCheck;
        public AIHeloSpecialTransportState(Aircraft aircraft)
        {
            base.aircraft = aircraft;
        }
        public override void EnterState(Pilot pilot)
        {
            base.pilot = pilot;
            aircraft = pilot.aircraft;
            aircraftParameters = aircraft.GetAircraftParameters();
            controlInputs = aircraft.GetInputs();
            aircraft.SetFlightAssistToDefault();
            slope = 90f;
            touchdownPointAttempts = 0;
            touchedDownTime = 0f;
            targetHeight = 20f;
            deployedCargo = false;
            airdrop = false;
            lastLandingSpotCheck = 0f;
            AirbaseRepairManager.AssignedRepairs.TryGetValue(aircraft, out repairTarget);
            lzMargin = LZ_MARGIN;
            gearCommandLogged = false;
            targetAxis = ComputeTargetAxis();
            landingZone = ComputeLandingZone();
            touchdownPoint = landingZone;
            destination = touchdownPoint;
            stateDisplayName = (repairTarget != null)
                ? "Repairing " + repairTarget.unitName
                : "Repair Transport";
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} entering repair transport state for '{(repairTarget != null ? repairTarget.unitName : "none")}'.");
        }
        public override void UpdateState(Pilot pilot) { }
        public override void LeaveState()
        {
            ReleaseDropZone();
        }
        private GlobalPosition TargetPosition()
        {
            return (repairTarget != null) ? repairTarget.GlobalPosition() : aircraft.GlobalPosition();
        }
        private Vector3 ComputeTargetAxis()
        {
            Vector3 axis = (repairTarget != null) ? repairTarget.transform.forward : aircraft.transform.forward;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.01f)
            {
                axis = aircraft.transform.forward;
                axis.y = 0f;
            }
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.forward;
            return axis.normalized;
        }
        private GlobalPosition ComputeLandingZone()
        {
            float radius = (repairTarget != null) ? repairTarget.maxRadius : 0f;
            return TargetPosition() + targetAxis * (radius + lzMargin);
        }
        private bool AssignmentStillValid()
        {
            if (!AirbaseRepairManager.IsValidRepairTarget(repairTarget)) return false;
            return AirbaseRepairManager.AssignedRepairs.TryGetValue(aircraft, out Unit current) && current == repairTarget;
        }
        private void SearchForLandingSpot()
        {
            if (Time.timeSinceLevelLoad - lastLandingSpotCheck < 3f) return;
            lastLandingSpotCheck = Time.timeSinceLevelLoad;
            SelectCargoStation();
            pilot.flightInfo.EnemyContact = true;
            if (!FastMath.InRange(aircraft.GlobalPosition(), touchdownPoint, 3000f))
            {
                targetAxis = ComputeTargetAxis();
                GlobalPosition candidate = ComputeLandingZone();
                if (!FastMath.InRange(candidate, landingZone, 1000f))
                {
                    landingZone = candidate;
                    slope = 90f;
                    touchdownPointAttempts = 0;
                }
            }
            UpdateTouchdownPoint();
        }
        private void UpdateTouchdownPoint()
        {
            if (slope < SLOPE_SATISFIED) return;
            if (slope < SLOPE_ACCEPTABLE &&
                FastMath.SquareDistance(aircraft.GlobalPosition(), touchdownPoint) < COMMIT_RANGE_SQR)
            {
                return;
            }
            if (touchdownPointAttempts >= MAX_TOUCHDOWN_ATTEMPTS)
            {
                if (!airdrop)
                {
                    airdrop = true;
                    touchdownPoint = TargetPosition();
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} found no level touchdown point after {touchdownPointAttempts} attempts; delivering from a hover instead.");
                }
                return;
            }
            Vector3 lateral = Vector3.Cross(targetAxis, Vector3.up).normalized;
            float jitter = UnityEngine.Random.Range(-lzMargin, lzMargin);
            GlobalPosition sample = ComputeLandingZone() + lateral * jitter;
            touchdownPointAttempts++;
            if (!Physics.Linecast(sample.ToLocalPosition() + Vector3.up * 4000f,
                                  sample.ToLocalPosition() - Vector3.up * 4000f,
                                  out RaycastHit hit, PhysicsLayers.StaticsMask))
            {
                WidenLandingZone();
                return;
            }
            float sampledSlope = Vector3.Angle(hit.normal, Vector3.up);
            if (sampledSlope >= SLOPE_ACCEPTABLE || hit.point.y <= Datum.LocalSeaY || sampledSlope >= slope)
            {
                WidenLandingZone();
                return;
            }
            ReleaseDropZone();
            GlobalPosition found = hit.point.ToGlobalPosition();
            if (!aircraft.NetworkHQ.IsDropZoneClear(found))
            {
                WidenLandingZone();
                return;
            }
            slope = sampledSlope;
            touchdownPoint = found;
            aircraft.NetworkHQ.RegisterDropZone(touchdownPoint);
            dropZoneRegistered = true;
        }
        private void WidenLandingZone()
        {
            lzMargin += LZ_MARGIN;
        }
        private void ReleaseDropZone()
        {
            if (!dropZoneRegistered || aircraft == null || aircraft.NetworkHQ == null) return;
            aircraft.NetworkHQ.DeregisterDropZone(touchdownPoint);
            dropZoneRegistered = false;
        }
        public override void FixedUpdateState(Pilot pilot)
        {
            try
            {
                FixedUpdateStateCore(pilot);
            }
            catch (Exception ex)
            {
                TransportFaultGuard.Report(aircraft, "AIHeloSpecialTransportState", ex);
                if (pilot != null && pilot.AIHeloCombatState != null)
                {
                    pilot.SwitchState(pilot.AIHeloCombatState);
                }
            }
        }
        private void FixedUpdateStateCore(Pilot pilot)
        {
            if (!AssignmentStillValid())
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} lost its repair assignment; returning to vanilla helo AI.");
                pilot.SwitchState(pilot.AIHeloCombatState);
                return;
            }
            Vector3 toDestination = destination - aircraft.GlobalPosition();
            toDestination.y = 0f;
            float rangeToGo = toDestination.magnitude;
            float minAlt = aircraftParameters.minimumRadarAlt;
            if (rangeToGo < 1500f)
            {
                minAlt *= 0.3f;
                if (rangeToGo < 200f) minAlt = Mathf.Lerp(0f, minAlt, (rangeToGo - 10f) / 200f);
            }
            UpdateGear(!airdrop && rangeToGo < 1000f);
            aircraft.SetFlightAssist(enabled: true);
            SearchForLandingSpot();
            destination = touchdownPoint;
            bool followTerrain = rangeToGo > 200f;
            if (airdrop)
            {
                minAlt = 200f;
                aircraft.autopilot.AutoAim(aircraft.GlobalPosition() + toDestination.normalized * 10000f,
                                           minAlt, Vector3.zero, Vector3.zero, followTerrain);
                if (FastMath.InRange(destination, aircraft.GlobalPosition(), 1000f))
                {
                    Vector3 toDrop = FastMath.Direction(aircraft.GlobalPosition(), destination);
                    toDrop.y = 0f;
                    float lead = aircraft.speed * 1.5f + 2.5f * aircraft.speed;
                    if (toDrop.sqrMagnitude < lead * lead)
                    {
                        DeployCargo();
                        pilot.SwitchState(pilot.AIHeloTakeoffState);
                        return;
                    }
                }
            }
            else if (rangeToGo < 300f)
            {
                if (!aircraft.IsAutoHoverEnabled())
                {
                    aircraft.GetControlsFilter().SetAutoHover(enabled: true);
                    aircraft.SetFlightAssist(enabled: false);
                }
                Vector3 aimDirection = (rangeToGo > 50f)
                    ? (destination - aircraft.GlobalPosition())
                    : aircraft.cockpit.xform.forward;
                targetHeight = (rangeToGo > 20f)
                    ? HOVER_HEIGHT
                    : Mathf.Max(TOUCHDOWN_HEIGHT, targetHeight - DESCENT_RATE * Time.fixedDeltaTime);
                aircraft.autopilot.Hover(destination, targetHeight, aimDirection);
            }
            else
            {
                aircraft.autopilot.AutoAim(destination, minAlt, Vector3.zero, Vector3.zero, followTerrain);
            }
            float sinkRate = (aircraft.rb != null) ? Mathf.Abs(aircraft.rb.velocity.y) : 0f;
            if (aircraft.radarAlt < 2f && aircraft.speed < 10f && sinkRate < SETTLED_SINK_RATE)
            {
                controlInputs.brake = 1f;
                controlInputs.throttle = 0f;
                controlInputs.pitch = 0f;
                controlInputs.yaw = 0f;
                controlInputs.roll = 0f;
                if (touchedDownTime == 0f && Plugin.Dbg)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} touched down: radarAlt={aircraft.radarAlt:F1}m, sink={sinkRate:F1}m/s, speed={aircraft.speed:F1}m/s.");
                }
                pilot.flightInfo.EnemyContact = true;
                touchedDownTime += Time.deltaTime;
                DeployCargo();
                if (touchedDownTime > 7f)
                {
                    pilot.SwitchState(pilot.AIHeloTakeoffState);
                }
            }
        }
        private void UpdateGear(bool wantGear)
        {
            TransportGear.Apply(aircraft, wantGear);
            if (wantGear && !gearCommandLogged)
            {
                gearCommandLogged = true;
                if (Plugin.Dbg)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} lowering gear for the repair landing.");
                }
            }
        }
        private void SelectCargoStation()
        {
            if (aircraft.weaponStations == null) return;
            foreach (WeaponStation station in aircraft.weaponStations)
            {
                if (station != null && station.WeaponInfo != null && station.WeaponInfo.cargo && station.Ammo > 0)
                {
                    aircraft.weaponManager.currentWeaponStation = station;
                    return;
                }
            }
        }
        private void DeployCargo()
        {
            if (deployedCargo) return;
            SelectCargoStation();
            pilot.Fire();
            pilot.flightInfo.LastCargoDelivery = Time.timeSinceLevelLoad;
            deployedCargo = true;
            pilot.flightInfo.EnemyContact = true;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} deployed repair cargo for '{repairTarget.unitName}'.");
        }
    }
}