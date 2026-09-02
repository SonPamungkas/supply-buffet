
using System;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    public static class ResupplySpawner
    {
        private static readonly Dictionary<string, AircraftDefinition> _defCache =
            new Dictionary<string, AircraftDefinition>(StringComparer.Ordinal);
        private static readonly Dictionary<string, float> _lastNoBaseLog =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private const float NO_BASE_LOG_INTERVAL = 30f;
        private static AircraftDefinition GetDefinition(string jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey)) return null;
            if (_defCache.TryGetValue(jsonKey, out AircraftDefinition cached)) return cached;
            if (Encyclopedia.i == null || Encyclopedia.i.aircraft == null) return null;
            AircraftDefinition found = null;
            foreach (AircraftDefinition a in Encyclopedia.i.aircraft)
            {
                if (a != null && a.jsonKey == jsonKey) { found = a; break; }
            }
            if (found != null) _defCache[jsonKey] = found;
            return found;
        }
        public static bool TriggerResupply(FactionHQ hq, Unit requester, bool isWet)
        {
            if (hq == null || requester == null) return false;
            if (!ChimeraSpawnQueue.IsServerAuthority()) return false;
            float thresholdA = Plugin.ThresholdA.Value;
            float thresholdB = Plugin.ThresholdB.Value;
            FindResupplyCandidates(hq, requester, isWet,
                out float distIbis, out Airbase ibisBase,
                out float distTarantula, out Airbase tarantulaBase,
                out float distChimera, out Airbase chimeraBase);
            if (Step3_TryHelicopters(hq, requester, isWet, thresholdA, thresholdB, distIbis, ibisBase, distTarantula, tarantulaBase))
                return true;
            return Step4_TryChimera(hq, requester, isWet, thresholdB, distTarantula, tarantulaBase, distChimera, chimeraBase);
        }
        private static bool Step3_TryHelicopters(FactionHQ hq, Unit requester, bool isWet, float thresholdA, float thresholdB,
            float distIbis, Airbase ibisBase, float distTarantula, Airbase tarantulaBase)
        {
            bool ibisLimit = Plugin.IsResupplyLimitReached(hq, "UtilityHelo1", isWet, false);
            bool tarantulaLimit = Plugin.IsResupplyLimitReached(hq, "QuadVTOL1", isWet, false);
            bool ibisValid = distIbis < thresholdA && ibisBase != null && !ibisLimit;
            bool tarantulaValid = distTarantula < thresholdB && tarantulaBase != null && !tarantulaLimit;
            if (ibisValid && tarantulaValid)
            {
                if (distTarantula < distIbis && distTarantula < thresholdA)
                {
                    if (TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula)) return true;
                    if (TrySpawnAircraftAtBase(hq, requester, "UtilityHelo1", isWet, ibisBase, distIbis)) return true;
                }
                else
                {
                    if (TrySpawnAircraftAtBase(hq, requester, "UtilityHelo1", isWet, ibisBase, distIbis)) return true;
                    if (TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula)) return true;
                }
            }
            else if (ibisValid)
            {
                if (TrySpawnAircraftAtBase(hq, requester, "UtilityHelo1", isWet, ibisBase, distIbis)) return true;
            }
            else if (tarantulaValid)
            {
                if (TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula)) return true;
            }
            if (distIbis < thresholdA && ibisBase != null && ibisLimit)
                Plugin.Log.LogInfo($"[SB|P5] Ibis limit reached, skipping for '{requester.unitName}'.");
            if (distTarantula < thresholdB && tarantulaBase != null && tarantulaLimit)
                Plugin.Log.LogInfo($"[SB|P5] Tarantula limit reached, skipping for '{requester.unitName}'.");
            return false;
        }
        private static bool Step4_TryChimera(FactionHQ hq, Unit requester, bool isWet, float thresholdB,
            float distTarantula, Airbase tarantulaBase, float distChimera, Airbase chimeraBase)
        {
            bool advancedFallbackEnabled = Plugin.Advanced3xFallbackEnabled == null || Plugin.Advanced3xFallbackEnabled.Value;
            bool use3xFallback = advancedFallbackEnabled
                && Advanced.TarantulaChimeraFallback.ShouldPreferTarantula(distTarantula, tarantulaBase, distChimera, chimeraBase);
            if (use3xFallback)
            {
                if (Plugin.IsResupplyLimitReached(hq, "QuadVTOL1", isWet, false)) return false;
                Plugin.Log.LogInfo($"[SB|P5] 3x fallback: Tarantula ({distTarantula:F0}m) vs Chimera ({(chimeraBase != null ? distChimera.ToString("F0") : "N/A")}m). Spawning Tarantula for '{requester.unitName}'.");
                Plugin.MarkExtendedZoneTarget(requester);
                return TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula);
            }
            if (chimeraBase != null)
            {
                if (!isWet && distChimera < thresholdB)
                {
                    Plugin.Log.LogInfo($"[SB|P5] '{requester.unitName}' is {distChimera:F0}m from the nearest Chimera base, inside ThresholdB ({thresholdB:F0}m) - too short for a Chimera run; leaving it to the helos.");
                    return false;
                }
                if (Plugin.IsResupplyLimitReached(hq, "Aryx_CargoPlane1", isWet, true)) return false;
                return EnqueueChimeraRequest(hq, requester, isWet, distChimera);
            }
            Plugin.Log.LogInfo($"[SB|P5] No valid resupply base found for '{requester.unitName}'. Skipping.");
            return false;
        }
        private static void FindResupplyCandidates(FactionHQ hq, Unit requester, bool isWet,
            out float distIbis, out Airbase ibisBase,
            out float distTarantula, out Airbase tarantulaBase,
            out float distChimera, out Airbase chimeraBase)
        {
            distIbis = float.MaxValue; ibisBase = null;
            distTarantula = float.MaxValue; tarantulaBase = null;
            distChimera = float.MaxValue; chimeraBase = null;
            if (hq.faction == null) return;
            AircraftDefinition defIbis = GetDefinition("UtilityHelo1");
            AircraftDefinition defTarantula = GetDefinition("QuadVTOL1");
            AircraftDefinition defChimera = GetDefinition("Aryx_CargoPlane1");
            List<string> refusedIbis = null;
            List<string> refusedTarantula = null;
            List<string> refusedChimera = null;
            foreach (var ab in AirbaseFactionCache.GetFactionAirbases(hq.faction))
            {
                if (ab == null || !ab.isActiveAndEnabled) continue;
                bool wetOk = !isWet || IsWetSpawnBase(ab);
                float d = Vector3.Distance(ab.transform.position, requester.transform.position);
                if (defIbis != null)
                {
                    if (!wetOk) Refuse(ref refusedIbis, ab, "not a wet spawn base (name has no Helipad/SupplyShip/Atlas)");
                    else if (!ab.CanSpawnAircraft(defIbis)) Refuse(ref refusedIbis, ab, "CanSpawnAircraft refused UtilityHelo1");
                    else if (d < distIbis) { distIbis = d; ibisBase = ab; }
                }
                if (defTarantula != null)
                {
                    if (!wetOk) Refuse(ref refusedTarantula, ab, "not a wet spawn base (name has no Helipad/SupplyShip/Atlas)");
                    else if (!ab.CanSpawnAircraft(defTarantula)) Refuse(ref refusedTarantula, ab, "CanSpawnAircraft refused QuadVTOL1");
                    else if (d < distTarantula) { distTarantula = d; tarantulaBase = ab; }
                }
                if (defChimera != null)
                {
                    if (!ab.CanSpawnAircraft(defChimera)) Refuse(ref refusedChimera, ab, "CanSpawnAircraft refused Aryx_CargoPlane1");
                    else if (d < distChimera) { distChimera = d; chimeraBase = ab; }
                }
            }
            LogNoSpawnBase("UtilityHelo1", isWet, ibisBase, refusedIbis);
            LogNoSpawnBase("QuadVTOL1", isWet, tarantulaBase, refusedTarantula);
            LogNoSpawnBase("Aryx_CargoPlane1", false, chimeraBase, refusedChimera);
        }
        private static void LogNoSpawnBase(string jsonKey, bool wetOnly, Airbase found, List<string> refused)
        {
            if (found != null || refused == null) return;
            string key = jsonKey + "|" + wetOnly;
            float now = Time.timeSinceLevelLoad;
            if (!_lastNoBaseLog.TryGetValue(key, out float last) || now - last >= NO_BASE_LOG_INTERVAL)
            {
                _lastNoBaseLog[key] = now;
                Plugin.Log.LogInfo($"[SB|P7] No spawn base for {jsonKey} (wetOnly={wetOnly}): {string.Join("; ", refused)}");
            }
        }
        private static void Refuse(ref List<string> refused, Airbase ab, string why)
        {
            if (refused == null) refused = new List<string>();
            if (refused.Count >= 12) return;
            string name = (ab != null && ab.gameObject != null) ? ab.gameObject.name : "<null>";
            refused.Add($"{name} - {why}");
        }
        private static bool IsWetSpawnBase(Airbase ab)
        {
            if (ab == null || ab.gameObject == null) return false;
            string n = ab.gameObject.name;
            return n.IndexOf("Helipad",    StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("SupplyShip", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Atlas",      StringComparison.OrdinalIgnoreCase) >= 0;
        }
        private static bool TrySpawnAircraftAtBase(
            FactionHQ hq, Unit requester, string jsonKey,
            bool isWet, Airbase spawnBase, float dist)
        {
            AircraftDefinition spawnDef = GetDefinition(jsonKey);
            if (spawnDef == null || spawnBase == null) return false;
            if (!ResupplyCensus.CanSpawnNow(hq))
            {
                if (Plugin.Dbg)
                {
                    Plugin.Log.LogInfo($"[SB|P6] Spawn interval active for '{requester.unitName}' ({ResupplyCensus.SpawnIntervalRemaining(hq):F0}s left); deferring {spawnDef.unitName}.");
                }
                return false;
            }
            string preferredMount = null;
            if (ChimeraHelper.IsDrivingToRestock(requester))
            {
                preferredMount = (jsonKey == "UtilityHelo1") ? "MunitionsPallet1x1" : "MunitionsContainerx1";
            }
            StandardLoadout bestLoadout = GetBestStandardLoadout(spawnDef, isWet, preferredMount);
            if (bestLoadout == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid {(isWet ? "naval" : "ground")} loadout for {spawnDef.unitName}.");
                return false;
            }
            hq.AddSupplyUnit(spawnDef, 1);
            ResupplyCensus.RegisterDispatch(hq, jsonKey, isWet, spawnBase);
            int livery = spawnDef.aircraftParameters.GetRandomLiveryForFaction(hq.faction);
            var result = spawnBase.TrySpawnAircraft(null, spawnDef, new LiveryKey(livery), bestLoadout.loadout, bestLoadout.FuelRatio);
            if (!result.Allowed)
            {
                hq.AddSupplyUnit(spawnDef, -1);
                ResupplyCensus.CancelDispatch(hq, jsonKey, isWet);
                Plugin.Log.LogWarning($"[SupplyBuffetMod] TrySpawnAircraft denied for {spawnDef.unitName} at {spawnBase.gameObject.name}. Reserve reverted.");
                return false;
            }
            ResupplyCensus.MarkSpawned(hq);
            Plugin.Log.LogInfo($"[SB|P6] Spawned {spawnDef.unitName} ({bestLoadout.Name}) at {spawnBase.gameObject.name} for {(isWet ? "ship" : "ground")} '{requester.unitName}'. Dist: {dist:F0}m.");
            return true;
        }
        private static bool EnqueueChimeraRequest(FactionHQ hq, Unit requester, bool isWet, float dist)
        {
            var chimeraDef = ChimeraHelper.GetChimeraDefinition();
            if (chimeraDef == null)
            {
                Plugin.Log.LogWarning("[SupplyBuffetMod] Chimera definition (Aryx_CargoPlane1) not found.");
                return false;
            }
            int sortieIndex = isWet ? 0 : SortieParity.Next(hq, ChimeraHelper.DryCategoryFor(requester));
            Loadout loadout = ChimeraHelper.CreateDynamicLoadout(requester, isWet, sortieIndex, out string loadoutName, out string _);
            Plugin.Log.LogInfo($"[SB|P5] Requesting {(isWet ? "Wet" : "Dry")} Chimera for '{requester.unitName}' (Loadout: {loadoutName}, Dist: {dist:F0}m).");
            return ChimeraSpawnQueue.Request(hq, requester, chimeraDef, loadout, loadoutName, isWet);
        }
        private static StandardLoadout GetBestStandardLoadout(AircraftDefinition def, bool isWet, string preferredMountKey = null)
        {
            if (def == null || def.aircraftParameters == null
                || def.aircraftParameters.StandardLoadouts == null) return null;
            if (!string.IsNullOrEmpty(preferredMountKey))
            {
                foreach (var sl in def.aircraftParameters.StandardLoadouts)
                {
                    if (sl?.loadout?.weapons == null) continue;
                    foreach (var mount in sl.loadout.weapons)
                    {
                        if (mount != null && mount.jsonKey == preferredMountKey) return sl;
                    }
                }
                Plugin.Log.LogInfo($"[SupplyBuffetMod] No {def.unitName} standard loadout carries '{preferredMountKey}'; falling back to the default supply loadout.");
            }
            StandardLoadout best = null;
            foreach (var sl in def.aircraftParameters.StandardLoadouts)
            {
                if (sl?.loadout?.weapons == null) continue;
                bool matches = false;
                foreach (var mount in sl.loadout.weapons)
                {
                    if (mount == null || mount.info == null) continue;
                    if (isWet  && mount.info.rearmShip)   { matches = true; break; }
                    if (!isWet && mount.info.rearmGround)  { matches = true; break; }
                }
                if (!matches) continue;
                best = sl;
                if (sl.Name.IndexOf("supply", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sl.Name.IndexOf("heavy",  StringComparison.OrdinalIgnoreCase) >= 0)
                    break;
            }
            return best;
        }
    }
}