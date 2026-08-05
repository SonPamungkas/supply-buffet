// ============================================================================
// FILE: ChimeraSpawnQueue.cs
// PURPOSE: Owns every Chimera (Aryx_CargoPlane1) spawn request: the pending
//          queue, the per-hangar reservation ledger, and the dispatch itself.
//
// TRIGGERS:
//   - Request(): called by ResupplySpawner the moment a Chimera mission is decided.
//   - Drain():   called every frame by Plugin.Update.
//
// WHY RESERVATIONS INSTEAD OF A TIMER:
//   The old design gated the whole queue behind a 2s timer and stopped after the
//   first success, so N requests took N*2s and a request arriving just after a
//   tick waited the full 2s before anything looked at it. That timer only existed
//   to stop two spawns landing in one hangar - but being global, it also
//   serialised spawns into different, free hangars, which was pure latency.
//   Reserving the individual hangar makes the double-spawn structurally
//   impossible, so the timer is gone: Request() dispatches on the spot, and
//   Drain() may spawn into several hangars in the same frame.
//
// SEARCH ORDER NOTE: the airbase loop MUST continue when a base has no free
//   hangar. An earlier build broke out on the first base that merely passed
//   CanSpawnAircraft, so one busy hangar_med stranded the request in the queue
//   forever while IsUnitAssignedOrQueued kept its target marked "assigned".
//
// SERVER AUTHORITY: Hangar.TrySpawnAircraft and FactionHQ.AddSupplyUnit are both
//   [Server] and THROW when the server is inactive, so every entry point rejects
//   on a multiplayer client before touching either.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.Networking;
using NuclearOption.SavedMission;

namespace SupplyBuffetMod
{
    /// <summary>
    /// A queued request to spawn a Chimera once a medium hangar is free.
    /// </summary>
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

        // Hangar -> time the reservation lapses. Longer than a frame so vanilla's
        // own Available flag has taken over, shorter than a door cycle so a real
        // hangar is never held back.
        private static readonly Dictionary<Hangar, float> Reserved = new Dictionary<Hangar, float>();

        private const float RESERVE_SECONDS = 5f;
        private const float RETRY_SECONDS = 1f;
        private const float REQUEST_TTL = 120f;

        // Reused across prune passes so the maintenance loop allocates nothing.
        private static readonly List<Hangar> ExpiredScratch = new List<Hangar>();

        /// <summary>
        /// True only on the authoritative host. Spawning and reserve accounting both
        /// call [Server] methods that throw outright on a client.
        /// </summary>
        public static bool IsServerAuthority()
        {
            NetworkManagerNuclearOption net = NetworkManagerNuclearOption.i;
            return net != null && net.Server != null && net.Server.Active;
        }

        /// <summary>
        /// Files a Chimera spawn request and tries to satisfy it immediately.
        /// Only queues when no hangar is free right now, so the common case never
        /// waits for an Update tick.
        /// </summary>
        public static void Request(FactionHQ hq, Unit requester, AircraftDefinition chimeraDef, Loadout loadout, string loadoutName, bool isWet)
        {
            if (hq == null || requester == null || chimeraDef == null) return;
            if (!IsServerAuthority()) return;
            if (Contains(requester)) return;

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

            if (TryDispatch(req, now)) return;

            req.NextAttempt = now + RETRY_SECONDS;
            Pending.Enqueue(req);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Queued {(isWet ? "Wet" : "Dry")} Chimera for '{requester.unitName}' (Loadout: {loadoutName}) - no free hangar yet.");
        }

        /// <summary>
        /// One pass over the queue. Every request gets a turn: reservations make
        /// several spawns in the same frame safe, which is what removes the old
        /// one-per-2s serialisation.
        /// </summary>
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

        /// <summary>True while a unit already has a Chimera request waiting.</summary>
        public static bool Contains(Unit unit)
        {
            if (unit == null) return false;
            foreach (ChimeraSpawnRequest req in Pending)
            {
                if (req != null && req.Requester == unit) return true;
            }
            return false;
        }

        /// <summary>Pending Chimera requests for an HQ on the given mission type.</summary>
        public static int CountFor(FactionHQ hq, bool isWet)
        {
            int count = 0;
            foreach (ChimeraSpawnRequest req in Pending)
            {
                if (req != null && req.HQ == hq && req.IsWet == isWet) count++;
            }
            return count;
        }

        /// <summary>Clears all pending requests and reservations (mission reset).</summary>
        public static void Clear()
        {
            Pending.Clear();
            Reserved.Clear();
        }

        /// <summary>
        /// Finds a free hangar anywhere in the HQ's faction and spawns into it.
        /// Returns false when the request should stay queued and be retried.
        /// </summary>
        private static bool TryDispatch(ChimeraSpawnRequest req, float now)
        {
            // Refusal logs fire once per request, not once per 1s retry.
            if (Plugin.IsResupplyLimitReached(req.HQ, "Aryx_CargoPlane1", req.IsWet, false))
            {
                if (!req.LoggedWait)
                {
                    req.LoggedWait = true;
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Active Chimera limit reached, holding request for '{req.Requester.unitName}'.");
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
                // Deliberately keep scanning the remaining airbases when this one
                // had nothing free - see the search order note in the file header.
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

            try
            {
                int livery = req.ChimeraDef.aircraftParameters.GetRandomLiveryForFaction(req.HQ.faction);
                req.HQ.AddSupplyUnit(req.ChimeraDef, 1);
                Airbase.TrySpawnResult result = chosenHangar.TrySpawnAircraft(null, req.ChimeraDef, new LiveryKey(livery), req.Loadout, 1f);

                if (!result.Allowed)
                {
                    req.HQ.AddSupplyUnit(req.ChimeraDef, -1);
                    Plugin.Log.LogWarning($"[SupplyBuffetMod] TrySpawnAircraft denied for {req.ChimeraDef.unitName} at {spawnBase.gameObject.name}:{chosenHangar.name}. Reverted +1 reserve.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Chimera spawn threw for '{req.Requester.unitName}': {ex.Message}");
                return false;
            }

            Reserved[chosenHangar] = now + RESERVE_SECONDS;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawned {req.ChimeraDef.unitName} ({req.LoadoutName}) at {spawnBase.gameObject.name} (Hangar: {chosenHangar.name}) for {req.Requester.unitName}.");
            return true;
        }

        /// <summary>
        /// A hangar is usable when vanilla says it is available and we have not
        /// just dispatched into it. The reservation is what makes double-spawn
        /// impossible without throttling the whole queue.
        /// </summary>
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
