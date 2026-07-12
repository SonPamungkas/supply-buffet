using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "Start")]
    public class Rearmer_Start_SupplyRadius_Patch
    {
        static void Prefix(Rearmer __instance)
        {
            try
            {
                if (__instance.gameObject == null) return;
                string name = __instance.gameObject.name;
                Traverse obj = Traverse.Create(__instance);
                float range;
                if (name.Contains("MunitionsPallet1"))
                {
                    range = Plugin.MunitionsPalletRadius.Value;
                    obj.Field("range").SetValue(range);
                    obj.Field("singleUse").SetValue(!Plugin.MunitionsPalletReplenishable.Value);
                    obj.Field("checkInterval").SetValue(Plugin.MunitionsPalletCheckInterval.Value);
                }
                else if (name.Contains("NavalPallet1"))
                {
                    range = Plugin.NavalPalletRadius.Value;
                    obj.Field("range").SetValue(range);
                    obj.Field("singleUse").SetValue(!Plugin.NavalPalletReplenishable.Value);
                    obj.Field("checkInterval").SetValue(Plugin.NavalPalletCheckInterval.Value);
                }
                else if (name.Contains("MunitionsContainer1"))
                {
                    range = Plugin.MunitionsContainerRadius.Value;
                    obj.Field("range").SetValue(range);
                    obj.Field("singleUse").SetValue(!Plugin.MunitionsContainerReplenishable.Value);
                    obj.Field("checkInterval").SetValue(Plugin.MunitionsContainerCheckInterval.Value);
                }
                else if (name.Contains("NavalSupplyContainer1"))
                {
                    range = Plugin.NavalContainerRadius.Value;
                    obj.Field("range").SetValue(range);
                    obj.Field("singleUse").SetValue(!Plugin.NavalContainerReplenishable.Value);
                    obj.Field("checkInterval").SetValue(Plugin.NavalContainerCheckInterval.Value);
                }
                else
                {
                    return;
                }
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Configured Rearmer '{name}': range={range}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in Rearmer_Start_SupplyRadius_Patch: {ex}");
            }
        }
    }
}
