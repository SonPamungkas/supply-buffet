using System;
using HarmonyLib;
using UnityEngine;

namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "ProcessRearmRequest")]
    public class Rearmer_ProcessRearmRequest_Cooldown_Patch
    {
        static bool Prefix(Rearmer __instance, Unit unitToRearm, ref bool __result, ref int shortfall)
        {
            if (unitToRearm == null) return true;
            if (Plugin.UnitLastRearmTime.TryGetValue(unitToRearm, out var box))
            {
                float timeSince = Time.timeSinceLevelLoad - box.Value;
                if (timeSince < Plugin.UnitCooldown.Value)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Rearm blocked for '{unitToRearm.unitName}': on cooldown ({timeSince:F1}s < {Plugin.UnitCooldown.Value}s)");
                    shortfall = 0;
                    __result = false;
                    return false; 
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Unit), "RpcRearm")]
    public class Unit_RpcRearm_Timer_Patch
    {
        static void Postfix(Unit __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Unit '{__instance.unitName}' rearmed at {Time.timeSinceLevelLoad:F1}s");
        }
    }
}
