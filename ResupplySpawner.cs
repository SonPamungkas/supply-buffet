// ============================================================================
// FILE: ResupplySpawner.cs
// PURPOSE: Core unified air-resupply spawning engine for SupplyBuffetMod.
//
// TRIGGERS:
//   - Called by SupplyRunDry.SpawnAirResupply when a ground unit needs rearming.
//   - Called by SupplyRunWet.HandleNavalResupply when a naval unit needs rearming.
//
// EFFECTS:
//   - Calculates distances between the requester and friendly airbases.
//   - Evaluates ThresholdA/ThresholdB and the 3x nearest-naval-airbase fallback rule.
//   - Respects active unit limits for Ibis (UtilityHelo1), Tarantula (QuadVTOL1),
//     and Chimera (Aryx_CargoPlane1).
//   - Spawns the appropriate aircraft or enqueues a Chimera spawn request.
//   - Safely increments and reverts reserve counters to prevent reserve ballooning.
//
// AIRCRAFT SELECTION LOGIC:
//   Wet resupply (naval unit requesting):
//     - Ibis / Tarantula spawn from any allied NAVAL airbase (ship with helipad).
//     - Chimera is skipped (not applicable for wet).
//
//   Dry resupply (ground unit requesting):
//     - Ibis / Tarantula spawn from ANY allied airbase (land OR naval).
//     - If no base within threshold, fall to Chimera from medium hangar.
//     - 3x fallback: if the nearest Chimera hangar is >3x further than the nearest
//       naval airbase that can spawn Tarantula, spawn Tarantula from that naval base.
// ============================================================================

using System;
using System.Collections.Generic;
using UnityEngine;
using NuclearOption.SavedMission;

namespace SupplyBuffetMod
{
    public static class ResupplySpawner
    {
        // Lazily-built jsonKey -> definition cache. The old code ran a LINQ
        // FirstOrDefault over Encyclopedia.i.aircraft three times per request
        // (plus once more when spawning), allocating a closure and enumerator each time.
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

        // -----------------------------------------------------------------------
        // PUBLIC ENTRY POINT
        // -----------------------------------------------------------------------

        /// <summary>
        /// Evaluate and trigger an air resupply mission for a requesting unit.
        /// </summary>
        public static void TriggerResupply(FactionHQ hq, Unit requester, bool isWet)
        {
            if (hq == null || requester == null) return;
            // Spawning goes through [Server] methods that throw on a client.
            if (!ChimeraSpawnQueue.IsServerAuthority()) return;

            float thresholdA = Plugin.ThresholdA.Value;
            float thresholdB = Plugin.ThresholdB.Value;

            // Wet resupply: helicopters MUST spawn from a Helipad or Atlas (Aryx_SupplyShip1) only.
            // Dry resupply: helicopters may spawn from any allied airbase — land OR any ship.
            float distIbis      = GetClosestSpawnBaseDistance(hq, requester, "UtilityHelo1",   out Airbase ibisBase,      wetOnly: isWet);
            float distTarantula = GetClosestSpawnBaseDistance(hq, requester, "QuadVTOL1",       out Airbase tarantulaBase, wetOnly: isWet);
            float distChimera   = GetClosestSpawnBaseDistance(hq, requester, "Aryx_CargoPlane1", out Airbase chimeraBase);

            // --- Step 1: Ibis if within ThresholdA ---
            if (distIbis < thresholdA && ibisBase != null)
            {
                if (Plugin.IsResupplyLimitReached(hq, "UtilityHelo1", isWet, false))
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Ibis limit reached, skipping for '{requester.unitName}'.");
                    return;
                }
                TrySpawnAircraftAtBase(hq, requester, "UtilityHelo1", isWet, ibisBase, distIbis);
                return;
            }

            // --- Step 2: Tarantula if within ThresholdB ---
            if (distTarantula < thresholdB && tarantulaBase != null)
            {
                if (Plugin.IsResupplyLimitReached(hq, "QuadVTOL1", isWet, false))
                {
                    Plugin.Log.LogInfo($"[SupplyBuffetMod] Tarantula limit reached, skipping for '{requester.unitName}'.");
                    return;
                }
                TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula);
                return;
            }

            // --- Step 3: Chimera / 3x fallback ---
            // If the closest valid Tarantula base distance * 3 < closest Chimera base, prefer Tarantula.
            // Also fallback to Tarantula if there's no Chimera base at all.
            bool use3xFallback = false;
            if (tarantulaBase != null)
            {
                if (chimeraBase == null) use3xFallback = true;
                else if (distTarantula * 3f < distChimera) use3xFallback = true;
            }

