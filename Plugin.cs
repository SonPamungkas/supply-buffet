using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace SupplyBuffetMod
{
    [BepInPlugin("com.neutral.supplybuffet", "SupplyBuffetMod", "1.8.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        public static ConfigEntry<bool> AdaptiveSupplyEnabled;
        public static ConfigEntry<float> AdaptiveSupplyCooldown;

        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;

        public static ConfigEntry<bool> MunitionsPalletReplenishable;
        public static ConfigEntry<bool> NavalPalletReplenishable;
        public static ConfigEntry<bool> MunitionsContainerReplenishable;
        public static ConfigEntry<bool> NavalContainerReplenishable;

        public static ConfigEntry<float> MunitionsPalletCheckInterval;
        public static ConfigEntry<float> NavalPalletCheckInterval;
        public static ConfigEntry<float> MunitionsContainerCheckInterval;
        public static ConfigEntry<float> NavalContainerCheckInterval;

        public static ConfigEntry<float> UnitCooldown;

        public static ConditionalWeakTable<Unit, StrongBox<float>> UnitLastRearmTime = new ConditionalWeakTable<Unit, StrongBox<float>>();

        public static readonly Dictionary<string, string> AdaptiveSupplyPairs = new Dictionary<string, string>
        {
            { "MunitionsPallet1", "NavalPallet1" },
            { "NavalPallet1", "MunitionsPallet1" },
            { "MunitionsContainer1", "NavalSupplyContainer1" },
            { "NavalSupplyContainer1", "MunitionsContainer1" },
        };

        private static Dictionary<string, WeaponMount> _mountsByJsonKey;
        public static ConditionalWeakTable<Aircraft, StrongBox<float>> AdaptiveSupplyLastSwap = new ConditionalWeakTable<Aircraft, StrongBox<float>>();

        public static WeaponMount FindMountByJsonKey(string jsonKey)
        {
            if (_mountsByJsonKey == null)
            {
                _mountsByJsonKey = Resources.FindObjectsOfTypeAll<WeaponMount>()
                    .Where(m => m != null && !string.IsNullOrEmpty(m.jsonKey))
                    .GroupBy(m => m.jsonKey)
                    .ToDictionary(g => g.Key, g => g.First());
            }
            _mountsByJsonKey.TryGetValue(jsonKey, out var mount);
            return mount;
        }

        public static ConfigEntry<bool> AutoRequestRearmEnabled;

        private void Awake()
        {
            Log = Logger;

            AdaptiveSupplyEnabled = Config.Bind("AdaptiveSupply", "Enabled", true, "Let AI cargo helos swap their pallet/container between ground and naval variants when the type they're carrying has no demand but the other does.");
            AdaptiveSupplyCooldown = Config.Bind("AdaptiveSupply", "Cooldown", 30f, "Minimum time (in seconds) between loadout swaps on the same aircraft.");

            MunitionsPalletRadius = Config.Bind("SupplyRadius", "MunitionsPallet1", 1500f, "Supply radius for Munitions Pallet");
            NavalPalletRadius = Config.Bind("SupplyRadius", "NavalPallet1", 1500f, "Supply radius for Naval Pallet");
            MunitionsContainerRadius = Config.Bind("SupplyRadius", "MunitionsContainer1", 1500f, "Supply radius for Munitions Container");
            NavalContainerRadius = Config.Bind("SupplyRadius", "NavalSupplyContainer1", 1500f, "Supply radius for Naval Container");

            MunitionsPalletReplenishable = Config.Bind("SupplyContainer", "MunitionsPallet1_Replenishable", true, "Whether the Munitions Pallet can be used multiple times.");
            NavalPalletReplenishable = Config.Bind("SupplyContainer", "NavalPallet1_Replenishable", true, "Whether the Naval Pallet can be used multiple times.");
            MunitionsContainerReplenishable = Config.Bind("SupplyContainer", "MunitionsContainer1_Replenishable", true, "Whether the Munitions Container can be used multiple times.");
            NavalContainerReplenishable = Config.Bind("SupplyContainer", "NavalSupplyContainer1_Replenishable", true, "Whether the Naval Container can be used multiple times.");

            MunitionsPalletCheckInterval = Config.Bind("SupplyContainer", "MunitionsPallet1_CheckInterval", 1f, "Throttle (in seconds) between resupply checks for the Munitions Pallet.");
            NavalPalletCheckInterval = Config.Bind("SupplyContainer", "NavalPallet1_CheckInterval", 1f, "Throttle (in seconds) between resupply checks for the Naval Pallet.");
            MunitionsContainerCheckInterval = Config.Bind("SupplyContainer", "MunitionsContainer1_CheckInterval", 1f, "Throttle (in seconds) between resupply checks for the Munitions Container.");
            NavalContainerCheckInterval = Config.Bind("SupplyContainer", "NavalSupplyContainer1_CheckInterval", 1f, "Throttle (in seconds) between resupply checks for the Naval Container.");

            AutoRequestRearmEnabled = Config.Bind("AutoRequestRearm", "Enabled", true, "Let ships and ground vehicles automatically request rearm (join the resupply demand queue) after firing leaves a weapon station short of ammo, instead of requiring a manual player request.");

            UnitCooldown = Config.Bind("Rearming", "UnitCooldown", 10f, "Minimum time (in seconds) between successive resupplies of the same unit, to prevent nonstop firing/rearm loops.");

            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();

            Log.LogInfo("SupplyBuffetMod initialized.");
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "Fire", new Type[] { typeof(Unit), typeof(Unit) })]
    public class WeaponStation_Fire_AutoRequestRearm_Patch
    {
        static void Postfix(WeaponStation __instance, Unit owner)
        {
            if (!Plugin.AutoRequestRearmEnabled.Value) return;
            if (!(owner is Ship) && !(owner is GroundVehicle)) return;

            try
            {
                if (NetworkManagerNuclearOption.i == null || !NetworkManagerNuclearOption.i.Server.Active) return;

                __instance.AccountAmmo();
                if (__instance.Ammo < __instance.FullAmmo && owner is IRearmable rearmable)
                {
                    rearmable.RequestRearm();
                    // Plugin.Log.LogInfo($"[SupplyBuffetMod] Auto-requested rearm for '{owner.unitName}' (ammo {__instance.Ammo}/{__instance.FullAmmo})."); // Removed to prevent log spam
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in WeaponStation_Fire_AutoRequestRearm_Patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(AIHeloTransportState), "SearchForLandingSpot")]
    public class AIHelo_AdaptiveSupply_Patch
    {
        static void Prefix(AIHeloTransportState __instance)
        {
            if (!Plugin.AdaptiveSupplyEnabled.Value) return;

            try
            {
                Aircraft aircraft = Traverse.Create(__instance).Field("aircraft").GetValue<Aircraft>();
                if (aircraft == null || aircraft.weaponManager == null || aircraft.weaponManager.hardpointSets == null) return;

                var box = Plugin.AdaptiveSupplyLastSwap.GetOrCreateValue(aircraft);
                if (Time.timeSinceLevelLoad - box.Value < Plugin.AdaptiveSupplyCooldown.Value) return;

                HardpointSet cargoSet = aircraft.weaponManager.hardpointSets
                    .FirstOrDefault(hs => hs != null && hs.weaponMount != null && hs.weaponMount.Cargo);
                if (cargoSet == null) return;

                WeaponMount currentMount = cargoSet.weaponMount;
                WeaponInfo info = currentMount.info;
                if (info == null || (!info.rearmShip && !info.rearmGround)) return;

                if (!aircraft.NetworkHQ.GetListUnitsRequiringRearm(out var demand) || demand == null) return;

                bool hasShipDemand = demand.Any(u => u is Ship);
                bool hasGroundDemand = demand.Any(u => u is GroundVehicle);

                bool needsSwap = (info.rearmShip && !hasShipDemand && hasGroundDemand)
                               || (info.rearmGround && !hasGroundDemand && hasShipDemand);
                if (!needsSwap) return;

                if (string.IsNullOrEmpty(currentMount.jsonKey) || !Plugin.AdaptiveSupplyPairs.TryGetValue(currentMount.jsonKey, out string targetKey)) return;

                WeaponMount replacement = Plugin.FindMountByJsonKey(targetKey);
                if (replacement == null) return;

                cargoSet.RemoveMounts();
                cargoSet.SpawnMounts(aircraft, replacement);
                box.Value = Time.timeSinceLevelLoad;

                Plugin.Log.LogInfo($"[SupplyBuffetMod] Adaptive Supply: swapped {currentMount.jsonKey} -> {replacement.jsonKey} on '{aircraft.unitName}'.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SupplyBuffetMod] Error in Adaptive Supply Patch: {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(Rearmer), "Start")]
    public class Rearmer_Start_SupplyRadius_Patch
    {
        static void Postfix(Rearmer __instance)
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

    [HarmonyPatch(typeof(Ship), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class Ship_Rearm_Timer_Patch
    {
        static void Postfix(Ship __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
        }
    }

    [HarmonyPatch(typeof(GroundVehicle), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class GroundVehicle_Rearm_Timer_Patch
    {
        static void Postfix(GroundVehicle __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Rearm", new Type[] { typeof(RearmEventArgs) })]
    public class Aircraft_Rearm_Timer_Patch
    {
        static void Postfix(Aircraft __instance)
        {
            Plugin.UnitLastRearmTime.GetOrCreateValue(__instance).Value = Time.timeSinceLevelLoad;
        }
    }

    [HarmonyPatch(typeof(Ship), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class Ship_CanRearm_Cooldown_Patch
    {
        static bool Prefix(Ship __instance, ref bool __result)
        {
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box)
                && Time.timeSinceLevelLoad - box.Value < Plugin.UnitCooldown.Value)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(GroundVehicle), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class GroundVehicle_CanRearm_Cooldown_Patch
    {
        static bool Prefix(GroundVehicle __instance, ref bool __result)
        {
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box)
                && Time.timeSinceLevelLoad - box.Value < Plugin.UnitCooldown.Value)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "CanRearm", new Type[] { typeof(bool), typeof(bool), typeof(bool) })]
    public class Aircraft_CanRearm_Cooldown_Patch
    {
        static bool Prefix(Aircraft __instance, ref bool __result)
        {
            if (Plugin.UnitLastRearmTime.TryGetValue(__instance, out var box)
                && Time.timeSinceLevelLoad - box.Value < Plugin.UnitCooldown.Value)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }
}
