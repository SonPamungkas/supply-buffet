using System.Collections.Generic;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class AirbaseFactionCache
    {
        private const float CACHE_TTL = 1f;
        private static float _builtAt = float.NegativeInfinity;
        private static readonly Dictionary<Faction, List<Airbase>> _byFaction = new Dictionary<Faction, List<Airbase>>();
        private static readonly List<Airbase> _empty = new List<Airbase>();
        internal static void ResetForNewLevel()
        {
            _builtAt = float.NegativeInfinity;
            _byFaction.Clear();
        }
        public static List<Airbase> GetFactionAirbases(Faction faction)
        {
            if (faction == null) return _empty;
            RebuildIfStale();
            return _byFaction.TryGetValue(faction, out List<Airbase> list) ? list : _empty;
        }
        private static void RebuildIfStale()
        {
            float now = Time.timeSinceLevelLoad;
            if (now - _builtAt < CACHE_TTL) return;
            _builtAt = now;
            foreach (var list in _byFaction.Values) list.Clear();
            if (FactionRegistry.airbaseLookup == null) return;
            foreach (var ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null || !ab.isActiveAndEnabled || ab.CurrentHQ == null || ab.CurrentHQ.faction == null) continue;
                Faction f = ab.CurrentHQ.faction;
                if (!_byFaction.TryGetValue(f, out List<Airbase> list))
                {
                    list = new List<Airbase>();
                    _byFaction[f] = list;
                }
                list.Add(ab);
            }
        }
    }
}