            if (use3xFallback)
            {
                if (Plugin.IsResupplyLimitReached(hq, "QuadVTOL1", isWet, false)) return;
                Plugin.Log.LogInfo($"[SupplyBuffetMod] 3x fallback: Tarantula ({distTarantula:F0}m) vs Chimera ({(chimeraBase != null ? distChimera.ToString("F0") : "N/A")}m). Spawning Tarantula for '{requester.unitName}'.");
                Plugin.MarkExtendedZoneTarget(requester);
                TrySpawnAircraftAtBase(hq, requester, "QuadVTOL1", isWet, tarantulaBase, distTarantula);
            }
            else if (chimeraBase != null)
            {
                if (Plugin.IsResupplyLimitReached(hq, "Aryx_CargoPlane1", isWet, true)) return;
                EnqueueChimeraRequest(hq, requester, isWet, distChimera);
            }
            else
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] No valid resupply base found for '{requester.unitName}'. Skipping.");
            }
        }

        // -----------------------------------------------------------------------
        // BASE DISTANCE QUERIES
        // -----------------------------------------------------------------------

        /// <summary>
        /// Finds the closest allied airbase capable of spawning the given aircraft type.
        /// </summary>
        /// <param name="wetOnly">
        ///   When true, restricts results to Helipad or Atlas (Aryx_SupplyShip1) only.
        ///   Wet resupply helicopters must not spawn from any other ship or land base.
        ///   When false (default), any allied airbase qualifies — land or any ship.
        /// </param>
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

        /// <summary>
        /// WET RESUPPLY filter — restricts to Helipad or Atlas (Aryx_SupplyShip1) only.
        /// Wet (naval) resupply helicopters must NOT spawn from carriers or other ships.
        /// Only a dedicated helipad or the Atlas supply ship is a valid wet spawn origin.
        /// </summary>
        private static bool IsWetSpawnBase(Airbase ab)
        {
            if (ab == null || ab.gameObject == null) return false;
            string n = ab.gameObject.name;
            return n.IndexOf("Helipad",    StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("SupplyShip", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Atlas",      StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // -----------------------------------------------------------------------
        // SPAWN HELPERS
        // -----------------------------------------------------------------------

        /// <summary>
        /// Spawns an Ibis or Tarantula at the given base with a suitable resupply loadout.
        /// Safely manages reserve accounting (+1 before spawn, -1 on failure).
        /// </summary>
        private static void TrySpawnAircraftAtBase(
            FactionHQ hq, Unit requester, string jsonKey,
            bool isWet, Airbase spawnBase, float dist)
        {
            AircraftDefinition spawnDef = GetDefinition(jsonKey);
            if (spawnDef == null || spawnBase == null) return;

            StandardLoadout bestLoadout = GetBestStandardLoadout(spawnDef, isWet);
            if (bestLoadout == null)
            {
                Plugin.Log.LogWarning($"[SupplyBuffetMod] No valid {(isWet ? "naval" : "ground")} loadout for {spawnDef.unitName}.");
                return;
            }

            hq.AddSupplyUnit(spawnDef, 1);
            int livery = spawnDef.aircraftParameters.GetRandomLiveryForFaction(hq.faction);
            var result = spawnBase.TrySpawnAircraft(null, spawnDef, new LiveryKey(livery), bestLoadout.loadout, bestLoadout.FuelRatio);

            if (!result.Allowed)
            {
                hq.AddSupplyUnit(spawnDef, -1);
                Plugin.Log.LogWarning($"[SupplyBuffetMod] TrySpawnAircraft denied for {spawnDef.unitName} at {spawnBase.gameObject.name}. Reserve reverted.");
            }
            else
            {
                Plugin.Log.LogInfo($"[SupplyBuffetMod] Spawned {spawnDef.unitName} ({bestLoadout.Name}) at {spawnBase.gameObject.name} for {(isWet ? "ship" : "ground")} '{requester.unitName}'. Dist: {dist:F0}m.");
            }
        }

        /// <summary>
        /// Files a Chimera spawn request. ChimeraSpawnQueue tries to satisfy it on the
        /// spot and only queues it when every hangar is busy.
        /// </summary>
        private static void EnqueueChimeraRequest(FactionHQ hq, Unit requester, bool isWet, float dist)
        {
            var chimeraDef = ChimeraHelper.GetChimeraDefinition();
            if (chimeraDef == null)
            {
                Plugin.Log.LogWarning("[SupplyBuffetMod] Chimera definition (Aryx_CargoPlane1) not found.");
                return;
            }

            Loadout loadout = ChimeraHelper.CreateDynamicLoadout(isWet, out string loadoutName);
            Plugin.Log.LogInfo($"[SupplyBuffetMod] Requesting {(isWet ? "Wet" : "Dry")} Chimera for '{requester.unitName}' (Loadout: {loadoutName}, Dist: {dist:F0}m).");

            ChimeraSpawnQueue.Request(hq, requester, chimeraDef, loadout, loadoutName, isWet);
        }

        /// <summary>
        /// Picks the best standard loadout from an aircraft definition for the
        /// requested mission type (ground = rearmGround, naval = rearmShip).
        /// Prefers loadouts whose name contains "supply" or "heavy".
        /// </summary>
        private static StandardLoadout GetBestStandardLoadout(AircraftDefinition def, bool isWet)
        {
            if (def?.aircraftParameters?.StandardLoadouts == null) return null;

            StandardLoadout best = null;
            foreach (var sl in def.aircraftParameters.StandardLoadouts)
            {
                if (sl?.loadout?.weapons == null) continue;

                bool matches = false;
                foreach (var mount in sl.loadout.weapons)
                {
                    if (mount?.info == null) continue;
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
