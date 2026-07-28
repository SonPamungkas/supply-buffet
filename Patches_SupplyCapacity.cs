using System;
using HarmonyLib;
using UnityEngine;

namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "Awake")]
    public class Rearmer_Start_SupplyCapacity_Patch
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
                bool isSingleUse = true;

                if (name.IndexOf("MunitionsPallet1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.MunitionsPalletCapacity.Value;
                    isSingleUse = Plugin.MunitionsPalletSingleUse.Value;
                }
                else if (name.IndexOf("MunitionsPallet2", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.MunitionsPallet2Capacity.Value;
                    isSingleUse = Plugin.MunitionsPallet2SingleUse.Value;
                }
                else if (name.IndexOf("NavalPallet1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.NavalPalletCapacity.Value;
                    isSingleUse = Plugin.NavalPalletSingleUse.Value;
                }
                else if (name.IndexOf("MunitionsContainer1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.MunitionsContainerCapacity.Value;
                    isSingleUse = Plugin.MunitionsContainerSingleUse.Value;
                }
                else if (name.IndexOf("NavalSupplyContainer1", StringComparison.Ordinal) >= 0)
                {
                    capacity = Plugin.NavalContainerCapacity.Value;
                    isSingleUse = Plugin.NavalContainerSingleUse.Value;
                }
                else
                {
                    return;
                }

                __instance.Capacity = capacity;
                MaxCapacityRef(__instance) = capacity;
                SingleUseRef(__instance) = isSingleUse;

                Plugin.Log.LogInfo($"[SupplyBuffetMod] Configured Rearmer '{name}': capacity={capacity}, singleUse={isSingleUse}.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in Rearmer_Start_SupplyCapacity_Patch: {ex}");
            }
        }
    }
}
