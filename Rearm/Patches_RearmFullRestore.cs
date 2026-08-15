using HarmonyLib;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Rearmer), "ProcessRearmRequest")]
    public static class Rearmer_ProcessRearmRequest_FullRestore_Patch
    {
        private static readonly AccessTools.FieldRef<Rearmer, bool> SingleUseRef =
            AccessTools.FieldRefAccess<Rearmer, bool>("singleUse");
        static bool Prefix(Rearmer __instance, Unit unitToRearm, ref int shortfall, ref bool __result)
        {
            shortfall = 0;
            if (__instance == null || unitToRearm == null || unitToRearm.weaponStations == null) return true;
            if (!SupplyFullRestore.IsFullRestore(__instance)) return true;   
            bool allowNuclear = Plugin.AllowNuclearFieldRearm != null && Plugin.AllowNuclearFieldRearm.Value;
            int[] stations = new int[unitToRearm.weaponStations.Count];
            bool granted = false;
            for (int i = 0; i < unitToRearm.weaponStations.Count; i++)
            {
                WeaponStation ws = unitToRearm.weaponStations[i];
                if (ws == null || ws.WeaponInfo == null) continue;
                if (ws.WeaponInfo.cargo || ws.WeaponInfo.massPerRound == 0f) continue;
                int deficit = ws.FullAmmo - ws.GetAmmoTotal();
                if (deficit <= 0) continue;
                if (ws.WeaponInfo.nuclear && !allowNuclear)
                {
                    stations[i] = -3;   
                    continue;
                }
                stations[i] = deficit;  
                granted = true;
            }
            unitToRearm.RpcRearm(new RearmEventArgs
            {
                Rearmer = __instance.Unit,
                Stations = stations
            });
            if (granted)
            {
                __instance.Capacity = 0f;
                if (__instance.Unit != null)
                {
                    __instance.Unit.RpcUpdateRearmerCapacity(0f);
                    if (SingleUseRef(__instance)) __instance.Unit.Networkdisabled = true;
                }
                if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Full restore: '{__instance.gameObject.name}' fully rearmed '{unitToRearm.unitName}' and was consumed.");
                }
            }
            __result = granted;
            return false;   
        }
    }
}