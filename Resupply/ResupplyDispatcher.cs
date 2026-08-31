
using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class ResupplyDispatcher
    {
        private static readonly ConditionalWeakTable<Unit, StrongBox<float>> LastRearm =
            new ConditionalWeakTable<Unit, StrongBox<float>>();
        private static readonly ConditionalWeakTable<Unit, StrongBox<float>> LastDrop =
            new ConditionalWeakTable<Unit, StrongBox<float>>();
        private static readonly ConditionalWeakTable<Unit, StrongBox<float>> LastDispatch =
            new ConditionalWeakTable<Unit, StrongBox<float>>();
        public static void MarkRearmed(Unit unit) => Stamp(LastRearm, unit);
        public static void MarkDropped(Unit unit) => Stamp(LastDrop, unit);
        public static void MarkDispatched(Unit unit) => Stamp(LastDispatch, unit);
        public static bool IsOnCooldown(Unit unit)
            => WithinCooldown(unit, MostRecent(unit, includeDispatch: true), DispatchCooldown(unit));
        public static bool IsRecentlyServed(Unit unit)
            => WithinCooldown(unit, MostRecent(unit, includeDispatch: false), ReserviceCooldown(unit));
        private const float TOPUP_FRACTION = 0.25f;
        private const float TOPUP_FLOOR_SECONDS = 15f;
        private static bool StillInDeficit(Unit unit)
        {
            if (unit == null) return false;
            if (Plugin.TopUpUntilFull == null || !Plugin.TopUpUntilFull.Value) return false;
            return unit.HasRequestedRearm;
        }
        private static float DispatchCooldown(Unit unit)
        {
            float full = Plugin.Cfg(Plugin.UnitCooldown, 60f);
            if (!StillInDeficit(unit)) return full;
            return Mathf.Min(full, Mathf.Max(TOPUP_FLOOR_SECONDS, full * TOPUP_FRACTION));
        }
        private static float ReserviceCooldown(Unit unit)
        {
            return Plugin.Cfg(Plugin.ReserviceCooldown, 1f);
        }
        public static float Remaining(Unit unit)
        {
            if (unit == null || Plugin.UnitCooldown == null) return 0f;
            float remaining = DispatchCooldown(unit) - (Time.timeSinceLevelLoad - MostRecent(unit, includeDispatch: true));
            return remaining > 0f ? remaining : 0f;
        }
        private static bool WithinCooldown(Unit unit, float stamp, float window)
        {
            if (unit == null || window <= 0f) return false;
            return Time.timeSinceLevelLoad - stamp < window;
        }
        private static float MostRecent(Unit unit, bool includeDispatch)
        {
            if (unit == null) return float.NegativeInfinity;
            float latest = float.NegativeInfinity;
            if (LastRearm.TryGetValue(unit, out StrongBox<float> rearm) && rearm.Value > latest) latest = rearm.Value;
            if (LastDrop.TryGetValue(unit, out StrongBox<float> drop) && drop.Value > latest) latest = drop.Value;
            if (includeDispatch && LastDispatch.TryGetValue(unit, out StrongBox<float> dispatch) && dispatch.Value > latest) latest = dispatch.Value;
            return latest;
        }
        private static void Stamp(ConditionalWeakTable<Unit, StrongBox<float>> table, Unit unit)
        {
            if (unit == null) return;
            table.GetOrCreateValue(unit).Value = Time.timeSinceLevelLoad;
        }
        public static void TryDispatchResupply(FactionHQ hq, Unit requester)
        {
            if (hq == null || requester == null || requester.disabled) return;
            if (Plugin.ResupplyEnabled != null && !Plugin.ResupplyEnabled.Value) return;
            if (IsOnCooldown(requester))
            {
                if (Plugin.Dbg)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Resupply for '{requester.unitName}' on cooldown ({Remaining(requester):F0}s left).");
                }
                return;
            }
            bool isWet = Plugin.IsNavalUnit(requester);
            if (ResupplySpawner.TriggerResupply(hq, requester, isWet))
            {
                MarkDispatched(requester);
            }
        }
    }
    [HarmonyPatch(typeof(Unit), "RpcRearm")]
    public class Unit_RpcRearm_Timer_Patch
    {
        static void Postfix(Unit __instance)
        {
            ResupplyDispatcher.MarkRearmed(__instance);
            if (Plugin.Dbg)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Unit '{__instance.unitName}' rearmed at {Time.timeSinceLevelLoad:F1}s");
            }
        }
    }
    [HarmonyPatch(typeof(RearmMissionController), "RegisterNeedsRearm")]
    public class RearmMissionController_RegisterNeedsRearm_InstantDispatch_Patch
    {
        static void Postfix(Unit requester)
        {
            if (requester == null || requester.disabled || requester is Aircraft) return;
            FactionHQ hq = requester.NetworkHQ;
            if (hq == null) return;
            if (ResupplyMissionManager.IsUnitAssignedOrQueued(requester)) return;
            try
            {
                ResupplyDispatcher.TryDispatchResupply(hq, requester);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in instant dispatch patch: {ex.Message}");
            }
        }
    }
}