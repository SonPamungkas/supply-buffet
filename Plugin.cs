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
    [BepInPlugin("com.neutral.supplybuffet", "SupplyBuffetMod", "2.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> DebugLogging;
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
        public static bool ForceSpawnInProgress = false;
        public static bool ForceSpawnIsNaval = false;
        public static readonly Dictionary<string, string> AdaptiveSupplyPairs = new Dictionary<string, string>
        {
            { "MunitionsPallet1", "NavalPallet1" },
            { "NavalPallet1", "MunitionsPallet1" },
            { "MunitionsContainer1", "NavalSupplyContainer1" },
            { "NavalSupplyContainer1", "MunitionsContainer1" },
        };
        private static Dictionary<string, WeaponMount> _mountsByJsonKey;
        public static ConditionalWeakTable<Aircraft, StrongBox<float>> AdaptiveSupplyLastSwap = new ConditionalWeakTable<Aircraft, StrongBox<float>>();
        public static string GetMountKey(WeaponMount mount)
        {
            if (mount == null) return null;
            if (!string.IsNullOrEmpty(mount.jsonKey)) return mount.jsonKey;
            string n = mount.name;
            if (n.EndsWith("(Clone)")) n = n.Substring(0, n.Length - 7);
            return n;
        }
        public static WeaponMount FindMountByKey(string key)
        {
            if (_mountsByJsonKey == null)
            {
                _mountsByJsonKey = Resources.FindObjectsOfTypeAll<WeaponMount>()
                    .Where(m => m != null && !string.IsNullOrEmpty(GetMountKey(m)))
                    .GroupBy(m => GetMountKey(m))
                    .ToDictionary(g => g.Key, g => g.First());
            }
            _mountsByJsonKey.TryGetValue(key, out var mount);
            return mount;
        }
        public static ConfigEntry<bool> AutoRequestRearmEnabled;
        private void Awake()
        {
            Log = Logger;
            DebugLogging = Config.Bind("General", "DebugLogging", false, "Enable debug logging for Supply Buffet.");
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
}
