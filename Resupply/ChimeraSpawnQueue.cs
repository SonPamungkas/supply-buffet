using System;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    public class ChimeraSpawnRequest
    {
        public FactionHQ HQ;
        public Unit Requester;
        public AircraftDefinition ChimeraDef;
        public Loadout Loadout;
        public string LoadoutName;
        public bool IsWet;
        public float RequestTime;
        public float NextAttempt;
        public bool LoggedWait;
    }
    public static class ChimeraSpawnQueue
    {
        private static readonly Queue<ChimeraSpawnRequest> Pending = new Queue<ChimeraSpawnRequest>();
        private static readonly Dictionary<Hangar, float> Reserved = new Dictionary<Hangar, float>();
        private const float RESERVE_SECONDS = 5f;
        private const float RETRY_SECONDS = 1f;
        private const float REQUEST_TTL = 120f;
        private static readonly List<Hangar> ExpiredScratch = new List<Hangar>();
        public static bool IsServerAuthority()
        {
            NetworkManagerNuclearOption net = NetworkManagerNuclearOption.i;
            return net != null && net.Server != null && net.Server.Active;
        }
        public static bool Request(FactionHQ hq, Unit requester, AircraftDefinition chimeraDef, Loadout loadout, string loadoutName, bool isWet)
        {
            if (hq == null || requester == null || chimeraDef == null) return false;
            if (!IsServerAuthority()) return false;
            if (Contains(requester)) return false;
            float now = Time.timeSinceLevelLoad;
            var req = new ChimeraSpawnRequest
            {
                HQ = hq,
                Requester = requester,
                ChimeraDef = chimeraDef,
                Loadout = loadout,
                LoadoutName = loadoutName,
                IsWet = isWet,
                RequestTime = now,
                NextAttempt = now
            };
            if (TryDispatch(req, now)) return true;
            req.NextAttempt = now + RETRY_SECONDS;
            Pending.Enqueue(req);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Queued {(isWet ? "Wet" : "Dry")} Chimera for '{requester.unitName}' (Loadout: {loadoutName}) - no free hangar yet.");
            return true;
        }
        public static void Drain()
        {
            if (Pending.Count == 0)
            {
                if (Reserved.Count > 0) PruneReservations(Time.timeSinceLevelLoad);
                return;
            }
            if (!IsServerAuthority()) return;
            float now = Time.timeSinceLevelLoad;
            PruneReservations(now);
            int passes = Pending.Count;
            for (int i = 0; i < passes; i++)
            {
                ChimeraSpawnRequest req = Pending.Dequeue();
                if (req == null || req.HQ == null || req.Requester == null || req.Requester.disabled)
                {
                    continue;
                }
                if (now - req.RequestTime > REQUEST_TTL)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Dropping stale Chimera request for '{req.Requester.unitName}' after {REQUEST_TTL:F0}s.");
                    continue;
                }
                if (now < req.NextAttempt)
                {
                    Pending.Enqueue(req);
                    continue;
                }
                if (!TryDispatch(req, now))
                {
                    req.NextAttempt = now + RETRY_SECONDS;
                    Pending.Enqueue(req);
                }
            }
        }
        public static bool Contains(Unit unit)
        {
            if (unit == null) return false;
            foreach (ChimeraSpawnRequest req in Pending)
            {
                if (req != null && req.Requester == unit) return true;
            }
            return false;
        }
        public static int CountFor(FactionHQ hq, bool isWet)
        {
            int count = 0;
            foreach (ChimeraSpawnRequest req in Pending)
            {
                if (req != null && req.HQ == hq && req.IsWet == isWet) count++;
            }
            return count;
        }
        private static bool TryDispatch(ChimeraSpawnRequest req, float now)
        {
            if (Plugin.IsResupplyLimitReached(req.HQ, "Aryx_CargoPlane1", req.IsWet, false))
            {
                if (!req.LoggedWait)
                {
                    req.LoggedWait = true;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Active Chimera limit reached, holding request for '{req.Requester.unitName}'.");
                }
                return false;
            }
            if (!ResupplyCensus.CanSpawnNow(req.HQ))
            {
                if (!req.LoggedWait)
                {
                    req.LoggedWait = true;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawn interval active ({ResupplyCensus.SpawnIntervalRemaining(req.HQ):F0}s left), holding request for '{req.Requester.unitName}'.");
                }
                return false;
            }
            if (FactionRegistry.airbaseLookup == null) return false;
            Airbase spawnBase = null;
            Hangar chosenHangar = null;
            foreach (Airbase ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null || ab.disabled) continue;
                if (ab.CurrentHQ != req.HQ && (ab.CurrentHQ == null || ab.CurrentHQ.faction != req.HQ.faction)) continue;
                if (!ab.CanSpawnAircraft(req.ChimeraDef)) continue;
                foreach (Hangar h in ab.hangars)
                {
                    if (!IsFree(h, req.ChimeraDef, now)) continue;
                    spawnBase = ab;
                    chosenHangar = h;
                    break;
                }
                if (chosenHangar != null) break;
            }
            if (spawnBase == null || chosenHangar == null)
            {
                if (!req.LoggedWait)
                {
                    req.LoggedWait = true;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] No free hangar for '{req.Requester.unitName}', retrying every {RETRY_SECONDS:F0}s.");
                }
                return false;
            }
            ResupplyCensus.RegisterDispatch(req.HQ, req.ChimeraDef.jsonKey, req.IsWet);
            try
            {
                int livery = req.ChimeraDef.aircraftParameters.GetRandomLiveryForFaction(req.HQ.faction);
                req.HQ.AddSupplyUnit(req.ChimeraDef, 1);
                Airbase.TrySpawnResult result = chosenHangar.TrySpawnAircraft(null, req.ChimeraDef, new LiveryKey(livery), req.Loadout, 1f);
                if (!result.Allowed)
                {
                    req.HQ.AddSupplyUnit(req.ChimeraDef, -1);
                    ResupplyCensus.CancelDispatch(req.HQ, req.ChimeraDef.jsonKey, req.IsWet);
                    Plugin.Log.LogWarning($"[SupplyBuffetMod] TrySpawnAircraft denied for {req.ChimeraDef.unitName} at {spawnBase.gameObject.name}:{chosenHangar.name}. Reverted +1 reserve.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                ResupplyCensus.CancelDispatch(req.HQ, req.ChimeraDef.jsonKey, req.IsWet);
                Plugin.Log.LogError($"[SupplyBuffetMod] Chimera spawn threw for '{req.Requester.unitName}': {ex.Message}");
                return false;
            }
            Reserved[chosenHangar] = now + RESERVE_SECONDS;
            ResupplyCensus.MarkSpawned(req.HQ);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawned {req.ChimeraDef.unitName} ({req.LoadoutName}) at {spawnBase.gameObject.name} (Hangar: {chosenHangar.name}) for {req.Requester.unitName}.");
            return true;
        }
        private static bool IsFree(Hangar h, AircraftDefinition def, float now)
        {
            if (h == null || !h.Available || !h.CanSpawnAircraft(def)) return false;
            return !(Reserved.TryGetValue(h, out float until) && now < until);
        }
        private static void PruneReservations(float now)
        {
            ExpiredScratch.Clear();
            foreach (KeyValuePair<Hangar, float> kvp in Reserved)
            {
                if (kvp.Key == null || now >= kvp.Value) ExpiredScratch.Add(kvp.Key);
            }
            for (int i = 0; i < ExpiredScratch.Count; i++) Reserved.Remove(ExpiredScratch[i]);
            ExpiredScratch.Clear();
        }
    }
}