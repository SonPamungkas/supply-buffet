using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Ship), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class Ship_Rearm_Timer_Patch
    {
        static void Postfix(Ship __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Ship '{__instance.unitName}' rearmed at {Time.timeSinceLevelLoad}");
        }
    }
    [HarmonyPatch(typeof(GroundVehicle), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class GroundVehicle_Rearm_Timer_Patch
    {
        static void Postfix(GroundVehicle __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
        }
    }
    [HarmonyPatch(typeof(Aircraft), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class Aircraft_Rearm_Timer_Patch
    {
        static void Postfix(Aircraft __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
        }
    }
    [HarmonyPatch(typeof(Ship), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class Ship_CanRearm_Cooldown_Patch
    {
        static bool Prefix(Ship __instance, bool shipRearm, ref bool __result)
        {
            if (!shipRearm)
            {
                __result = false;
                return false;
            }
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box))
            {
                float timeSince = Time.timeSinceLevelLoad - box.Value;
                if (timeSince < Plugin.UnitCooldown.Value)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Ship '{__instance.unitName}' CanRearm blocked: on cooldown ({timeSince} < {Plugin.UnitCooldown.Value})");
                    __result = false;
                    return false;
                }
            }
            __result = true;
            return false;
        }
    }
    [HarmonyPatch(typeof(GroundVehicle), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class GroundVehicle_CanRearm_Cooldown_Patch
    {
        static bool Prefix(GroundVehicle __instance, ref bool __result)
        {
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box)
                && Time.timeSinceLevelLoad - box.Value < Plugin.UnitCooldown.Value)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
    [HarmonyPatch(typeof(Aircraft), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class Aircraft_CanRearm_Cooldown_Patch
    {
        static bool Prefix(Aircraft __instance, ref bool __result)
        {
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box)
                && Time.timeSinceLevelLoad - box.Value < Plugin.UnitCooldown.Value)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
