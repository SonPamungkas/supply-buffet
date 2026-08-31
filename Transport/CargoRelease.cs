using System;
using UnityEngine;
namespace SupplyBuffetMod
{
    internal enum Crate
    {
        Single,
        Rear,
        Later
    }
    internal struct ReleaseInputs
    {
        internal Vector3 ToTarget;
        internal Vector3 Velocity;
        internal Vector3 RunAxis;
        internal float Speed;
        internal float RollDegrees;
        internal float RadarAlt;
        internal float VerticalSpeed;
        internal bool Wet;
        internal Crate Crate;
        internal string CargoKey;
    }
    internal static class CargoRelease
    {
        private const float BASELINE_DRY = 850f;
        private const float BASELINE_WET = 784f;
        private const float OFFSET_HLT = 600f;
        private const float OFFSET_REAR_CRATE_DRY = -110f;
        private const float OFFSET_REAR_CRATE_WET = -242f;
        private const float OFFSET_LATER_CRATE = 0f;
        private const float BANK_REDUCTION = 150f;
        private const float BANK_ROLL_LIMIT = 5f;
        private const float REFERENCE_SPEED = 200f;
        private const float SCALE_MIN = 0.5f;
        private const float SCALE_MAX = 1.5f;
        internal const float PAST_TARGET_TOLERANCE = 180f;
        private const float WIND_DESCENT_RATE = 10f;
        private const float WIND_OFFSET_MAX = 400f;
        internal static Vector3 WindOffset(Vector3 wind, float dropHeight)
        {
            if (dropHeight <= 0f) return Vector3.zero;
            wind.y = 0f;
            Vector3 offset = -wind * (dropHeight / WIND_DESCENT_RATE);
            return Vector3.ClampMagnitude(offset, WIND_OFFSET_MAX);
        }
        internal static Crate CrateOf(WeaponStation station)
        {
            if (station == null) return Crate.Single;
            if (station.FullAmmo <= 1) return Crate.Single;
            return (station.Ammo >= station.FullAmmo) ? Crate.Rear : Crate.Later;
        }
        internal static float Distance(in ReleaseInputs i)
        {
            float b = i.Wet ? BASELINE_WET : BASELINE_DRY;
            if (!string.IsNullOrEmpty(i.CargoKey)
                && i.CargoKey.IndexOf("HLT", StringComparison.Ordinal) >= 0)
            {
                b += OFFSET_HLT;
            }
            if (i.Crate == Crate.Rear) b += i.Wet ? OFFSET_REAR_CRATE_WET : OFFSET_REAR_CRATE_DRY;
            else if (i.Crate == Crate.Later) b += OFFSET_LATER_CRATE;
            if (i.RollDegrees > BANK_ROLL_LIMIT) b -= BANK_REDUCTION;
            float scale = Mathf.Clamp(i.Speed / REFERENCE_SPEED, SCALE_MIN, SCALE_MAX);
            return Mathf.Max(b, 0f) * scale;
        }
        internal static bool RingReached(in ReleaseInputs i, float b, out float slant, out float trip)
        {
            float height = i.ToTarget.y;
            slant = i.ToTarget.magnitude;
            trip = Mathf.Sqrt(height * height + b * b);
            bool closing = Vector3.Dot(i.Velocity, i.ToTarget) > 0f;
            return slant <= trip && closing;
        }
        internal static bool PastTarget(in ReleaseInputs i)
        {
            Vector3 flat = i.ToTarget;
            flat.y = 0f;
            return AlongTrack(i) <= 0f - PAST_TARGET_TOLERANCE && flat.sqrMagnitude > 0f;
        }
        internal static bool RunwayReady(in ReleaseInputs i, float targetAlt)
        {
            if (Mathf.Abs(i.RadarAlt - targetAlt) > Plugin.Cfg(Plugin.ChimeraRunwayDropTolerance, 2f)) return false;
            float minSpeed = Plugin.Cfg(Plugin.ChimeraRunwayMinReleaseSpeed, 75f);
            float maxSpeed = Plugin.Cfg(Plugin.ChimeraRunwayMaxReleaseSpeed, 190f);
            if (i.Speed < minSpeed || i.Speed > maxSpeed) return false;
            if (i.RollDegrees > Plugin.Cfg(Plugin.ChimeraRunwayMaxRoll, 18f)) return false;
            if (Mathf.Abs(i.VerticalSpeed) > Plugin.Cfg(Plugin.ChimeraRunwayMaxVerticalSpeed, 30f)) return false;
            Vector3 toTarget = i.ToTarget;
            toTarget.y = 0f;
            Vector3 horizVel = i.Velocity;
            horizVel.y = 0f;
            if (toTarget.sqrMagnitude <= 1f || horizVel.sqrMagnitude <= 1f) return false;
            return Vector3.Angle(toTarget, horizVel) < 20f;
        }
        internal static float AlongTrack(in ReleaseInputs i)
        {
            Vector3 flat = i.ToTarget;
            flat.y = 0f;
            return Vector3.Dot(flat, i.RunAxis);
        }
    }
}