# Supply Buffet 2.1.0

![1000100405](https://github.com/user-attachments/assets/acd106c4-9dee-4ea0-a9c4-14da0f9321a7)

Ensuring that your ships, ground units, and front-line bases never run dry when the heat is on.

## Features
- **Express Rearm (Naval & Ground):** Supply Buffet forces an immediate `0.999f` rearm request threshold.  When a naval or ground unit fires, units no longer wait to spent half of their magazine; they ask for supplies immediately. If target is further than 10 km, spawn Tarantula Instead.
- **Native 0.34 Truck Integration:** Ground unit correctly spawn the new 0.34 Munitions Trucks from the nearest Vehicle Depot. Alongside Air Supply Run from nearest Helipad, Carriers, or Supply Ship
- **Naval Helicopter Supply Drops:** Ships are strictly served by Ibis and Tarantula launching from Helipad or Atlas Supply Ship
- **Flawless Loadout Forcing:** Hooked directly into the AI deployment and standard loadout selection to assigns exactly the right payload — Munitions Pallets and Containers for ground targets, and Naval Pallets/Containers for ships — completely bypassing vanilla RNG or hardcoded limits.
