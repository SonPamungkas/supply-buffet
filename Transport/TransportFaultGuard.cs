using System;
using System.Runtime.CompilerServices;
namespace SupplyBuffetMod
{
    public static class TransportFaultGuard
    {
        private static readonly ConditionalWeakTable<Aircraft, object> Faulted =
            new ConditionalWeakTable<Aircraft, object>();
        public static bool IsFaulted(Aircraft aircraft)
        {
            return aircraft != null && Faulted.TryGetValue(aircraft, out _);
        }
        public static bool Report(Aircraft aircraft, string stateName, Exception ex)
        {
            if (aircraft == null) return false;
            if (Faulted.TryGetValue(aircraft, out _)) return false;
            Faulted.Add(aircraft, null);
            Plugin.Log.LogError($"[SupplyBuffetMod] {aircraft.unitName} threw in {stateName}; handing it back to vanilla AI and not re-entering the state for this airframe. {ex}");
            return true;
        }
    }
}