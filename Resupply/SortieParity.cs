using System.Collections.Generic;
namespace SupplyBuffetMod
{
    public enum SortieCategory
    {
        DryMoving,
        DryStatic,
        Repair
    }
    public static class SortieParity
    {
        private static readonly Dictionary<string, int> Counters = new Dictionary<string, int>();
        internal static void ResetForNewLevel()
        {
            Counters.Clear();
        }
        private static string Key(FactionHQ hq, SortieCategory category)
        {
            string faction = (hq != null && hq.faction != null) ? hq.faction.name : "none";
            return faction + "/" + category;
        }
        public static int Next(FactionHQ hq, SortieCategory category)
        {
            string key = Key(hq, category);
            Counters.TryGetValue(key, out int value);
            Counters[key] = value + 1;
            return value;
        }
        public static bool IsFirstOfPair(int sortieIndex)
        {
            return (sortieIndex % 2) == 0;
        }
    }
}