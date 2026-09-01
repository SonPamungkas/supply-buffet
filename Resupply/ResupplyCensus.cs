using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class ResupplyCensus
    {
        private class InFlightSpawn
        {
            public FactionHQ HQ;
            public string JsonKey;
            public bool IsWet;
            public float Expiry;
            public Airbase Base;
        }
        private const float MATERIALISE_TTL = 30f;
        private static readonly List<InFlightSpawn> Dispatched = new List<InFlightSpawn>();
        private static readonly ConditionalWeakTable<Aircraft, StrongBox<bool>> WetTag =
            new ConditionalWeakTable<Aircraft, StrongBox<bool>>();
        private static readonly ConditionalWeakTable<Aircraft, StrongBox<Airbase>> HomeBase =
            new ConditionalWeakTable<Aircraft, StrongBox<Airbase>>();
        private static readonly Dictionary<Faction, float> LastSpawnPerFaction =
            new Dictionary<Faction, float>();
        private static float _lastObservedTime = float.NegativeInfinity;
        private static void DetectLevelReset(float now)
        {
            if (now < _lastObservedTime) ResetForNewLevel();
            _lastObservedTime = now;
        }
        internal static void ResetForNewLevel()
        {
            Dispatched.Clear();
            LastSpawnPerFaction.Clear();
        }
        public static bool CanSpawnNow(FactionHQ hq)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || hq.faction == null) return true;
            float interval = Plugin.Cfg(Plugin.SpawnInterval, 60f);
            if (interval <= 0f) return true;
            if (!LastSpawnPerFaction.TryGetValue(hq.faction, out float last)) return true;
            if (Time.timeSinceLevelLoad - last >= interval) return true;
            return Plugin.NoResupplyTransportAirborne(hq);
        }
        public static float SpawnIntervalRemaining(FactionHQ hq)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || hq.faction == null || Plugin.SpawnInterval == null) return 0f;
            if (!LastSpawnPerFaction.TryGetValue(hq.faction, out float last)) return 0f;
            float remaining = Plugin.SpawnInterval.Value - (Time.timeSinceLevelLoad - last);
            return remaining > 0f ? remaining : 0f;
        }
        public static void MarkSpawned(FactionHQ hq)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || hq.faction == null) return;
            LastSpawnPerFaction[hq.faction] = Time.timeSinceLevelLoad;
        }
        public static void RegisterDispatch(FactionHQ hq, string jsonKey, bool isWet, Airbase spawnBase = null)
        {
            if (hq == null || string.IsNullOrEmpty(jsonKey)) return;
            float now = Time.timeSinceLevelLoad;
            DetectLevelReset(now);
            PruneExpired(now);
            Dispatched.Add(new InFlightSpawn
            {
                HQ = hq,
                JsonKey = jsonKey,
                IsWet = isWet,
                Base = spawnBase,
                Expiry = now + MATERIALISE_TTL
            });
        }
        public static void CancelDispatch(FactionHQ hq, string jsonKey, bool isWet)
        {
            if (hq == null || string.IsNullOrEmpty(jsonKey)) return;
            for (int i = Dispatched.Count - 1; i >= 0; i--)
            {
                InFlightSpawn entry = Dispatched[i];
                if (entry.IsWet == isWet && entry.JsonKey == jsonKey && SameFaction(entry.HQ, hq))
                {
                    Dispatched.RemoveAt(i);
                    return;
                }
            }
        }
        public static bool AnyDispatchPending(FactionHQ hq)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || Dispatched.Count == 0) return false;
            PruneExpired(Time.timeSinceLevelLoad);
            for (int i = 0; i < Dispatched.Count; i++)
            {
                if (SameFaction(Dispatched[i].HQ, hq)) return true;
            }
            return false;
        }
        public static int CountInFlight(FactionHQ hq, string jsonKey, bool isWet)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || Dispatched.Count == 0) return 0;
            PruneExpired(Time.timeSinceLevelLoad);
            int count = 0;
            for (int i = 0; i < Dispatched.Count; i++)
            {
                InFlightSpawn entry = Dispatched[i];
                if (entry.IsWet == isWet && entry.JsonKey == jsonKey && SameFaction(entry.HQ, hq)) count++;
            }
            return count;
        }
        public static void OnAircraftRegistered(FactionHQ hq, Aircraft aircraft)
        {
            DetectLevelReset(Time.timeSinceLevelLoad);
            if (hq == null || aircraft == null || aircraft.definition == null) return;
            if (Dispatched.Count == 0) return;
            PruneExpired(Time.timeSinceLevelLoad);
            string jsonKey = aircraft.definition.jsonKey;
            for (int i = 0; i < Dispatched.Count; i++)
            {
                InFlightSpawn entry = Dispatched[i];
                if (entry.JsonKey != jsonKey || !SameFaction(entry.HQ, hq)) continue;
                WetTag.GetOrCreateValue(aircraft).Value = entry.IsWet;
                if (entry.Base != null) HomeBase.GetOrCreateValue(aircraft).Value = entry.Base;
                Dispatched.RemoveAt(i);
                return;
            }
        }
        public static bool WasDispatchedByMod(Aircraft aircraft)
        {
            return aircraft != null && WetTag.TryGetValue(aircraft, out _);
        }
        public static bool TryGetHomeBase(Aircraft aircraft, out Airbase home)
        {
            if (aircraft != null && HomeBase.TryGetValue(aircraft, out StrongBox<Airbase> tag) && tag.Value != null)
            {
                home = tag.Value;
                return true;
            }
            home = null;
            return false;
        }
        public static bool TryGetIsWet(Aircraft aircraft, out bool isWet)
        {
            if (aircraft != null && WetTag.TryGetValue(aircraft, out StrongBox<bool> tag))
            {
                isWet = tag.Value;
                return true;
            }
            isWet = false;
            return false;
        }
        private static bool SameFaction(FactionHQ a, FactionHQ b)
        {
            if (a == null || b == null) return false;
            return a == b || (a.faction != null && a.faction == b.faction);
        }
        private static void PruneExpired(float now)
        {
            for (int i = Dispatched.Count - 1; i >= 0; i--)
            {
                InFlightSpawn entry = Dispatched[i];
                if (entry.HQ == null || now >= entry.Expiry)
                {
                    if (entry.HQ != null)
                    {
                        Plugin.Log.LogWarning($"[SupplyBuffetMod] Dispatched {entry.JsonKey} ({(entry.IsWet ? "Wet" : "Dry")}) never materialised within {MATERIALISE_TTL:F0}s. Releasing its slot.");
                    }
                    Dispatched.RemoveAt(i);
                }
            }
        }
    }
}