using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    public class Aircraft_FilterInputs_Nullifier_Patch
    {
        static void Postfix(Aircraft __instance)
        {
            if (!Plugin.AnyControlNullified) return;
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
            if (!Plugin.AnyControlNullified) return;
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
            if (!Plugin.AnyControlNullified) return;
            if (__instance != null && __instance.Player == null && Plugin.IsControlNullified(__instance))
            {
                if (__instance.rb != null && Plugin.TryGetNullifiedVelocityDir(__instance, out Vector3 dir))
                {
                    Vector3 v = __instance.rb.velocity;
                    float horizSpeed = new Vector2(v.x, v.z).magnitude;
                    if (horizSpeed > 0.1f)
                    {
                        __instance.rb.velocity = new Vector3(dir.x * horizSpeed, v.y, dir.z * horizSpeed);
                    }
                }
            }
        }
    }
}