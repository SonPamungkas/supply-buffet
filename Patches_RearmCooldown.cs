// ============================================================================
// FILE: Patches_RearmCooldown.cs
// PURPOSE: Enforces minimum cooldown intervals between successive rearm missions
//          for any given unit to prevent rapid-fire supply spam.
//
// TRIGGERS:
//   - Rearmer_ProcessRearmRequest_Cooldown_Patch: Prefix on Rearmer.ProcessRearmRequest.
//   - Unit_RpcRearm_Timer_Patch: Postfix on Unit.RpcRearm.
//
// EFFECTS:
//   - When a unit successfully rearms, its timestamp is recorded in UnitLastRearmTime.
//   - If ProcessRearmRequest is called while the unit is within UnitCooldown seconds
//     of its last rearm, the request is blocked.
// ============================================================================

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
                    return false; // Skip vanilla rearm processing while on cooldown
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
