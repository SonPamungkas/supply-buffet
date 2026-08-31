using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx.Bootstrap;
using HarmonyLib;
namespace SupplyBuffetMod
{
    public static class Patches_AryxChimera
    {
        private const string ARYX_GUID = "Aryx_MC260_Chimera";
        private const string SELECTOR_TYPE = "Aryx_MC260_Chimera.FixedWingTransportSelector";
        private const string SELECTOR_METHOD = "ShouldUseTransportState";
        public static bool AryxPresent { get; private set; }
        public static void TryApply(Harmony harmony)
        {
            try
            {
                if (Chainloader.PluginInfos == null || !Chainloader.PluginInfos.ContainsKey(ARYX_GUID))
                {
                    return;
                }
                AryxPresent = true;
                ApplySelectorPatch(harmony);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Aryx Chimera compatibility patch could not be applied: {ex.Message}");
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        private static void ApplySelectorPatch(Harmony harmony)
        {
            Type selector = AccessTools.TypeByName(SELECTOR_TYPE);
            if (selector == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Aryx Chimera mod is loaded but '{SELECTOR_TYPE}' was not found; leaving both transport states active.");
                return;
            }
            MethodInfo target = AccessTools.Method(selector, SELECTOR_METHOD, new[] { typeof(Pilot) });
            if (target == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Aryx Chimera mod is loaded but '{SELECTOR_METHOD}(Pilot)' was not found; leaving both transport states active.");
                return;
            }
            MethodInfo prefix = AccessTools.Method(typeof(Patches_AryxChimera), nameof(DeclineOurFlights));
            harmony.Patch(target, new HarmonyMethod(prefix));
            Plugin.Log.LogInfo("[SupplyBuffetMod] Aryx MC-260 Chimera detected; its transport state will skip flights this mod dispatched, and ours will skip every other Chimera.");
        }
        public static bool DeclineOurFlights(Pilot pilot, ref bool __result)
        {
            try
            {
                if (pilot == null || pilot.aircraft == null) return true;
                if (!ResupplyCensus.WasDispatchedByMod(pilot.aircraft)) return true;
                __result = false;
                return false;   
            }
            catch
            {
                return true;    
            }
        }
    }
}