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
            float distIbis      = GetClosestSpawnBaseDistance(hq, requester, "UtilityHelo1",   out Airbase ibisBase,      wetOnly: isWet);
            float distTarantula = GetClosestSpawnBaseDistance(hq, requester, "QuadVTOL1",       out Airbase tarantulaBase, wetOnly: isWet);
            float distChimera   = GetClosestSpawnBaseDistance(hq, requester, "Aryx_CargoPlane1", out Airbase chimeraBase);
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
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Ibis limit reached, skipping for '{requester.unitName}'.");
            if (distTarantula < thresholdB && tarantulaBase != null && tarantulaLimit)
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Tarantula limit reached, skipping for '{requester.unitName}'.");
            bool use3xFallback = false;
            if (tarantulaBase != null)
            {
                if (chimeraBase == null) use3xFallback = true;
                else if (distTarantula * 3f < distChimera) use3xFallback = true;
            }
            if (use3xFallback)
            {
                if (Plugin.IsResupplyLimitReached(hq, "QuadVTOL1", isWet, false)) return false;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] 3x fallback: Tarantula ({distTarantula:F0}m) vs Chimera ({(chimeraBase != null ? distChimera.ToString("F0") : "N/A")}m). Spawning Tarantula for '{requester.unitName}'.");
                Plugin.MarkExtendedZoneTarget(requester);
                return TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula);
            }
            if (chimeraBase != null)
            {
                if (Plugin.IsResupplyLimitReached(hq, "Aryx_CargoPlane1", isWet, true)) return false;
                return EnqueueChimeraRequest(hq, requester, isWet, distChimera);
            }
            Plugin.Log.LogInfo($"[SupplyBuffetMod] No valid resupply base found for '{requester.unitName}'. Skipping.");
            return false;
        }
        private static float GetClosestSpawnBaseDistance(
            FactionHQ hq, Unit requester, string jsonKey,
            out Airbase closestBase, bool wetOnly = false)
        {
            closestBase = null;
            float minDist = float.MaxValue;
            if (FactionRegistry.airbaseLookup == null) return minDist;
            AircraftDefinition spawnDef = GetDefinition(jsonKey);
            if (spawnDef == null) return minDist;
            foreach (var ab in FactionRegistry.airbaseLookup.Values)
            {
                if (ab == null || !ab.isActiveAndEnabled) continue;
                if (ab.CurrentHQ != hq && (ab.CurrentHQ == null || ab.CurrentHQ.faction != hq.faction)) continue;
                if (wetOnly && !IsWetSpawnBase(ab)) continue;
                if (!ab.CanSpawnAircraft(spawnDef)) continue;
                float d = Vector3.Distance(ab.transform.position, requester.transform.position);
                if (d < minDist) { minDist = d; closestBase = ab; }
            }
            return minDist;
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
                if (Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawn interval active for '{requester.unitName}' ({ResupplyCensus.SpawnIntervalRemaining(hq):F0}s left); deferring {spawnDef.unitName}.");
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
            ResupplyCensus.RegisterDispatch(hq, jsonKey, isWet);
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
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawned {spawnDef.unitName} ({bestLoadout.Name}) at {spawnBase.gameObject.name} for {(isWet ? "ship" : "ground")} '{requester.unitName}'. Dist: {dist:F0}m.");
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
            Loadout loadout = ChimeraHelper.CreateDynamicLoadout(requester, isWet, out string loadoutName, out string _);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Requesting {(isWet ? "Wet" : "Dry")} Chimera for '{requester.unitName}' (Loadout: {loadoutName}, Dist: {dist:F0}m).");
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