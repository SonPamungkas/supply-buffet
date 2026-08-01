using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(AIHeloTransportState), "DeployCargo")]
    public class AIHeloTransportState_DeployCargo_Nullifier_Patch
    {
        private static readonly AccessTools.FieldRef<PilotBaseState, Aircraft> AircraftRef = AccessTools.FieldRefAccess<PilotBaseState, Aircraft>("aircraft");
        static void Postfix(AIHeloTransportState __instance)
        {
            if (__instance == null) return;
            try
            {
                var aircraft = AircraftRef(__instance);
                if (aircraft != null)
                {
                    Plugin.TriggerControlNullifier(aircraft, 5.0f);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in AIHeloTransportState_DeployCargo_Nullifier_Patch: {ex.Message}");
            }
        }
    }
    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    public class Aircraft_FilterInputs_Nullifier_Patch
    {
        static void Prefix(Aircraft __instance)
        {
            ApplyControlNullifier(__instance);
        }
        static void Postfix(Aircraft __instance)
        {
            ApplyControlNullifier(__instance);
        }
        private static void ApplyControlNullifier(Aircraft __instance)
        {
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                var inputs = __instance.GetInputs();
                if (inputs != null)
                {
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                }
            }
        }
    }
    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    public class Aircraft_FixedUpdate_Nullifier_Patch
    {
        static void Prefix(Aircraft __instance)
        {
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                var inputs = __instance.GetInputs();
                if (inputs != null)
                {
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                }
            }
        }
        static void Postfix(Aircraft __instance)
        {
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                if (__instance.rb != null && Plugin.TryGetNullifiedVelocityDir(__instance, out Vector3 dir))
                {
                    float speed = __instance.rb.velocity.magnitude;
                    if (speed > 0.1f)
                    {
                        __instance.rb.velocity = dir * speed;
                    }
                }
            }
        }
    }
}