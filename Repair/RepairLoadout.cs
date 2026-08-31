
using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;
namespace SupplyBuffetMod
{
    public static class RepairLoadout
    {
        public const string DozerMount = "UGVDozer1x1";
        private static readonly string[] IbisDozerStations = { "Cargo Bay" };
        private static readonly string[] IbisEmptyStations = { "Cargo Bay (Front)", "Cargo Bay (Rear)" };
        private static readonly string[] TarantulaDozerStations = { "Cargo Bay (Front)", "Cargo Bay(Rear)" };
        private static readonly string[] TarantulaEmptyStations = { "Cargo Bay" };
        public static Loadout Build(string jsonKey, WeaponManager manager, int sortieIndex)
        {
            if (jsonKey == "Aryx_CargoPlane1")
            {
                Loadout chimera = ChimeraHelper.CreateRepairLoadout(sortieIndex, out string chimeraName);
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Chimera repair loadout: {chimeraName}.");
                return chimera;
            }
            var loadout = new Loadout { weapons = new List<WeaponMount>() };
            if (manager == null || manager.hardpointSets == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] {jsonKey} hardpointSets unavailable; repair loadout cannot be built.");
                return loadout;
            }
            string[] dozerStations;
            string[] emptyStations;
            if (jsonKey == "QuadVTOL1")
            {
                dozerStations = TarantulaDozerStations;
                emptyStations = TarantulaEmptyStations;
            }
            else
            {
                dozerStations = IbisDozerStations;
                emptyStations = IbisEmptyStations;
            }
            WeaponMount dozer = ChimeraHelper.GetWeaponMount(DozerMount);
            if (dozer == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] Mount '{DozerMount}' not found in the catalogue; repair sortie will carry nothing.");
            }
            int count = manager.hardpointSets.Length;
            var chosen = new WeaponMount[count];
            for (int i = 0; i < count; i++)
            {
                HardpointSet hp = manager.hardpointSets[i];
                if (hp != null && Contains(dozerStations, hp.name)) chosen[i] = dozer;
            }
            var precluded = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                if (chosen[i] == null) continue;
                HardpointSet hp = manager.hardpointSets[i];
                if (hp?.precludingHardpointSets == null) continue;
                foreach (byte index in hp.precludingHardpointSets) precluded.Add(index);
            }
            for (int i = 0; i < count; i++)
            {
                HardpointSet hp = manager.hardpointSets[i];
                if (hp == null) { loadout.weapons.Add(null); continue; }
                if (chosen[i] != null) { loadout.weapons.Add(chosen[i]); continue; }
                if (precluded.Contains(i) || Contains(emptyStations, hp.name)) { loadout.weapons.Add(null); continue; }
                loadout.weapons.Add(RandomOption(hp, jsonKey));
            }
            return loadout;
        }
        private static WeaponMount RandomOption(HardpointSet hp, string jsonKey)
        {
            if (hp.weaponOptions == null || hp.weaponOptions.Count == 0)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {jsonKey} station '{hp.name}' lists no weapon options; leaving it empty.");
                return null;
            }
            WeaponMount picked = null;
            int seen = 0;
            for (int i = 0; i < hp.weaponOptions.Count; i++)
            {
                WeaponMount option = hp.weaponOptions[i];
                if (option == null) continue;
                seen++;
                if (Random.Range(0, seen) == 0) picked = option;
            }
            if (picked == null)
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] {jsonKey} station '{hp.name}' has only empty options; leaving it empty.");
            }
            return picked;
        }
        private static bool Contains(string[] names, string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == name) return true;
            }
            return false;
        }
    }
}