using System;
using HarmonyLib;
using TMPro;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(UnitDebug), "Update")]
    public static class Patches_UnitDebug_Target
    {
        private static AccessTools.FieldRef<UnitDebug, Unit> _followingUnit;
        private static AccessTools.FieldRef<UnitDebug, TMP_Text> _target;
        private static bool _resolved;
        private static bool _usable;
        private static bool Resolve()
        {
            if (_resolved) return _usable;
            _resolved = true;
            try
            {
                _followingUnit = AccessTools.FieldRefAccess<UnitDebug, Unit>("followingUnit");
                _target = AccessTools.FieldRefAccess<UnitDebug, TMP_Text>("target");
                _usable = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Unit debug panel layout not as expected; transport target line disabled. {ex.Message}");
                _usable = false;
            }
            return _usable;
        }
        static void Postfix(UnitDebug __instance)
        {
            try
            {
                if (!Resolve()) return;
                Aircraft aircraft = (_followingUnit != null ? _followingUnit(__instance) : null) as Aircraft;
                if (aircraft == null || aircraft.disabled) return;
                if (!ResupplyCensus.WasDispatchedByMod(aircraft)) return;
                AIFixedWingTransportState transport = Patches_RearmMissionDisplay.TransportStateOf(aircraft);
                if (transport == null) return;
                TMP_Text target = _target != null ? _target(__instance) : null;
                if (target == null) return;
                string label = transport.MissionTargetLabel;
                target.text = string.IsNullOrEmpty(label) ? "none" : label;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Transport target line failed; disabling it. {ex.Message}");
                _usable = false;
            }
        }
    }
}