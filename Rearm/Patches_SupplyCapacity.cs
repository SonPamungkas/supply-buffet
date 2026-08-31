using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "Awake")]
    public class Rearmer_Start_SupplyConfig_Patch
    {
        private static readonly AccessTools.FieldRef<Rearmer, float> MaxCapacityRef = AccessTools.FieldRefAccess<Rearmer, float>("maxCapacity");
        private static readonly AccessTools.FieldRef<Rearmer, bool> SingleUseRef = AccessTools.FieldRefAccess<Rearmer, bool>("singleUse");
        static void Prefix(Rearmer __instance)
        {
            try
            {
                if (__instance.gameObject == null) return;
                string name = __instance.gameObject.name;
                if (name == null) return;
                float capacity;
                bool singleUse;
                float range;
                if (name.IndexOf("MunitionsPallet1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.Cfg(Plugin.MunitionsPalletCapacity, 6000f);
                    singleUse = Plugin.Cfg(Plugin.MunitionsPalletSingleUse, true);
                    range = Plugin.Cfg(Plugin.MunitionsPalletRadius, 100f);
                }
                else if (name.IndexOf("MunitionsPallet2", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.Cfg(Plugin.MunitionsPallet2Capacity, 1500f);
                    singleUse = Plugin.Cfg(Plugin.MunitionsPallet2SingleUse, true);
                    range = Plugin.Cfg(Plugin.MunitionsPallet2Radius, 100f);
                }
                else if (name.IndexOf("NavalPallet1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.Cfg(Plugin.NavalPalletCapacity, 6000f);
                    singleUse = Plugin.Cfg(Plugin.NavalPalletSingleUse, true);
                    range = Plugin.Cfg(Plugin.NavalPalletRadius, 100f);
                }
                else if (name.IndexOf("MunitionsContainer1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.Cfg(Plugin.MunitionsContainerCapacity, 10000f);
                    singleUse = Plugin.Cfg(Plugin.MunitionsContainerSingleUse, true);
                    range = Plugin.Cfg(Plugin.MunitionsContainerRadius, 100f);
                }
                else if (name.IndexOf("NavalSupplyContainer1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.Cfg(Plugin.NavalContainerCapacity, 10000f);
                    singleUse = Plugin.Cfg(Plugin.NavalContainerSingleUse, true);
                    range = Plugin.Cfg(Plugin.NavalContainerRadius, 200f);
                }
                else
                {
                    return;
                }
                __instance.Capacity = capacity;
                MaxCapacityRef(__instance) = capacity;
                SingleUseRef(__instance) = singleUse;
                __instance.Range = range;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Configured Rearmer '{name}': capacity={capacity}, singleUse={singleUse}, range={range}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in Rearmer_Start_SupplyConfig_Patch: {ex}");
            }
        }
    }
}