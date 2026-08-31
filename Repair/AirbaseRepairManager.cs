using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class AirbaseRepairManager
    {
        public static Dictionary<Aircraft, Unit> AssignedRepairs = new Dictionary<Aircraft, Unit>();
        public enum RepairKind
        {
            Local,
            Interbase,
            Heavy
        }
        private static bool KindEnabled(RepairKind kind)
        {
            switch (kind)
            {
                case RepairKind.Local:     return Plugin.LocalAirbaseRepairEnabled != null && Plugin.LocalAirbaseRepairEnabled.Value;
                case RepairKind.Interbase: return Plugin.InterbaseRepairEnabled != null && Plugin.InterbaseRepairEnabled.Value;
                default:                   return Plugin.HeavyRepairEnabled != null && Plugin.HeavyRepairEnabled.Value;
            }
        }
        private static int KindLimit(RepairKind kind)
        {
            switch (kind)
            {
                case RepairKind.Local:     return Plugin.Cfg(Plugin.ActiveLocalRepairLimit, 1);
                case RepairKind.Interbase: return Plugin.Cfg(Plugin.ActiveInterbaseRepairLimit, 1);
                default:                   return Plugin.Cfg(Plugin.ActiveHeavyRepairLimit, 1);
            }
        }
        private static int CountActiveRepairs(FactionHQ hq, RepairKind kind)
        {
            int count = 0;
            foreach (var kvp in AssignedRepairs)
            {
                Aircraft ac = kvp.Key;
                if (ac == null || ac.disabled || ac.NetworkHQ != hq) continue;
                if (_flightKinds.TryGetValue(ac, out RepairKind flown) && flown == kind) count++;
            }
            for (int i = 0; i < _pending.Count; i++)
            {
                RepairState state = _pending[i];
                if (state.IsPending && state.PendingHQ == hq && state.PendingKind == kind) count++;
            }
            return count;
        }
        private static readonly Dictionary<Aircraft, RepairKind> _flightKinds = new Dictionary<Aircraft, RepairKind>();
        private class RepairState
        {
            public float NextAllowedTime;
            public Aircraft DispatchedAircraft;
            public bool IsPending;
            public string PendingAircraftKey;
            public Unit PendingTarget;
            public FactionHQ PendingHQ;
            public RepairKind PendingKind;
            public string LastSkipReason;
        }
        private static readonly ConditionalWeakTable<Airbase, RepairState> _airbaseStates = new ConditionalWeakTable<Airbase, RepairState>();
        private static readonly Dictionary<FactionHQ, RepairState> _outpostStates = new Dictionary<FactionHQ, RepairState>();
        private static readonly List<RepairState> _pending = new List<RepairState>();
        internal static void ResetForNewLevel()
        {
            AssignedRepairs.Clear();
            _flightKinds.Clear();
            _outpostStates.Clear();
            _pending.Clear();
        }
        private static bool DebugOn => Plugin.Dbg;
        public static bool IsValidRepairTarget(Unit unit)
        {
            return unit != null && unit is IRepairable repairable && repairable.NeedsRepair();
        }
        private static void LogSkip(RepairState state, Airbase ab, bool isOutpost, int targetCount, string reason)
        {
            if (state != null)
            {
                if (state.LastSkipReason == reason) return;
                state.LastSkipReason = reason;
            }
            if (!DebugOn) return;
            string where = isOutpost ? "outpost group" : (ab != null ? ab.gameObject.name : "unknown airbase");
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Repair for {where} ({targetCount} target(s)) skipped: {reason}.");
        }
        public static void TryDispatchRepair(FactionHQ hq, Unit unit)
        {
            if (hq == null || !IsValidRepairTarget(unit)) return;
            if (Plugin.ThresholdA == null || Plugin.ThresholdB == null) return;
            if (!ChimeraSpawnQueue.IsServerAuthority()) return;
            if (DebugOn)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] '{unit.unitName}' reported damage; evaluating instant repair dispatch.");
            }
            Airbase ab = unit.GetAirbase();
            ProcessTargetGroup(hq, ab, new List<Unit> { unit }, isOutpost: ab == null);
        }
        public static void Update()
        {
            if (Plugin.ThresholdA == null || Plugin.ThresholdB == null) return;
            if (!ChimeraSpawnQueue.IsServerAuthority()) return;
            var toRemove = new List<Aircraft>();
            foreach (var kvp in AssignedRepairs)
            {
                if (kvp.Key == null || kvp.Key.disabled || !IsValidRepairTarget(kvp.Value))
                {
                    toRemove.Add(kvp.Key);
                }
            }
            foreach (var ac in toRemove)
            {
                AssignedRepairs.Remove(ac);
                if (ac != null) _flightKinds.Remove(ac);
            }
            if (FactionRegistry.HQLookup == null) return;
            foreach (var hq in FactionRegistry.HQLookup.Values)
            {
                if (hq == null || !hq.isActiveAndEnabled || hq.faction == null) continue;
                ProcessHQ(hq);
            }
        }
        private static void ProcessHQ(FactionHQ hq)
        {
            var needsRepair = AccessTools.FieldRefAccess<FactionHQ, List<Unit>>(hq, "unitsNeedingRepair");
            if (needsRepair == null) return;
            var grouped = new Dictionary<Airbase, List<Unit>>();
            var outpostNeeds = new List<Unit>();
            foreach (var unit in needsRepair)
            {
                if (!IsValidRepairTarget(unit)) continue;
                Airbase ab = unit.GetAirbase();
                if (ab != null)
                {
                    if (!grouped.ContainsKey(ab)) grouped[ab] = new List<Unit>();
                    grouped[ab].Add(unit);
                }
                else
                {
                    outpostNeeds.Add(unit);
                }
            }
            foreach (var kvp in grouped)
            {
                ProcessTargetGroup(hq, kvp.Key, kvp.Value, isOutpost: false);
            }
            if (outpostNeeds.Count > 0)
            {
                ProcessTargetGroup(hq, null, outpostNeeds, isOutpost: true);
            }
        }
        private static void ProcessTargetGroup(FactionHQ hq, Airbase ab, List<Unit> targets, bool isOutpost)
        {
            RepairState state;
            if (isOutpost)
            {
                if (!_outpostStates.TryGetValue(hq, out state))
                {
                    state = new RepairState();
                    _outpostStates[hq] = state;
                }
            }
            else
            {
                state = _airbaseStates.GetValue(ab, _ => new RepairState());
            }
            bool isAircraftActive = state.DispatchedAircraft != null && !state.DispatchedAircraft.disabled;
            bool isAircraftDead = state.DispatchedAircraft != null && state.DispatchedAircraft.disabled;
            string aircraftName = (state.DispatchedAircraft != null) ? state.DispatchedAircraft.unitName : "repair flight";
            if (isAircraftActive)
            {
                if (CargoDemand.ItemsAboard(state.DispatchedAircraft) == 0)
                {
                    state.NextAllowedTime = Time.timeSinceLevelLoad + Plugin.AirbaseRepairCooldown.Value;
                    state.DispatchedAircraft = null;
                    LogSkip(state, ab, isOutpost, targets.Count, $"'{aircraftName}' has delivered; cooldown started ({Plugin.AirbaseRepairCooldown.Value:F0}s)");
                    return;
                }
                LogSkip(state, ab, isOutpost, targets.Count, $"'{aircraftName}' is already en route with cargo aboard");
                return;
            }
            else if (isAircraftDead)
            {
                if (!isOutpost)
                {
                    state.NextAllowedTime = Time.timeSinceLevelLoad + Plugin.AirbaseRepairCooldown.Value;
                }
                else
                {
                    state.NextAllowedTime = 0f;
                }
                state.DispatchedAircraft = null;
                LogSkip(state, ab, isOutpost, targets.Count, isOutpost
                    ? $"'{aircraftName}' was lost before delivering; outpost cooldown cleared"
                    : $"'{aircraftName}' was lost before delivering; cooldown started ({Plugin.AirbaseRepairCooldown.Value:F0}s)");
                return;
            }
            else if (state.IsPending)
            {
                LogSkip(state, ab, isOutpost, targets.Count, $"a {state.PendingAircraftKey} is already spawning and awaiting registration");
                return;
            }
            if (Time.timeSinceLevelLoad < state.NextAllowedTime)
            {
                LogSkip(state, ab, isOutpost, targets.Count, $"cooldown active until T={state.NextAllowedTime:F0}s");
                return;
            }
            Unit target = ChooseTarget(ab, targets);
            if (target == null) return;
            bool spawned = false;
            if (GetBestRepairSpawner(hq, target, isOutpost, out Airbase spawnBase, out string bestAircraft, out RepairKind kind, out string rejection))
            {
                float dist = Vector3.Distance(spawnBase.transform.position, target.transform.position);
                int active = CountActiveRepairs(hq, kind);
                int limit = KindLimit(kind);
                if (active >= limit)
                {
                    LogSkip(state, ab, isOutpost, targets.Count, $"{kind} repair limit reached ({active}/{limit} active)");
                    return;
                }
                if (bestAircraft == "UtilityHelo1" || bestAircraft == "QuadVTOL1")
                {
                    spawned = TrySpawnHelicopter(hq, target, bestAircraft, spawnBase, dist, state, kind);
                }
                else if (bestAircraft == "Aryx_CargoPlane1")
                {
                    spawned = TrySpawnChimera(hq, target, spawnBase, dist, state, kind);
                }
                if (spawned)
                {
                    state.LastSkipReason = null;
                    bool isShip = spawnBase.TryGetAttachedUnit(out Unit attachedUnit) && attachedUnit.GetType().Name == "Ship";
                    if (isShip)
                    {
                        var spawnerState = _spawnerStates.GetValue(spawnBase, _ => new SpawnerState());
                        spawnerState.NextAllowedTime = Time.timeSinceLevelLoad + Plugin.AirbaseRepairCooldown.Value;
                    }
                }
                else
                {
                    LogSkip(state, ab, isOutpost, targets.Count, $"{bestAircraft} at {spawnBase.gameObject.name} was refused (limit, spawn interval, or hangar denial)");
                }
            }
            else if (isOutpost)
            {
                LogSkip(state, ab, isOutpost, targets.Count, rejection);
                return;
            }
            else
            {
                LogSkip(state, ab, isOutpost, targets.Count, rejection);
            }
        }
        private static Unit ChooseTarget(Airbase ab, List<Unit> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            if (targets.Count == 1) return targets[0];
            GlobalPosition reference = (ab != null) ? ab.center.GlobalPosition() : targets[0].GlobalPosition();
            float range = Plugin.ThresholdA.Value;
            Unit best = null;
            float bestPriority = float.NegativeInfinity;
            for (int i = 0; i < targets.Count; i++)
            {
                if (!(targets[i] is IRepairable repairable)) continue;
                float priority = repairable.GetRepairPriority(reference, range);
                if (priority > bestPriority)
                {
                    bestPriority = priority;
                    best = targets[i];
                }
            }
            return best ?? targets[0];
        }
        private class SpawnerState
        {
            public float NextAllowedTime;
        }
        private static readonly ConditionalWeakTable<Airbase, SpawnerState> _spawnerStates = new ConditionalWeakTable<Airbase, SpawnerState>();
        private static bool GetBestRepairSpawner(FactionHQ hq, Unit target, bool isOutpost, out Airbase bestBase, out string bestAircraft, out RepairKind bestKind, out string rejection)
        {
            bestBase = null;
            bestAircraft = null;
            bestKind = RepairKind.Interbase;
            rejection = "no allied airbase can spawn a repair aircraft";
            bool anyKindDisabled = false;
            Airbase targetAirbase = isOutpost ? null : target.GetAirbase();
            Airbase nearestRejected = null;
            string nearestRejectedKey = null;
            float nearestRejectedDist = float.MaxValue;
            float minDist = float.MaxValue;
            float thresholdA = Plugin.ThresholdA.Value;
            float thresholdB = Plugin.ThresholdB.Value;
            var defIbis = GetAircraftDefinition("UtilityHelo1");
            var defTarantula = GetAircraftDefinition("QuadVTOL1");
            var defChimera = GetAircraftDefinition("Aryx_CargoPlane1");
            foreach (var ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null || !ab.isActiveAndEnabled) continue;
                if (ab.CurrentHQ != hq && (ab.CurrentHQ == null || ab.CurrentHQ.faction != hq.faction)) continue;
                bool isShip = ab.TryGetAttachedUnit(out Unit attachedUnit) && attachedUnit.GetType().Name == "Ship";
                if (isShip)
                {
                    var spawnerState = _spawnerStates.GetValue(ab, _ => new SpawnerState());
                    if (Time.timeSinceLevelLoad < spawnerState.NextAllowedTime) continue; 
                }
                RepairKind kind = (targetAirbase != null && ab == targetAirbase) ? RepairKind.Local : RepairKind.Interbase;
                if (!KindEnabled(kind)) { anyKindDisabled = true; continue; }
                float d = Vector3.Distance(ab.transform.position, target.transform.position);
                if (defIbis != null && ab.CanSpawnAircraft(defIbis))
                {
                    float limit = isShip ? thresholdB : thresholdA;
                    if (d < limit && d < minDist)
                    {
                        minDist = d;
                        bestBase = ab;
                        bestAircraft = "UtilityHelo1";
                        bestKind = kind;
                    }
                    else if (d >= limit && d < nearestRejectedDist)
                    {
                        nearestRejectedDist = d;
                        nearestRejected = ab;
                        nearestRejectedKey = "UtilityHelo1";
                    }
                }
                if (defTarantula != null && ab.CanSpawnAircraft(defTarantula))
                {
                    float limit = thresholdB;
                    if (d < limit && d < minDist)
                    {
                        minDist = d;
                        bestBase = ab;
                        bestAircraft = "QuadVTOL1";
                        bestKind = kind;
                    }
                    else if (d >= limit && d < nearestRejectedDist)
                    {
                        nearestRejectedDist = d;
                        nearestRejected = ab;
                        nearestRejectedKey = "QuadVTOL1";
                    }
                }
            }
            if (bestBase == null && !isOutpost && defChimera != null && !KindEnabled(RepairKind.Heavy))
            {
                anyKindDisabled = true;
            }
            else if (bestBase == null && !isOutpost && defChimera != null)
            {
                foreach (var ab in FactionRegistry.airbaseLookup.Values)
                {
                    if (ab == null || !ab.isActiveAndEnabled) continue;
                    if (ab.CurrentHQ != hq && (ab.CurrentHQ == null || ab.CurrentHQ.faction != hq.faction)) continue;
                    if (ab.CanSpawnAircraft(defChimera))
                    {
                        float d = Vector3.Distance(ab.transform.position, target.transform.position);
                        if (d < minDist)
                        {
                            minDist = d;
                            bestBase = ab;
                            bestAircraft = "Aryx_CargoPlane1";
                            bestKind = RepairKind.Heavy;
                        }
                    }
                }
            }
            if (bestBase == null && nearestRejected != null)
            {
                rejection = $"nearest {nearestRejectedKey} base '{nearestRejected.gameObject.name}' is {nearestRejectedDist:F0}m away, outside its threshold (A={thresholdA:F0}m, B={thresholdB:F0}m)";
            }
            else if (bestBase == null && anyKindDisabled)
            {
                rejection = "every candidate belongs to a repair kind that is disabled in the config";
            }
            return bestBase != null;
        }
        private static WeaponManager PrefabWeaponManager(AircraftDefinition definition)
        {
            if (definition == null || definition.unitPrefab == null) return null;
            Aircraft prefabAircraft = definition.unitPrefab.GetComponent<Aircraft>();
            return (prefabAircraft != null) ? prefabAircraft.weaponManager : null;
        }
        private static AircraftDefinition GetAircraftDefinition(string jsonKey)
        {
            if (Encyclopedia.i == null || Encyclopedia.i.aircraft == null) return null;
            foreach (var a in Encyclopedia.i.aircraft)
                if (a != null && a.jsonKey == jsonKey) return a;
            return null;
        }
        public static void TryClaimPendingAircraft(FactionHQ hq, Aircraft aircraft)
        {
            if (hq == null || aircraft == null || aircraft.definition == null) return;
            string key = aircraft.definition.jsonKey;
            for (int i = 0; i < _pending.Count; i++)
            {
                RepairState state = _pending[i];
                if (!state.IsPending || state.PendingHQ != hq || state.PendingAircraftKey != key) continue;
                if (!IsValidRepairTarget(state.PendingTarget))
                {
                    ClearPending(state);
                    i--;
                    continue;
                }
                state.DispatchedAircraft = aircraft;
                AssignedRepairs[aircraft] = state.PendingTarget;
                _flightKinds[aircraft] = state.PendingKind;
                string repairWindow = (state.PendingTarget is Building building)
                    ? $", repairable={building.IsRepairable()}"
                    : string.Empty;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Repair {key} '{aircraft.unitName}' bound to target '{state.PendingTarget.unitName}' ({CargoDemand.ItemsAboard(aircraft)} item(s) aboard{repairWindow}).");
                ClearPending(state);
                return;
            }
            if (DebugOn && _pending.Count > 0)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Registered {key} '{aircraft.unitName}' matched no pending repair ({_pending.Count} awaiting).");
            }
        }
        private static void MarkPending(RepairState state, FactionHQ hq, Unit target, string jsonKey, RepairKind kind)
        {
            state.IsPending = true;
            state.PendingAircraftKey = jsonKey;
            state.PendingTarget = target;
            state.PendingHQ = hq;
            state.PendingKind = kind;
            if (!_pending.Contains(state)) _pending.Add(state);
        }
        private static void ClearPending(RepairState state)
        {
            state.IsPending = false;
            state.PendingTarget = null;
            state.PendingHQ = null;
            _pending.Remove(state);
        }
        private static bool TrySpawnHelicopter(FactionHQ hq, Unit target, string jsonKey, Airbase spawnBase, float dist, RepairState state, RepairKind kind)
        {
            const bool isWet = false;
            if (!ResupplyCensus.CanSpawnNow(hq)) return false;
            var spawnDef = GetAircraftDefinition(jsonKey);
            if (spawnDef == null) return false;
            var manager = PrefabWeaponManager(spawnDef);
            Loadout loadout = RepairLoadout.Build(jsonKey, manager, SortieParity.Next(hq, SortieCategory.Repair));
            hq.AddSupplyUnit(spawnDef, 1);
            ResupplyCensus.RegisterDispatch(hq, jsonKey, isWet);
            MarkPending(state, hq, target, jsonKey, kind);
            int livery = spawnDef.aircraftParameters.GetRandomLiveryForFaction(hq.faction);
            var result = spawnBase.TrySpawnAircraft(null, spawnDef, new LiveryKey(livery), loadout, 0.5f);
            if (!result.Allowed)
            {
                hq.AddSupplyUnit(spawnDef, -1);
                ResupplyCensus.CancelDispatch(hq, jsonKey, isWet);
                ClearPending(state);
                return false;
            }
            ResupplyCensus.MarkSpawned(hq);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawned Repair {jsonKey} at {spawnBase.gameObject.name} for {target.unitName}. Dist: {dist:F0}m.");
            return true;
        }
        private static bool TrySpawnChimera(FactionHQ hq, Unit target, Airbase spawnBase, float dist, RepairState state, RepairKind kind)
        {
            var def = ChimeraHelper.GetChimeraDefinition();
            if (def == null) return false;
            var loadout = new Loadout { weapons = new List<WeaponMount>() };
            WeaponMount dozer2x = ChimeraHelper.GetWeaponMount("Aryx_MC260_UGVDozer_2x");
            WeaponMount jammer = ChimeraHelper.GetWeaponMount("JammingPod1");
            var manager = PrefabWeaponManager(def);
            if (manager != null)
            {
                foreach (var hp in manager.hardpointSets)
                {
                    if (hp.name == ChimeraHelper.RearCargoBay || hp.name == ChimeraHelper.FrontCargoBay)
                        loadout.weapons.Add(dozer2x);
                    else if (hp.name == ChimeraHelper.WingPylons)
                        loadout.weapons.Add(jammer);
                    else
                        loadout.weapons.Add(null);
                }
            }
            string loadoutName = "Heavy Dozer Repair";
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Requesting Repair Chimera for '{target.unitName}' (Dist: {dist:F0}m).");
            MarkPending(state, hq, target, "Aryx_CargoPlane1", kind);
            bool queued = ChimeraSpawnQueue.Request(hq, target, def, loadout, loadoutName, false);
            if (!queued) ClearPending(state);
            return queued;
        }
    }
}