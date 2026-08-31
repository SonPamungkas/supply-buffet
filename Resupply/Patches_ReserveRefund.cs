using HarmonyLib;
namespace SupplyBuffetMod
{
    [HarmonyPatch(typeof(Aircraft), "ReturnToInventory")]
    public static class Patches_ReserveRefund
    {
        internal class RefundState
        {
            public FactionHQ HQ;
            public AircraftDefinition Definition;
        }
        static void Prefix(Aircraft __instance, out RefundState __state)
        {
            __state = null;
            if (__instance == null || __instance.Player != null) return;
            if (__instance.NetworkHQ == null || __instance.definition == null) return;
            if (!ChimeraSpawnQueue.IsServerAuthority()) return;
            if (!Plugin.IsModDispatchedFlight(__instance)) return;
            __state = new RefundState
            {
                HQ = __instance.NetworkHQ,
                Definition = __instance.definition
            };
        }
        static void Postfix(Aircraft __instance, RefundState __state)
        {
            if (__state == null || __state.HQ == null || __state.Definition == null) return;
            __state.HQ.AddSupplyUnit(__state.Definition, -1);
            if (Plugin.Dbg)
            {
                string name = (__instance != null) ? __instance.unitName : __state.Definition.unitName;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] '{name}' recovered; cancelled the {__state.Definition.unitName} reserve refund so the sortie nets zero.");
            }
        }
    }
}