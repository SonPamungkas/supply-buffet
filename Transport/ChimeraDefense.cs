using System.Collections.Generic;
using UnityEngine;
namespace SupplyBuffetMod
{
    public class ChimeraDefense
    {
        private readonly Aircraft aircraft;
        private readonly List<JammingPod> pods = new List<JammingPod>();
        private WeaponStation jammerStation;
        private bool jammerResolved;
        private Unit jammerTarget;
        private float nextJammerScan;
        private const float JAMMER_SCAN_INTERVAL = 0.25f;
        private float nextBurstAt;
        private float burstUntil;
        private float holdUntil;
        private int burstsRemaining;
        private float nextDropBurstAt;
        public ChimeraDefense(Aircraft aircraft)
        {
            this.aircraft = aircraft;
        }
        public void Update()
        {
            if (aircraft == null || aircraft.disabled) return;
            UpdateDropBurst();
            UpdateCountermeasures();
            UpdateJamming();
        }
        public void Stop()
        {
            if (aircraft == null || aircraft.disabled || aircraft.countermeasureManager == null) return;
            if (aircraft.countermeasureTrigger)
            {
                aircraft.Countermeasures(active: false, aircraft.countermeasureManager.activeIndex);
            }
            for (int i = 0; i < pods.Count; i++)
            {
                if (pods[i] != null) pods[i].SetTarget(null);
            }
        }
        private void UpdateCountermeasures()
        {
            if (aircraft.countermeasureManager == null) return;
            float now = Time.timeSinceLevelLoad;
            if (now < holdUntil) return;
            Missile nearest = NearestKnownMissile();
            if (nearest != null && now >= nextBurstAt)
            {
                string countermeasure = aircraft.countermeasureManager.ChooseCountermeasure(nearest);
                nextBurstAt = now + 4f;
                if (!string.IsNullOrEmpty(countermeasure))
                {
                    burstUntil = now + 2f;
                }
            }
            bool shouldFire = nearest != null && now < burstUntil;
            if (shouldFire != aircraft.countermeasureTrigger)
            {
                aircraft.Countermeasures(shouldFire, aircraft.countermeasureManager.activeIndex);
            }
        }
        public void TriggerDropBurst()
        {
            if (burstsRemaining > 0) return;
            burstsRemaining = Plugin.FlareBurstCount != null ? Plugin.FlareBurstCount.Value : 0;
            nextDropBurstAt = Time.timeSinceLevelLoad;
        }
        private void UpdateDropBurst()
        {
            if (burstsRemaining <= 0) return;
            float now = Time.timeSinceLevelLoad;
            if (now < nextDropBurstAt) return;
            if (aircraft.countermeasureManager == null
                || aircraft.countermeasureManager.GetFlareAmmoProportion() <= 0f)
            {
                burstsRemaining = 0;
                return;
            }
            aircraft.countermeasureManager.PopFlares();
            holdUntil = now + 0.2f;
            burstsRemaining--;
            nextDropBurstAt = now + (Plugin.FlareBurstInterval != null ? Plugin.FlareBurstInterval.Value : 0.5f);
        }
        private bool ResolveJammer()
        {
            if (jammerResolved) return pods.Count > 0;
            jammerResolved = true;
            if (aircraft.weaponStations == null) return false;
            foreach (WeaponStation ws in aircraft.weaponStations)
            {
                if (ws == null || ws.WeaponInfo == null || !ws.WeaponInfo.jammer || ws.Weapons == null) continue;
                pods.Clear();
                bool allPods = true;
                foreach (Weapon w in ws.Weapons)
                {
                    if (w == null) continue;
                    if (w is JammingPod pod) pods.Add(pod);
                    else { allPods = false; break; }
                }
                if (allPods && pods.Count > 0)
                {
                    jammerStation = ws;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] {aircraft.unitName} resolved {pods.Count} jamming pod(s).");
                    return true;
                }
                pods.Clear();
            }
            return false;
        }
        private void UpdateJamming()
        {
            if (Plugin.JammerEnabled == null || !Plugin.JammerEnabled.Value) return;
            if (!ResolveJammer()) return;
            float now = Time.timeSinceLevelLoad;
            float range = Plugin.JammerRange != null ? Plugin.JammerRange.Value : 17000f;
            if (now >= nextJammerScan)
            {
                nextJammerScan = now + JAMMER_SCAN_INTERVAL;
                Unit found = NearestMissileTargetingUs();
                if (found == null) found = NearestRadarEmittingHostile(range);
                jammerTarget = found;
            }
            if (jammerTarget == null || jammerTarget.disabled
                || !FastMath.InRange(jammerTarget.GlobalPosition(), aircraft.GlobalPosition(), range))
            {
                for (int i = 0; i < pods.Count; i++)
                {
                    if (pods[i] != null) pods[i].SetTarget(null);
                }
                return;
            }
            Vector3 inheritedVelocity = (aircraft.rb != null) ? aircraft.rb.velocity : Vector3.zero;
            GlobalPosition aimpoint = jammerTarget.GlobalPosition();
            for (int i = 0; i < pods.Count; i++)
            {
                JammingPod pod = pods[i];
                if (pod == null) continue;
                pod.SetTarget(jammerTarget);
                pod.Fire(aircraft, jammerTarget, inheritedVelocity, jammerStation, aimpoint);
            }
        }
        private Missile NearestKnownMissile()
        {
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> missiles = (warning != null) ? warning.knownMissiles : null;
            if (missiles == null) return null;
            Missile nearest = null;
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < missiles.Count; i++)
            {
                Missile m = missiles[i];
                if (m == null || m.disabled) continue;
                float sqr = (m.transform.position - aircraft.transform.position).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearest = m;
                nearestSqr = sqr;
            }
            return nearest;
        }
        private Unit NearestMissileTargetingUs()
        {
            MissileWarning warning = aircraft.GetMissileWarningSystem();
            List<Missile> missiles = (warning != null) ? warning.knownMissiles : null;
            if (missiles == null) return null;
            Missile nearest = null;
            float nearestSqr = float.MaxValue;
            for (int i = 0; i < missiles.Count; i++)
            {
                Missile m = missiles[i];
                if (m == null || m.disabled) continue;
                if (!m.targetID.Equals(aircraft.persistentID)) continue;
                float sqr = (m.transform.position - aircraft.transform.position).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearest = m;
                nearestSqr = sqr;
            }
            return nearest;
        }
        private Unit NearestRadarEmittingHostile(float range)
        {
            FactionHQ hq = aircraft.NetworkHQ;
            if (hq == null || UnitRegistry.allAircraft == null) return null;
            Unit nearest = null;
            float nearestSqr = range * range;
            foreach (Aircraft candidate in UnitRegistry.allAircraft)
            {
                if (candidate == null || candidate.disabled || candidate == aircraft) continue;
                if (candidate.NetworkHQ == null || candidate.NetworkHQ == hq) continue;
                if (candidate.NetworkHQ.faction == hq.faction) continue;
                if (!candidate.HasRadarEmission()) continue;
                if (!hq.IsTargetBeingTracked(candidate)) continue;
                float sqr = (candidate.transform.position - aircraft.transform.position).sqrMagnitude;
                if (sqr >= nearestSqr) continue;
                nearest = candidate;
                nearestSqr = sqr;
            }
            return nearest;
        }
    }
}