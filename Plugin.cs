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
    [BepInPlugin("neutral.supplybuffet", "SupplyBuffetMod", "2.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        public static ConfigEntry<bool> DebugLogging;

        public static ConfigEntry<bool> ExpressRearmEnabled;
        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> MunitionsPallet2Radius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;

        public static ConfigEntry<bool> MunitionsPalletSingleUse;
        public static ConfigEntry<bool> MunitionsPallet2SingleUse;
        public static ConfigEntry<bool> NavalPalletSingleUse;
        public static ConfigEntry<bool> MunitionsContainerSingleUse;
        public static ConfigEntry<bool> NavalContainerSingleUse;

        public static ConfigEntry<float> MunitionsPalletCapacity;
        public static ConfigEntry<float> MunitionsPallet2Capacity;
        public static ConfigEntry<float> NavalPalletCapacity;
        public static ConfigEntry<float> MunitionsContainerCapacity;
        public static ConfigEntry<float> NavalContainerCapacity;

        public static ConfigEntry<float> UnitCooldown;

        public static ConditionalWeakTable<Unit, StrongBox<float>> UnitLastRearmTime = new ConditionalWeakTable<Unit, StrongBox<float>>();

        public static bool ForceSpawnInProgress = false;
        public static bool ForceSpawnIsNaval = false;
        private static Dictionary<string, WeaponMount> _mountsByJsonKey;

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

        private void Awake()
        {
            Log = Logger;
            DebugLogging = Config.Bind("General", "DebugLogging", false, "Enable debug logging for Supply Buffet.");

            ExpressRearmEnabled = Config.Bind("ExpressRearm", "Enabled", true, "Let ships and ground vehicles immediately request rearm, and spawn supply helicopters when they do.");

            MunitionsPalletRadius = Config.Bind("SupplyRadius", "MunitionsPallet1", 100f, "Supply radius for Munitions Pallet");
            MunitionsPallet2Radius = Config.Bind("SupplyRadius", "MunitionsPallet2", 100f, "Supply radius for Small Munitions Pallet");
            NavalPalletRadius = Config.Bind("SupplyRadius", "NavalPallet1", 100f, "Supply radius for Naval Pallet");
            MunitionsContainerRadius = Config.Bind("SupplyRadius", "MunitionsContainer1", 100f, "Supply radius for Munitions Container");
            NavalContainerRadius = Config.Bind("SupplyRadius", "NavalSupplyContainer1", 200f, "Supply radius for Naval Container");

            MunitionsPalletSingleUse = Config.Bind("SupplyContainer", "MunitionsPallet1_SingleUse", false, "If true, the container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            MunitionsPallet2SingleUse = Config.Bind("SupplyContainer", "MunitionsPallet2_SingleUse", false, "If true, the small pallet is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            NavalPalletSingleUse = Config.Bind("SupplyContainer", "NavalPallet1_SingleUse", false, "If true, the naval pallet is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            MunitionsContainerSingleUse = Config.Bind("SupplyContainer", "MunitionsContainer1_SingleUse", false, "If true, the munitions container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");
            NavalContainerSingleUse = Config.Bind("SupplyContainer", "NavalSupplyContainer1_SingleUse", false, "If true, the naval container is destroyed when depleted. If false, it provides infinite supply (disables singleUse).");

            MunitionsPalletCapacity = Config.Bind("SupplyCapacity", "MunitionsPallet1", 6000f, "Supply capacity for Munitions Pallet");
            MunitionsPallet2Capacity = Config.Bind("SupplyCapacity", "MunitionsPallet2", 1500f, "Supply capacity for Small Munitions Pallet");
            NavalPalletCapacity = Config.Bind("SupplyCapacity", "NavalPallet1", 6000f, "Supply capacity for Naval Pallet");
            MunitionsContainerCapacity = Config.Bind("SupplyCapacity", "MunitionsContainer1", 10000f, "Supply capacity for Munitions Container");
            NavalContainerCapacity = Config.Bind("SupplyCapacity", "NavalSupplyContainer1", 10000f, "Supply capacity for Naval Container");

            UnitCooldown = Config.Bind("Rearming", "UnitCooldown", 10f, "Minimum time (in seconds) between successive resupplies of the same unit, to prevent nonstop firing/rearm loops.");

            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();

            Log.LogInfo("SupplyBuffetMod initialized.");
        }
    }
}
