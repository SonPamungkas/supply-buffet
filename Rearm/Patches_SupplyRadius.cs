using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "Awake")]
    public class Rearmer_Start_SupplyRadius_Patch
    {
        static void Prefix(Rearmer __instance)
        {
            try
            {
                if (__instance.gameObject == null) return;
                string name = __instance.gameObject.name;
                if (name == null) return;
                float range;
                if (name.IndexOf("MunitionsPallet1", StringComparison.Ordinal) >= 0)
                {
                    range = Plugin.MunitionsPalletRadius.Value;
                }
                else if (name.IndexOf("MunitionsPallet2", StringComparison.Ordinal) >= 0)
                {
                    range = Plugin.MunitionsPallet2Radius.Value;
                }
                else if (name.IndexOf("NavalPallet1", StringComparison.Ordinal) >= 0)
                {
                    range = Plugin.NavalPalletRadius.Value;
                }
                else if (name.IndexOf("MunitionsContainer1", StringComparison.Ordinal) >= 0)
                {
                    range = Plugin.MunitionsContainerRadius.Value;
                }
                else if (name.IndexOf("NavalSupplyContainer1", StringComparison.Ordinal) >= 0)
                {
                    range = Plugin.NavalContainerRadius.Value;
                }
                else
                {
                    return;
                }
                __instance.Range = range;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Configured Rearmer '{name}': range={range}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in Rearmer_Start_SupplyRadius_Patch: {ex}");
            }
        }
    }
}