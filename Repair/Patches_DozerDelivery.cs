using System;
using HarmonyLib;
using UnityEngine;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Spawner), "SpawnUnit")]
    public static class Patches_DozerDelivery
    {
        static void Postfix(Unit owner, Unit __result)
        {
            try
            {
                if (__result == null || !(owner is Aircraft carrier)) return;
                if (!ResupplyCensus.WasDispatchedByMod(carrier)) return;
                if (!(__result is GroundVehicle vehicle)) return;
                if (!vehicle.TryGetComponent(out Repairer _)) return;
                Airbase homeAirbase = null;
                if (AirbaseRepairManager.AssignedRepairs.TryGetValue(carrier, out Unit repairTarget) && repairTarget != null)
                {
                    homeAirbase = repairTarget.GetAirbase();
                }
                DozerShepherd.Register(vehicle, homeAirbase);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Exception in dozer delivery patch: {ex.Message}");
            }
        }
    }
}