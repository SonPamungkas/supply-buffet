using UnityEngine;
namespace SupplyBuffetMod
{
    internal static class TransportGear
    {
        internal static void Apply(Aircraft aircraft, bool wantGear)
        {
            if (aircraft == null) return;
            LandingGear.GearState gearState = aircraft.gearState;
            if (gearState == LandingGear.GearState.Extending || gearState == LandingGear.GearState.Retracting) return;
            LandingGear.GearState settled = wantGear
                ? LandingGear.GearState.LockedExtended
                : LandingGear.GearState.LockedRetracted;
            if (gearState == settled) return;
            aircraft.SetGear(wantGear);
        }
    }
}