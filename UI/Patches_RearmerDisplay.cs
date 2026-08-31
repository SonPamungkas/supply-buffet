
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using TMPro;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class RearmerDisplayHooks
    {
        public sealed class PanelOwner
        {
            public Aircraft Aircraft;
            public HUDUnitMarker Marker;
            public bool OwnsObject;
        }
        private static readonly ConditionalWeakTable<RearmerDisplay, PanelOwner> Ours =
            new ConditionalWeakTable<RearmerDisplay, PanelOwner>();
        private static AccessTools.FieldRef<RearmerDisplay, TMP_Text> _text;
        private static AccessTools.FieldRef<RearmerDisplay, TMP_Text> _state;
        private static AccessTools.FieldRef<RearmerDisplay, TMP_Text> _availability;
        private static AccessTools.FieldRef<RearmerDisplay, TMP_Text> _targetDisplay;
        private static AccessTools.FieldRef<RearmerDisplay, GameObject> _panel;
        private static bool _resolved;
        private static bool _usable;
        private static bool Resolve()
        {
            if (_resolved) return _usable;
            _resolved = true;
            try
            {
                _text = AccessTools.FieldRefAccess<RearmerDisplay, TMP_Text>("text");
                _state = AccessTools.FieldRefAccess<RearmerDisplay, TMP_Text>("state");
                _availability = AccessTools.FieldRefAccess<RearmerDisplay, TMP_Text>("availability");
                _targetDisplay = AccessTools.FieldRefAccess<RearmerDisplay, TMP_Text>("targetDisplay");
                _panel = AccessTools.FieldRefAccess<RearmerDisplay, GameObject>("additionalInfoPanel");
                _usable = true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Rearmer panel layout not as expected; transport info panel disabled. {ex.Message}");
                _usable = false;
            }
            return _usable;
        }
        public static void Claim(RearmerDisplay display, Aircraft aircraft, HUDUnitMarker marker, bool ownsObject)
        {
            if (display == null || aircraft == null) return;
            Ours.Remove(display);
            Ours.Add(display, new PanelOwner { Aircraft = aircraft, Marker = marker, OwnsObject = ownsObject });
        }
        public static void Release(RearmerDisplay display)
        {
            if (display != null) Ours.Remove(display);
        }
        public static bool IsOurs(RearmerDisplay display, out PanelOwner owner)
        {
            owner = null;
            return display != null && Ours.TryGetValue(display, out owner);
        }
        public static bool ShowSpectatePanel(RearmerDisplay display)
        {
            if (!Resolve() || display == null) return false;
            GameObject info = _panel != null ? _panel(display) : null;
            if (info != null) info.SetActive(true);
            display.gameObject.SetActive(true);
            return true;
        }
        internal static string LastTeardownReason = "";
        public static bool Draw(RearmerDisplay display, PanelOwner owner)
        {
            LastTeardownReason = "";
            if (!Resolve() || owner == null) { LastTeardownReason = "fields unresolved or owner lost"; return false; }
            if (owner.OwnsObject)
            {
                if (owner.Marker == null) { LastTeardownReason = "marker null"; return false; }
                if (!owner.Marker.selected) { LastTeardownReason = "marker deselected"; return false; }
            }
            Aircraft aircraft = owner.Aircraft;
            if (aircraft == null || aircraft.disabled) { LastTeardownReason = "aircraft gone"; return false; }
            GameObject panel = _panel != null ? _panel(display) : null;
            if (panel != null && !panel.activeSelf) panel.SetActive(true);
            AIFixedWingTransportState transport = Patches_RearmMissionDisplay.TransportStateOf(aircraft);
            bool onMission = transport != null && transport.HasMission;
            if (_state != null && _state(display) != null)
            {
                _state(display).text = onMission ? transport.MissionKindLabel : "Not on a delivery";
            }
            if (_availability != null && _availability(display) != null)
            {
                string target = (transport != null) ? transport.MissionTargetLabel : "";
                _availability(display).text = string.IsNullOrEmpty(target) ? "No Target" : target;
            }
            if (_targetDisplay != null && _targetDisplay(display) != null)
            {
                float range = (transport != null) ? transport.DistanceToTarget : -1f;
                _targetDisplay(display).text = (range >= 0f) ? UnitConverter.DistanceReading(range) : "";
            }
            if (_text != null && _text(display) != null)
            {
                _text(display).text = (transport != null) ? $"{transport.CargoAboard} drop" : "";
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(HUDUnitMarker), "SelectMarker")]
    public static class Patches_HUDUnitMarker_SelectMarker
    {
        internal static bool IsTransportAirframe(Aircraft aircraft)
        {
            return aircraft != null && aircraft.definition != null
                && aircraft.definition.jsonKey == ChimeraHelper.ChimeraKey;
        }
        private static void Report(Aircraft aircraft, string outcome)
        {
            Plugin.Log.LogInfo($"[SB|U1] {aircraft.unitName} panel: {outcome}.");
        }
        static void Postfix(HUDUnitMarker __instance)
        {
            try
            {
                Aircraft aircraft = __instance.unit as Aircraft;
                if (aircraft == null || aircraft.disabled) return;
                if (!IsTransportAirframe(aircraft))
                {
                    Report(aircraft, "skipped - not a Chimera");
                    return;
                }
                if (aircraft.TryGetComponent<Rearmer>(out _))
                {
                    Report(aircraft, "skipped - carries a Rearmer, vanilla owns it");
                    return;
                }
                if (!ResupplyCensus.WasDispatchedByMod(aircraft))
                {
                    Report(aircraft, "skipped - not dispatched by mod");
                    return;
                }
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud == null || hud.aircraft == null || hud.rearmerDisplay == null)
                {
                    Report(aircraft, "skipped - hud or prefab unavailable");
                    return;
                }
                if (aircraft.NetworkHQ != hud.aircraft.NetworkHQ)
                {
                    Report(aircraft, "skipped - different HQ");
                    return;
                }
                if (__instance.image == null)
                {
                    Report(aircraft, "skipped - marker has no image");
                    return;
                }
                GameObject go = UnityEngine.Object.Instantiate(hud.rearmerDisplay, __instance.image.transform);
                go.transform.localScale = Vector3.one * 0.05f;
                go.transform.localPosition = Vector3.zero;
                RearmerDisplay display = go.GetComponent<RearmerDisplay>();
                if (display == null)
                {
                    UnityEngine.Object.Destroy(go);
                    Report(aircraft, "skipped - prefab has no RearmerDisplay");
                    return;
                }
                RearmerDisplayHooks.Claim(display, aircraft, __instance, ownsObject: true);
                display.Initialize(__instance, null, null);
                bool onDelivery = Patches_RearmMissionDisplay.TransportStateOf(aircraft) != null;
                Report(aircraft, onDelivery ? "created" : "created (not on a delivery yet)");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Could not open the transport info panel: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(RearmerDisplay), "Initialize")]
    public static class Patches_RearmerDisplay_Initialize
    {
        static void Postfix(RearmerDisplay __instance, HUDUnitMarker marker, Unit unit)
        {
            try
            {
                if (marker != null) return;
                Aircraft aircraft = unit as Aircraft;
                if (aircraft == null || aircraft.disabled
                    || !Patches_HUDUnitMarker_SelectMarker.IsTransportAirframe(aircraft)
                    || !ResupplyCensus.WasDispatchedByMod(aircraft))
                {
                    RearmerDisplayHooks.Release(__instance);
                    return;
                }
                GameManager.GetLocalFaction(out Faction localFaction);
                bool visible = (localFaction == null
                                || (aircraft.NetworkHQ != null && localFaction == aircraft.NetworkHQ.faction))
                               && !PlayerSettings.cinematicMode;
                if (!visible)
                {
                    RearmerDisplayHooks.Release(__instance);
                    return;
                }
                RearmerDisplayHooks.Claim(__instance, aircraft, null, ownsObject: false);
                if (!RearmerDisplayHooks.ShowSpectatePanel(__instance)) return;
                Plugin.Log.LogInfo($"[SB|U3] {aircraft.unitName} spectate panel: shown.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Could not enable the spectate panel: {ex.Message}");
                RearmerDisplayHooks.Release(__instance);
            }
        }
    }
    [HarmonyPatch(typeof(RearmerDisplay), "Update")]
    public static class Patches_RearmerDisplay_Update
    {
        static bool Prefix(RearmerDisplay __instance)
        {
            try
            {
                if (!RearmerDisplayHooks.IsOurs(__instance, out RearmerDisplayHooks.PanelOwner owner)) return true;
                if (!RearmerDisplayHooks.Draw(__instance, owner))
                {
                    string reason = RearmerDisplayHooks.LastTeardownReason;
                    if (reason != "marker deselected")
                    {
                        string name = (owner.Aircraft != null) ? owner.Aircraft.unitName : "a transport";
                        Plugin.Log.LogInfo($"[SB|U2] {name} panel torn down: {reason}.");
                    }
                    if (owner.OwnsObject)
                    {
                        UnityEngine.Object.Destroy(__instance.gameObject);
                    }
                    else
                    {
                        RearmerDisplayHooks.Release(__instance);
                    }
                }
                return false;   
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Transport info panel failed; closing it. {ex.Message}");
                UnityEngine.Object.Destroy(__instance.gameObject);
                return false;
            }
        }
    }
}