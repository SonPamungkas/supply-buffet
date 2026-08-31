using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using NuclearOption.SavedMission;
namespace SupplyBuffetMod
{
    [BepInPlugin("neutral.supplybuffet", "SupplyBuffetMod", "2.1.5")]
    public class Plugin : BaseUnityPlugin
    {
        public static Plugin Instance;
        public static ManualLogSource Log;
        public static ConfigEntry<bool> ResupplyEnabled;
        public static ConfigEntry<bool> LocalAirbaseRepairEnabled;
        public static ConfigEntry<bool> InterbaseRepairEnabled;
        public static ConfigEntry<bool> HeavyRepairEnabled;
        public static ConfigEntry<float> AirbaseRepairCooldown;
        public static ConfigEntry<int> ActiveLocalRepairLimit;
        public static ConfigEntry<int> ActiveInterbaseRepairLimit;
        public static ConfigEntry<int> ActiveHeavyRepairLimit;
        public static ConfigEntry<int> ActiveIbisLimitDryConfig;
        public static ConfigEntry<int> ActiveIbisLimitWetConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitDryConfig;
        public static ConfigEntry<int> ActiveTarantulaLimitWetConfig;
        public static ConfigEntry<int> ActiveChimeraLimitDryConfig;
        public static ConfigEntry<int> ActiveChimeraLimitWetConfig;
        public static ConfigEntry<float> SpawnInterval;
        public static ConfigEntry<float> ThresholdA;
        public static ConfigEntry<float> ThresholdB;
        public static ConfigEntry<float> UnitCooldown;
        public static ConfigEntry<float> ReserviceCooldown;
        public static ConfigEntry<bool> TopUpUntilFull;
        public static ConfigEntry<bool> AllowNuclearFieldRearm;
        public static ConfigEntry<float> ChimeraCruiseAltitude;
        public static ConfigEntry<float> ChimeraDescentDistance;
        public static ConfigEntry<float> ChimeraReleaseInterval;
        public static ConfigEntry<float> PostDropHoldBase;
        public static ConfigEntry<float> PostDropHoldPerCrate;
        public static ConfigEntry<float> ApproachSpeedFloor;
        public static ConfigEntry<float> NavalDropAltitude;
        public static ConfigEntry<float> DescentHandoffAltitude;
        public static ConfigEntry<float> PatternBankLimit;
        public static ConfigEntry<float> TransitBankLimit;
        public static ConfigEntry<float> DryPreferredRadarAltitude;
        public static ConfigEntry<float> ClusterCoverageGrace;
        public static ConfigEntry<float> DryFailsafeRollLimit;
        public static ConfigEntry<float> DryMovingTargetLeadCap;
        public static ConfigEntry<float> LandingRetrySeconds;
        public static ConfigEntry<float> DryTurnCornerSpeedFraction;
        public static ConfigEntry<float> DryRunInHandoff;
        public static ConfigEntry<bool> RTBOnDamage;
        public static ConfigEntry<float> RTBHitCount;
        public static ConfigEntry<float> RTBEngineHitCount;
        public static ConfigEntry<float> RTBMinHitDamage;
        public static ConfigEntry<bool> RTBDetachTriggers;
        public static ConfigEntry<bool> RTBEngineLossTriggers;
        public static ConfigEntry<bool> FreeSlotOnLanding;
        public static ConfigEntry<float> MaxResupplyFlightSeconds;
        public static ConfigEntry<float> ChimeraAirdropMaxRoll;
        public static ConfigEntry<float> ChimeraAirdropMaxVerticalSpeed;
        public static ConfigEntry<float> ChimeraAirdropMaxCrossTrack;
        public static ConfigEntry<int> ChimeraAirdropMaxAttempts;
        public static ConfigEntry<int> MaxRunAttempts;
        public static ConfigEntry<bool> JammerEnabled;
        public static ConfigEntry<float> JammerRange;
        public static ConfigEntry<int> FlareBurstCount;
        public static ConfigEntry<float> FlareBurstInterval;
        public static ConfigEntry<bool> ChimeraUseRunwayDelivery;
        public static ConfigEntry<float> ChimeraRunwayDropAltitude;
        public static ConfigEntry<float> ChimeraRunwayDropTolerance;
        public static ConfigEntry<float> ChimeraRunwayDescentDistance;
        public static ConfigEntry<float> ChimeraRunwayMinReleaseSpeed;
        public static ConfigEntry<float> ChimeraRunwayMaxReleaseSpeed;
        public static ConfigEntry<float> ChimeraRunwayMaxRoll;
        public static ConfigEntry<float> ChimeraRunwayMaxVerticalSpeed;
        public static ConfigEntry<float> MunitionsPalletCapacity;
        public static ConfigEntry<bool> MunitionsPalletSingleUse;
        public static ConfigEntry<float> MunitionsPallet2Capacity;
        public static ConfigEntry<bool> MunitionsPallet2SingleUse;
        public static ConfigEntry<float> NavalPalletCapacity;
        public static ConfigEntry<bool> NavalPalletSingleUse;
        public static ConfigEntry<float> MunitionsContainerCapacity;
        public static ConfigEntry<bool> MunitionsContainerSingleUse;
        public static ConfigEntry<float> NavalContainerCapacity;
        public static ConfigEntry<bool> NavalContainerSingleUse;
        public static ConfigEntry<float> MunitionsPalletRadius;
        public static ConfigEntry<float> MunitionsPallet2Radius;
        public static ConfigEntry<float> NavalPalletRadius;
        public static ConfigEntry<float> MunitionsContainerRadius;
        public static ConfigEntry<float> NavalContainerRadius;
        public static ConfigEntry<bool> FullRestoreMunitionsPallet1;
        public static ConfigEntry<bool> FullRestoreMunitionsPallet2;
        public static ConfigEntry<bool> FullRestoreNavalPallet1;
        public static ConfigEntry<bool> FullRestoreMunitionsContainer1;
        public static ConfigEntry<bool> FullRestoreNavalSupplyContainer1;
        public static ConfigEntry<bool> ExcludeLogisticsFromAILimit;
        public static ConfigEntry<bool> ExpressRearmEnabled;
        public static ConfigEntry<bool> ExpressRearmGroundEnabled;
        public static ConfigEntry<float> RearmRequestSensitivity;
        public static ConfigEntry<float> RestampInterval;
        public static ConfigEntry<float> StampThrottle;
        public static ConfigEntry<bool> DebugLogging;
        public static bool Dbg => DebugLogging != null && DebugLogging.Value;
        public static float Cfg(ConfigEntry<float> entry, float fallback) => (entry != null) ? entry.Value : fallback;
        public static bool Cfg(ConfigEntry<bool> entry, bool fallback) => (entry != null) ? entry.Value : fallback;
        public static int Cfg(ConfigEntry<int> entry, int fallback) => (entry != null) ? entry.Value : fallback;
        private static readonly ConditionalWeakTable<Unit, object> ExtendedZoneTable = new ConditionalWeakTable<Unit, object>();
        public static void MarkExtendedZoneTarget(Unit unit)
        {
            if (unit != null) ExtendedZoneTable.GetValue(unit, _ => new object());
        }
        public static bool IsExtendedZoneTarget(Unit unit)
        {
            return unit != null && ExtendedZoneTable.TryGetValue(unit, out _);
        }
        private float _lastMonitorTime = 0f;
        private float _lastLevelTime = 0f;
        private const float MONITOR_INTERVAL = 5f; 
        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo("[SupplyBuffetMod] Plugin v2.1.5 initializing...");
            BindConfigs();
            Harmony harmony = new Harmony("com.neutral.supplybuffet");
            harmony.PatchAll();
            Patches_AryxChimera.TryApply(harmony);
            Log.LogInfo("[SupplyBuffetMod] v2.1.5 loaded successfully.");
        }
        private void BindConfigs()
        {
            const string S_RESUPPLY = "Resupply";
            ResupplyEnabled = Config.Bind(S_RESUPPLY, "Enabled", true, "Master switch for ammunition resupply dispatch. Disabling it stops new sorties being sent; a transport already airborne finishes its delivery and returns.");
            const string S_REPAIR = "Repair";
            LocalAirbaseRepairEnabled = Config.Bind(S_REPAIR, "LocalAirbaseRepair", false, "Let an airbase repair its own damaged buildings with an Ibis that never leaves the field.");
            InterbaseRepairEnabled = Config.Bind(S_REPAIR, "InterbaseRepair", false, "Let an Ibis or Tarantula fly a repair dozer in from a different friendly base or ship.");
            HeavyRepairEnabled = Config.Bind(S_REPAIR, "HeavyRepair", false, "Let a Chimera fly a repair dozer when no helicopter base is within its threshold.");
            AirbaseRepairCooldown = Config.Bind(S_REPAIR, "Cooldown", 600f, "PER AIRBASE or outpost group. Seconds before another repair aircraft may be sent to the same place. The repair-side equivalent of UnitCooldown, and much longer because a base takes far longer to consume what it is brought.");
            ActiveLocalRepairLimit = Config.Bind(S_REPAIR, "ActiveLocalRepairLimit", 1, "Max concurrent local airbase repair flights per faction.");
            ActiveInterbaseRepairLimit = Config.Bind(S_REPAIR, "ActiveInterbaseRepairLimit", 1, "Max concurrent interbase repair flights per faction.");
            ActiveHeavyRepairLimit = Config.Bind(S_REPAIR, "ActiveHeavyRepairLimit", 1, "Max concurrent heavy (Chimera) repair flights per faction.");
            const string S_LIMITS = "ActiveLimits";
            ActiveIbisLimitDryConfig = Config.Bind(S_LIMITS, "ActiveIbisLimitDry", 2, "Max number of active UtilityHelo1 doing dry resupply");
            ActiveIbisLimitWetConfig = Config.Bind(S_LIMITS, "ActiveIbisLimitWet", 1, "Max number of active UtilityHelo1 doing wet resupply");
            ActiveTarantulaLimitDryConfig = Config.Bind(S_LIMITS, "ActiveTarantulaLimitDry", 2, "Max number of active QuadVTOL1 doing dry resupply");
            ActiveTarantulaLimitWetConfig = Config.Bind(S_LIMITS, "ActiveTarantulaLimitWet", 1, "Max number of active QuadVTOL1 doing wet resupply");
            ActiveChimeraLimitDryConfig = Config.Bind(S_LIMITS, "ActiveChimeraLimitDry", 1, "Max number of active Aryx_CargoPlane1 doing dry resupply");
            ActiveChimeraLimitWetConfig = Config.Bind(S_LIMITS, "ActiveChimeraLimitWet", 1, "Max number of active Aryx_CargoPlane1 doing wet resupply");
            ClusterCoverageGrace = Config.Bind(S_LIMITS, "ClusterCoverageGraceSeconds", 90f, "How long a unit already inside a stocked rearmer's radius is passed over before it becomes eligible for its own air delivery anyway, in seconds. This is what stops six units parked together costing six sorties: the first delivery lands, the crate registers as a rearmer, and the rest are covered by it. The radius is never ours - it is each rearmer's own Range, so munitions bunkers, ammo dumps, supply ships, supply vehicles and modded rearmers all count automatically with no entry naming them. The grace period exists because coverage is not the same as service: a hull in range of a rearmer that cannot actually reach it would otherwise be starved for the whole mission. 0 disables the grace and suppresses for as long as the coverage lasts.");
            SpawnInterval = Config.Bind(S_LIMITS, "SpawnIntervalSeconds", 60f, "PER FACTION, all airframes. Minimum seconds between resupply transport launches, whichever unit they are for. Throttles the fleet as a whole where UnitCooldown throttles one recipient. Raising it thins traffic everywhere; 0 disables.");
            FreeSlotOnLanding = Config.Bind(S_LIMITS, "FreeSlotOnLanding", true, "Stop counting a resupply transport against the active limit once it begins its landing pattern. Roughly halves turnaround, at the cost of more aircraft airborne at once. Disable to hold the slot until the aircraft is fully parked.");
            MaxResupplyFlightSeconds = Config.Bind(S_LIMITS, "MaxResupplyFlightSeconds", 600f, "Backstop: stop counting a dispatched transport against the active limit once it has been airborne this long, regardless of what state it is in. A transport destroyed before it reaches its landing state never satisfies FreeSlotOnLanding and holds its slot for the rest of the session, blocking every further dispatch of that airframe. 0 disables this backstop.");
            const string S_THRESHOLDS = "Thresholds";
            ThresholdA = Config.Bind(S_THRESHOLDS, "ThresholdA", 5000f, "Threshold distance in meters for UtilityHelo1 (Ibis).");
            ThresholdB = Config.Bind(S_THRESHOLDS, "ThresholdB", 15000f, "Threshold distance in meters for QuadVTOL1 (Tarantula).");
            const string S_REARMING = "Rearming";
            UnitCooldown = Config.Bind(S_REARMING, "UnitCooldown", 60f, "PER UNIT. Seconds before a NEW resupply sortie may be launched for the same unit, counted from its last dispatch, drop or rearm. This is the setting that stops a unit being supplied back-to-back. It does NOT stop a transport already airborne from making a second pass - that is ReserviceCooldown. Raising it makes a busy unit wait longer between deliveries; 0 disables.");
            ReserviceCooldown = Config.Bind(S_REARMING, "ReserviceCooldown", 1f, "PER UNIT. Seconds before a transport that is ALREADY AIRBORNE may turn back onto a unit it just served. Keep this short: it is what lets a two-crate sortie drop its second container on the same ship instead of carrying it home. Unrelated to UnitCooldown, which governs launching new aircraft.");
            TopUpUntilFull = Config.Bind(S_REARMING, "TopUpUntilFull", true, "Let a unit that is still short of ammunition draw its next delivery without waiting out UnitCooldown. Disable for the stricter one-delivery-per-cooldown behaviour.");
            AllowNuclearFieldRearm = Config.Bind(S_REARMING, "AllowNuclearFieldRearm", false, "Let full-restore containers refill nuclear-capable stations in the field. Vanilla requires warheads AND the container to sit inside an airbase radius (Rearmer.cs:171-177), which never happens at sea; 0.33.4 had no such gate at all.");
            const string S_CHIMERA = "Chimera";
            ChimeraCruiseAltitude = Config.Bind(S_CHIMERA, "CruiseAltitude", 2500f, "Transit altitude in metres above the target for the climb and cruise legs. Floored at the drop altitude + 300m. THE WHOLE GAP BETWEEN THIS AND THE DROP ALTITUDE MUST BE LOST WITHIN DescentStartDistance: at 2500 over a 5000m run-up that is a 46% gradient, where Kelly's AIRLIFT loses only 750m (1200 cruise to 450 drop) over the same distance. If the aircraft arrives at the pattern still descending, lower this rather than raising the drop altitude - raising DescentStartDistance instead buys the gradient back at the cost of flying low for longer.");
            ChimeraDescentDistance = Config.Bind(S_CHIMERA, "DescentStartDistance", 5000f, "Distance to the TARGET at which the Chimera leaves cruise and descends for the drop run. Measured to the delivery point, not to the pattern entry five kilometres beyond it - measuring to the entry started the descent thirteen kilometres out and flew the whole approach low. Matches Kelly's AIRLIFT, which cruises until 5km from the release line and descends once.");
            ChimeraReleaseInterval = Config.Bind(S_CHIMERA, "ReleaseInterval", 0.2f, "PER DROP. Seconds between successive containers leaving the bays within a SINGLE pass. Nothing to do with how often a unit is resupplied - this only spaces the crates so they do not collide on the way out.");
            PostDropHoldBase = Config.Bind(S_CHIMERA, "PostDropHoldBase", 6f, "Seconds a transport flies straight after a release with an empty bay, before turning for home. Also the control-nullifier duration.");
            PostDropHoldPerCrate = Config.Bind(S_CHIMERA, "PostDropHoldPerCrate", 6f, "Additional seconds of post-drop hold per crate still aboard. Each rail launch needs seconds to finish, and a transport with deliveries left needs a settled, straight position before its next approach is computed.");
            ApproachSpeedFloor = Config.Bind(S_CHIMERA, "ApproachSpeedFloor", 1.25f, "Minimum approach and drop-run speed as a multiple of the airframe's landing speed. Too high and the throttle governor commands full power for the whole run; too low and a loaded transport mushes. The debug log prints this against the schedule so the winning term is visible.");
            NavalDropAltitude = Config.Bind(S_CHIMERA, "NavalDropAltitude", 150f, "Release height in metres above the target for naval resupply drops.");
            DescentHandoffAltitude = Config.Bind(S_CHIMERA, "DescentHandoffAltitude", 1000f, "Height in metres above the TARGET at which the descent levels off. The approach and alignment legs then take the aircraft down to the drop altitude, so the run-in is not also a descent. Commanding the drop altitude directly from cruise made one long dive that had not finished when the run began, leaving the aircraft high and still sinking at the release.");
            LandingRetrySeconds = Config.Bind(S_CHIMERA, "LandingRetrySeconds", 10f, "How long a returning transport flies its own egress toward base before offering itself to the vanilla landing state again, in seconds. Vanilla hands an aborted landing back to the combat AI once the aircraft is above corner speed (AIPilotLandingState.AbortingLanding), and the mod takes it straight back so it cannot be turned into a radar-jamming flight - without this delay those two would fight every physics tick. Between attempts the aircraft is under the mod's control and self-defending, not idle.");
            DryFailsafeRollLimit = Config.Bind(S_CHIMERA, "DryFailsafeRollLimit", 5f, "Roll, in degrees, above which the transport counts as turning when it reaches the backstop ring - and therefore uses DryBackstopRadiusTurning instead of DryBackstopRadius. Sampled ONCE, on the first crossing of the level ring, and then held for that pass: re-reading it every tick made the radius oscillate as the wings moved, so an aircraft still banked at the wider ring never tripped there and delivered at the tighter one instead.");
            DryMovingTargetLeadCap = Config.Bind(S_CHIMERA, "DryMovingTargetLeadCap", 2000f, "How far ahead along a moving ground vehicle's own route the drop point may be placed, in metres. Vanilla refuses to let a MOVING ground unit draw from any rearmer (RearmMissionController.TryGetRearmer), so a crate placed alongside a driving vehicle is useless - it has to be where the vehicle stops, which is the end of its current path. The cap stops a vehicle crossing the map from sending the transport chasing a destination kilometres away that it may never reach. 0 disables the lead and drops on the vehicle's current position.");
            RTBOnDamage = Config.Bind(S_CHIMERA, "RTBOnDamage", true, "Send a resupply transport home once it has taken enough damage, jettisoning whatever it still carries. Seeker locks and jamming are ignored - only real hits count.");
            RTBHitCount = Config.Bind(S_CHIMERA, "RTBHitCount", 5f, "Hits a transport can absorb in one sortie before returning. Each condition counts as a fraction of its own threshold and they SUM, so any one of them alone trips the return and a mixture of lesser damage accumulates toward the same total.");
            RTBEngineHitCount = Config.Bind(S_CHIMERA, "RTBEngineHitCount", 3f, "Engine-damage events a transport can absorb in one sortie before returning. Counts alongside RTBHitCount as a fraction of the same total.");
            RTBMinHitDamage = Config.Bind(S_CHIMERA, "RTBMinHitDamage", 1f, "Minimum combined impact + pierce + fire + blast damage for an impact to count as a hit. Stops scratches counting toward RTBHitCount.");
            RTBDetachTriggers = Config.Bind(S_CHIMERA, "RTBDetachTriggers", true, "A single part being torn off sends the transport home immediately, regardless of hit count.");
            RTBEngineLossTriggers = Config.Bind(S_CHIMERA, "RTBEngineLossTriggers", true, "Losing a single engine sends the transport home immediately, regardless of hit count.");
            PatternBankLimit = Config.Bind(S_CHIMERA, "PatternBankLimit", 135f, "Maximum commanded roll, in degrees, while joining the pattern and turning onto the run line. The default is vanilla's own landing-pattern figure, but vanilla flies a fighter onto a runway; the Chimera's author uses 30-45 for this airframe. Lower it if the aircraft swings in and out of the lane instead of settling on it. Does NOT affect the drop run itself.");
            TransitBankLimit = Config.Bind(S_CHIMERA, "TransitBankLimit", 110f, "Maximum commanded roll, in degrees, while climbing, cruising, descending or exiting a drop. MUST STAY BELOW 125: the autopilot multiplies this by up to 1.44 at altitude in a climb, and at or above 180 it switches to a branch that does not clamp roll at all - which is what made transports roll about on the way to the target. Does not affect the pattern or the drop run.");
            DryPreferredRadarAltitude = Config.Bind(S_CHIMERA, "DryPreferredRadarAltitude", 250f, "Radar altitude a ground delivery flies and drops from, in metres. Handed to the autopilot as altitudeHold alongside terrain following, which owns the whole altitude profile - there is no separate climb, cruise or descent for a ground run.");
            DryTurnCornerSpeedFraction = Config.Bind(S_CHIMERA, "DryTurnCornerSpeedFraction", 0.85f, "NAVAL ONLY. Speed a naval transport sheds toward after a partial drop while it sets up its next delivery, as a fraction of its CORNER speed - the airframe's best-turn-rate speed by definition. It no longer governs the DRY second pass: that leg and the dry half of the post-drop exit both fly AlignApproachSpeed, a range-decaying schedule ported from the naval aligning phase, so the two ends of that handoff cannot name different speeds. NOT a multiple of landing speed: this key replaces DryTurnSpeedMultiplier, which was, and any leftover value of that key is ignored. Slower is not tighter in practice - AutopilotPlane scales its turn-toward-destination rate by (airspeed/cornerSpeed) applied quadratically, so bleeding speed toward the landing speed destroys the aircraft's ability to point itself: pinned at ~150m/s it managed 30 degrees of roll and flew AWAY from its target, where at 167-190m/s it rolled to 80 degrees on a ~470m radius. Floored at 1.2x landing speed.");
            DryRunInHandoff = Config.Bind(S_CHIMERA, "DryRunInHandoff", 6000f, "Minimum length of a ground run-in, in metres. A FLOOR, not a cap: the mod computes how far ahead of the aircraft a crate is thrown at the drop altitude and speed, and lengthens the run to contain it, because a run shorter than the throw begins past its own release point and can never drop. Raising this only ever makes the run longer. NOT an Aryx setting - their transport spawns beside its target and has no transit at all, so porting their code as-is flew a Chimera 13km at 250m AGL past a radar station and got it shot down. To spend LESS time low, lower DryCornerSpeedFraction instead: a slower run-in shortens the throw, and the run with it.");
            JammerEnabled = Config.Bind(S_CHIMERA, "JammerEnabled", true, "Employ the Chimera's jamming pod against incoming radar missiles and radar-emitting hostile aircraft.");
            JammerRange = Config.Bind(S_CHIMERA, "JammerRange", 17000f, "Maximum range in metres at which the jamming pod will engage a target.");
            FlareBurstCount = Config.Bind(S_CHIMERA, "FlareBurstCount", 10, "Number of scripted flare pulses released at the cargo drop, when the run-in is most exposed. 0 disables.");
            FlareBurstInterval = Config.Bind(S_CHIMERA, "FlareBurstInterval", 0.5f, "Seconds between the scripted drop flare pulses.");
            ChimeraAirdropMaxRoll = Config.Bind(S_CHIMERA, "AirdropMaxRoll", 20f, "Maximum absolute roll in degrees for an airdrop release. The autopilot is allowed roughly 39 degrees of bank at drop altitude, so a limit near 10 refuses banking that is simply the tracker correcting; 20 still rejects the badly banked releases this gate exists to stop. Crossing the release point above this abandons the run and re-flies the join.");
            ChimeraAirdropMaxVerticalSpeed = Config.Bind(S_CHIMERA, "AirdropMaxVerticalSpeed", 10f, "Maximum absolute vertical speed in m/s for an airdrop release.");
            ChimeraAirdropMaxCrossTrack = Config.Bind(S_CHIMERA, "AirdropMaxCrossTrack", 150f, "Maximum lateral offset from the run centreline, in metres, for an airdrop release.");
            ChimeraAirdropMaxAttempts = Config.Bind(S_CHIMERA, "AirdropMaxAttempts", 3, "Run restarts allowed before the cargo is released regardless of attitude, so a transport never carries its load home. Logged as forced=True.");
            MaxRunAttempts = Config.Bind(S_CHIMERA, "MaxRunAttempts", 5, "How many times a sortie may abandon its run and re-fly the join before giving up and returning to base. Unbounded retries are worse than a failed delivery: the aircraft holds an active slot, burns fuel, and circles near the target until something goes wrong. 0 restores unlimited retries.");
            const string S_CHIMERA_REPAIR = "ChimeraRepair";
            ChimeraUseRunwayDelivery = Config.Bind(S_CHIMERA_REPAIR, "UseRunwayDelivery", true, "Deliver a repair dozer with a low gear-down pass along the target airbase's runway instead of a 700m airdrop. Falls back to the airdrop for outposts and airbases with no usable runway.");
            ChimeraRunwayDropAltitude = Config.Bind(S_CHIMERA_REPAIR, "RunwayDropAltitude", 5f, "Release height in metres AGL for the runway pass. Clamped to 2-8m.");
            ChimeraRunwayDropTolerance = Config.Bind(S_CHIMERA_REPAIR, "RunwayDropTolerance", 2f, "Permitted deviation from the release height, in metres.");
            ChimeraRunwayDescentDistance = Config.Bind(S_CHIMERA_REPAIR, "RunwayDescentDistance", 6000f, "Distance before the release point at which the low-level descent begins.");
            ChimeraRunwayMinReleaseSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMinReleaseSpeed", 75f, "Minimum airspeed for a runway release.");
            ChimeraRunwayMaxReleaseSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxReleaseSpeed", 190f, "Maximum airspeed for a runway release.");
            ChimeraRunwayMaxRoll = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxRoll", 18f, "Maximum absolute roll in degrees at release.");
            ChimeraRunwayMaxVerticalSpeed = Config.Bind(S_CHIMERA_REPAIR, "RunwayMaxVerticalSpeed", 30f, "Maximum absolute vertical speed in m/s at release.");
            const string S_CAPACITY = "SupplyCapacity";
            MunitionsPalletCapacity = Config.Bind(S_CAPACITY, "MunitionsPallet1x1", 6000f, "Supply capacity for Munitions Pallet 1x1.");
            MunitionsPalletSingleUse = Config.Bind(S_CAPACITY, "MunitionsPallet1x1_SingleUse", true, "Whether Munitions Pallet 1x1 is single use.");
            MunitionsPallet2Capacity = Config.Bind(S_CAPACITY, "MunitionsPallet2x2", 1500f, "Supply capacity for Munitions Pallet 2x2.");
            MunitionsPallet2SingleUse = Config.Bind(S_CAPACITY, "MunitionsPallet2x2_SingleUse", true, "Whether Munitions Pallet 2x2 is single use.");
            NavalPalletCapacity = Config.Bind(S_CAPACITY, "NavalPallet1x1", 6000f, "Supply capacity for Naval Pallet.");
            NavalPalletSingleUse = Config.Bind(S_CAPACITY, "NavalPallet1x1_SingleUse", true, "Whether Naval Pallet is single use.");
            MunitionsContainerCapacity = Config.Bind(S_CAPACITY, "MunitionsContainer1", 10000f, "Supply capacity for Munitions Container.");
            MunitionsContainerSingleUse = Config.Bind(S_CAPACITY, "MunitionsContainer1_SingleUse", true, "Whether Munitions Container is single use.");
            NavalContainerCapacity = Config.Bind(S_CAPACITY, "NavalSupplyContainer1", 10000f, "Supply capacity for Naval Container.");
            NavalContainerSingleUse = Config.Bind(S_CAPACITY, "NavalSupplyContainer1_SingleUse", true, "Whether Naval Container is single use.");
            const string S_RADIUS = "SupplyRadius";
            MunitionsPalletRadius = Config.Bind(S_RADIUS, "MunitionsPallet1x1", 100f, "Rearm radius for Munitions Pallet 1x1.");
            MunitionsPallet2Radius = Config.Bind(S_RADIUS, "MunitionsPallet2x2", 100f, "Rearm radius for Munitions Pallet 2x2.");
            NavalPalletRadius = Config.Bind(S_RADIUS, "NavalPallet1x1", 100f, "Rearm radius for Naval Pallet.");
            MunitionsContainerRadius = Config.Bind(S_RADIUS, "MunitionsContainer1", 100f, "Rearm radius for Munitions Container.");
            NavalContainerRadius = Config.Bind(S_RADIUS, "NavalSupplyContainer1", 200f, "Rearm radius for Naval Container.");
            const string S_FULLRESTORE = "FullRestore";
            FullRestoreMunitionsPallet1 = Config.Bind(S_FULLRESTORE, "MunitionsPallet1x1", false, "Munitions Pallet 1x1 restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreMunitionsPallet2 = Config.Bind(S_FULLRESTORE, "MunitionsPallet2x2", false, "Munitions Pallet 2x2 restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreNavalPallet1 = Config.Bind(S_FULLRESTORE, "NavalPallet1x1", false, "Naval Pallet restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreMunitionsContainer1 = Config.Bind(S_FULLRESTORE, "MunitionsContainer1", false, "Munitions Container restores every weapon and is consumed, ignoring its capacity.");
            FullRestoreNavalSupplyContainer1 = Config.Bind(S_FULLRESTORE, "NavalSupplyContainer1", false, "Naval Supply Container restores every weapon and is consumed, ignoring its capacity.");
            const string S_AI = "AI";
            ExcludeLogisticsFromAILimit = Config.Bind(S_AI, "ExcludeLogisticsFromAILimit", true, "Exclude resupply aircraft from Faction AI limits.");
            const string S_ADVANCED = "Advanced";
            ExpressRearmEnabled = Config.Bind(S_ADVANCED, "ExpressRearmEnabled", true, "When enabled, all ships (naval) rearm weapons unconditionally (Wet resupply).");
            ExpressRearmGroundEnabled = Config.Bind(S_ADVANCED, "ExpressRearmGroundEnabled", true, "When enabled, all ground vehicles and buildings rearm weapons unconditionally (Dry resupply).");
            RearmRequestSensitivity = Config.Bind(S_ADVANCED, "RearmRequestSensitivity", 0.5f, new ConfigDescription("Ammo fraction remaining below which a unit requests rearm. 0.999 means any expenditure asks; 0.5 matches vanilla but starves large-magazine launchers.", new AcceptableValueRange<float>(0.0f, 1.0f)));
            DebugLogging = Config.Bind(S_ADVANCED, "DebugLogging", true, "Enable verbose debug logging for troubleshooting.");
            RestampInterval = Config.Bind(S_ADVANCED, "RestampInterval", 5f, "GLOBAL sweep. Seconds between re-stamping RequestRearmLevel across every unit, catching weapons created after spawn that would otherwise sit on vanilla's lower threshold. Cost is one walk over all units; 0 disables the stamping only - orphaned-rearm recovery still runs on the same sweep.");
            StampThrottle = Config.Bind(S_ADVANCED, "StampThrottle", 2f, "Minimum seconds between weapon re-stamps of the same unit on the fire path. The stamp used to walk every station and weapon on every shot fired, which dominated the frame during naval gunfire. 0 restores the old per-shot behaviour.");
        }
        private void Update()
        {
            float now = Time.timeSinceLevelLoad;
            if (now < _lastLevelTime) ResetForNewLevel();
            _lastLevelTime = now;
            if (now - _lastMonitorTime >= MONITOR_INTERVAL)
            {
                _lastMonitorTime = now;
                if (Dbg) Log.LogInfo("[SB|P2] Executing 5-second periodic monitor.");
                PeriodicMonitor();
            }
            ChimeraSpawnQueue.Drain();
        }
        private void ResetForNewLevel()
        {
            Log.LogInfo("[SupplyBuffetMod] Level reload detected (clock rewound). Resetting mod state.");
            _lastMonitorTime = 0f;
            _nullifierLatestEnd = 0f;
            _rearmListCache.Clear();
            ResupplyCensus.ResetForNewLevel();
            ChimeraSpawnQueue.ResetForNewLevel();
            ResupplyMissionManager.ResetForNewLevel();
            AirbaseRepairManager.ResetForNewLevel();
            DozerShepherd.ResetForNewLevel();
            SortieParity.ResetForNewLevel();
        }
        private void PeriodicMonitor()
        {
            if (Encyclopedia.i == null || FactionRegistry.HQLookup == null) return;
            ResupplyMissionManager.Update();
            AirbaseRepairManager.Update();
            DozerShepherd.Update();
            RestampUnits();
            foreach (var hq in FactionRegistry.HQLookup.Values)
            {
                if (hq == null || !hq.isActiveAndEnabled || hq.faction == null) continue;
                var controller = hq.RearmMissionController;
                if (controller == null) continue;
                if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, true, false, null, out Unit shipNeedingRearm) && shipNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected naval unit needing rearm: {shipNeedingRearm.unitName} (HQ: {hq.name}). Triggering wet resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, shipNeedingRearm);
                }
                else if (ResupplyMissionManager.TryGetUnassignedUnitNeedingRearm(controller, false, true, null, out Unit groundNeedingRearm) && groundNeedingRearm != null)
                {
                    if (DebugLogging.Value)
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Periodic monitor detected ground unit needing rearm: {groundNeedingRearm.unitName} (HQ: {hq.name}). Triggering dry resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, groundNeedingRearm);
                }
                else if (ResupplyMissionManager.TryGetRestockingSupplyVehicle(controller, null, out Unit restockingTruck) && restockingTruck != null)
                {
                    if (DebugLogging.Value && restockingTruck.TryGetComponent(out Rearmer truckRearmer))
                    {
                        Log.LogInfo($"[SupplyBuffetMod] Supply vehicle '{restockingTruck.unitName}' is driving to restock (capacity {truckRearmer.Capacity:F0}/{truckRearmer.GetMaxCapacity():F0}). Triggering dry resupply.");
                    }
                    ResupplyDispatcher.TryDispatchResupply(hq, restockingTruck);
                }
            }
        }
        private float _lastRestampTime;
        private void RestampUnits()
        {
            if (UnitRegistry.allUnits == null) return;
            bool doStamp = RestampInterval != null && RestampInterval.Value > 0f
                && Time.timeSinceLevelLoad - _lastRestampTime >= RestampInterval.Value;
            if (doStamp) _lastRestampTime = Time.timeSinceLevelLoad;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (unit == null || unit.disabled) continue;
                if (doStamp) RearmStampHelper.StampUnit(unit);
                RepairOrphanedRearmRequest(unit);
            }
            _rearmListCache.Clear();
        }
        private static readonly Dictionary<RearmMissionController, HashSet<Unit>> _rearmListCache
            = new Dictionary<RearmMissionController, HashSet<Unit>>();
        private static void RepairOrphanedRearmRequest(Unit unit)
        {
            if (!unit.HasRequestedRearm) return;
            if (unit is Aircraft) return;
            FactionHQ hq = unit.NetworkHQ;
            if (hq == null) return;
            RearmMissionController controller = hq.RearmMissionController;
            if (controller == null || controller.UnitsNeedingRearm == null) return;
            if (!_rearmListCache.TryGetValue(controller, out HashSet<Unit> listed))
            {
                listed = new HashSet<Unit>();
                for (int i = 0; i < controller.UnitsNeedingRearm.Count; i++)
                {
                    Unit listedUnit = controller.UnitsNeedingRearm[i];
                    if (listedUnit != null) listed.Add(listedUnit);
                }
                _rearmListCache[controller] = listed;
            }
            if (listed.Contains(unit)) return;
            if (unit.GetAmmoValue().Missing <= 0f) return;
            unit.HasRequestedRearm = false;
            unit.RequestRearm();
            listed.Add(unit);
            Log.LogInfo($"[SupplyBuffetMod] '{unit.unitName}' still needed rearm but had fallen off its HQ's list; re-registered it.");
        }
        public static bool IsNavalUnit(Unit unit) => unit is Ship;
        public static bool IsModDispatchedFlight(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            return ResupplyCensus.WasDispatchedByMod(aircraft)
                || ResupplyMissionManager.IsAssignedToResupply(aircraft);
        }
        private static bool IsSpawnedAircraftWet(Aircraft ac)
        {
            if (ac == null) return false;
            if (ResupplyCensus.TryGetIsWet(ac, out bool tagged)) return tagged;
            if (ac.loadout == null || ac.loadout.weapons == null) return false;
            foreach (var w in ac.loadout.weapons)
            {
                if (w == null || w.info == null) continue;
                string infoName = w.info.name;
                if (!string.IsNullOrEmpty(infoName) && infoName.IndexOf("Naval", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }
            return false;
        }
        private static bool IsLandingHome(Aircraft ac)
        {
            if (FreeSlotOnLanding == null || !FreeSlotOnLanding.Value) return false;
            if (ac.pilots == null) return false;
            for (int i = 0; i < ac.pilots.Length; i++)
            {
                Pilot pilot = ac.pilots[i];
                if (pilot == null) continue;
                if (pilot.currentState is AIPilotLandingState || pilot.currentState is AIHeloLandingState) return true;
            }
            return false;
        }
        private static readonly ConditionalWeakTable<Aircraft, StrongBox<float>> ResupplyFlightStart =
            new ConditionalWeakTable<Aircraft, StrongBox<float>>();
        public static void StampResupplyFlightStart(Aircraft aircraft)
        {
            if (aircraft == null) return;
            ResupplyFlightStart.GetValue(aircraft, _ => new StrongBox<float>()).Value = Time.timeSinceLevelLoad;
        }
        private static bool ExceededMaxFlightTime(Aircraft ac)
        {
            float maxSeconds = (MaxResupplyFlightSeconds != null) ? MaxResupplyFlightSeconds.Value : 600f;
            if (maxSeconds <= 0f) return false;
            return ResupplyFlightStart.TryGetValue(ac, out StrongBox<float> start)
                && Time.timeSinceLevelLoad - start.Value > maxSeconds;
        }
        private static int GetActiveResupplyCount(FactionHQ hq, string jsonKey, bool isWet, bool includeQueue = false)
        {
            int count = ResupplyCensus.CountInFlight(hq, jsonKey, isWet);
            if (UnitRegistry.allAircraft != null)
            {
                foreach (var ac in UnitRegistry.allAircraft)
                {
                    if (ac != null && !ac.disabled && ac.definition != null && ac.definition.jsonKey == jsonKey)
                    {
                        if (!IsModDispatchedFlight(ac)) continue;
                        if (IsLandingHome(ac)) continue;
                        if (ExceededMaxFlightTime(ac)) continue;
                        if (ac.NetworkHQ == hq || (ac.NetworkHQ != null && ac.NetworkHQ.faction == hq.faction))
                        {
                            if (IsSpawnedAircraftWet(ac) == isWet)
                            {
                                count++;
                            }
                        }
                    }
                }
            }
            if (includeQueue && jsonKey == "Aryx_CargoPlane1")
            {
                count += ChimeraSpawnQueue.CountFor(hq, isWet);
            }
            return count;
        }
        public static bool NoResupplyTransportAirborne(FactionHQ hq)
        {
            if (hq == null) return true;
            if (ResupplyCensus.AnyDispatchPending(hq)) return false;
            if (UnitRegistry.allAircraft == null) return true;
            foreach (var ac in UnitRegistry.allAircraft)
            {
                if (ac == null || ac.disabled || ac.definition == null) continue;
                string key = ac.definition.jsonKey;
                if (key != "UtilityHelo1" && key != "QuadVTOL1" && key != "Aryx_CargoPlane1") continue;
                if (!IsModDispatchedFlight(ac)) continue;
                if (IsLandingHome(ac) || ExceededMaxFlightTime(ac)) continue;
                if (ac.NetworkHQ == hq || (ac.NetworkHQ != null && ac.NetworkHQ.faction == hq.faction)) return false;
            }
            return true;
        }
        public static bool IsResupplyLimitReached(FactionHQ hq, string jsonKey, bool isWet, bool includeQueue = false)
        {
            int maxLimit = int.MaxValue;
            if (jsonKey == "UtilityHelo1") 
                maxLimit = isWet ? (ActiveIbisLimitWetConfig?.Value ?? int.MaxValue) : (ActiveIbisLimitDryConfig?.Value ?? int.MaxValue);
            else if (jsonKey == "QuadVTOL1") 
                maxLimit = isWet ? (ActiveTarantulaLimitWetConfig?.Value ?? int.MaxValue) : (ActiveTarantulaLimitDryConfig?.Value ?? int.MaxValue);
            else if (jsonKey == "Aryx_CargoPlane1") 
                maxLimit = isWet ? (ActiveChimeraLimitWetConfig?.Value ?? int.MaxValue) : (ActiveChimeraLimitDryConfig?.Value ?? int.MaxValue);
            int activeCount = GetActiveResupplyCount(hq, jsonKey, isWet, includeQueue);
            if (activeCount >= maxLimit)
            {
                if (DebugLogging != null && DebugLogging.Value)
                {
                    Log.LogInfo($"[SupplyBuffetMod] Active limit reached for '{jsonKey}' ({(isWet ? "Wet" : "Dry")}) (Active/Queued: {activeCount} >= Limit: {maxLimit}).");
                }
                return true;
            }
            return false;
        }
        private class NullifierWindow
        {
            public float EndTime;
            public Vector3 LockedDir;
        }
        private static readonly ConditionalWeakTable<Aircraft, NullifierWindow> Nullified =
            new ConditionalWeakTable<Aircraft, NullifierWindow>();
        private static float _nullifierLatestEnd;
        public static bool AnyControlNullified => Time.timeSinceLevelLoad < _nullifierLatestEnd;
        public static void TriggerControlNullifier(Aircraft aircraft, float duration = 3.0f)
        {
            if (aircraft == null) return;
            float endTime = Time.timeSinceLevelLoad + duration;
            NullifierWindow w = Nullified.GetValue(aircraft, _ => new NullifierWindow());
            w.EndTime = endTime;
            Vector3 dir = (aircraft.rb != null && aircraft.rb.velocity.sqrMagnitude > 1f)
                ? aircraft.rb.velocity
                : aircraft.transform.forward;
            dir.y = 0f;
            w.LockedDir = (dir.sqrMagnitude > 0.001f) ? dir.normalized : Vector3.forward;
            if (endTime > _nullifierLatestEnd) _nullifierLatestEnd = endTime;
            Log.LogInfo($"[SupplyBuffetMod] Control nullifier triggered for '{aircraft.unitName}' until T={endTime:F1}s ({duration}s duration).");
        }
        public static bool IsControlNullified(Aircraft aircraft)
        {
            if (aircraft == null) return false;
            return Nullified.TryGetValue(aircraft, out NullifierWindow w) && Time.timeSinceLevelLoad < w.EndTime;
        }
        public static bool TryGetNullifiedVelocityDir(Aircraft aircraft, out Vector3 dir)
        {
            if (aircraft != null && Nullified.TryGetValue(aircraft, out NullifierWindow w))
            {
                dir = w.LockedDir;
                return true;
            }
            dir = Vector3.forward;
            return false;
        }
    }
}