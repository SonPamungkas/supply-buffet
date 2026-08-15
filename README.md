# Supply Buffet

![1000100405](https://github.com/user-attachments/assets/acd106c4-9dee-4ea0-a9c4-14da0f9321a7)

Ensuring that your ships, ground units, and front-line bases never run dry when the heat is on.

## Features
- **Express Rearm (Naval & Ground):** Every ship, ground vehicle and building weapon is made rearmable and asks for resupply at a configurable ammo threshold (`RearmRequestSensitivity`, default `0.5`), instead of only the handful of weapons vanilla marks rearmable.
- **Air-Only Ground Logistics:** Munitions trucks no longer spawn from vehicle depots; supplies arrive by air.
- **Paced Launches:** A faction launches at most one transport per `SpawnIntervalSeconds` (default 20 s), so a fleet calling for resupply at once produces a steady stream of deliveries instead of a mass launch.
- **Full Chimera Sortie:** Climb, cruise at altitude, descend for the run, drop, then return to base and land — with a jamming pod and flare cover for the exposed part.
- **Demand-Sized Deliveries:** The Chimera releases as many crates as the target's capacity justifies, and can serve several targets on one sortie instead of dropping a single crate and going home.
- **Instant Dispatch:** A resupply mission is evaluated the moment a unit registers a rearm need, not on the next monitor tick, and the transport is spawned on the same frame if a hangar is free.
- **Precision Airdrop:** The MC-260 Chimera flies a proper three-point delivery run (approach, drop, exit) with the cargo bay pre-opened during the run-in, so crates land on the target instead of sailing past it.
- **Naval & Ground Supply Drops:** Ships are served by Utility Helicopters, VTOLs, and the Chimera launching from Airbases, Helipads, and Atlas supply ships; ground units and structures are served from any allied airbase.
- **Flawless Loadout Forcing:** The mod builds the payload itself — Munitions Pallets and Containers for ground targets, Naval Pallets/Containers for ships — completely bypassing vanilla RNG or hardcoded limits.
- **Distance-Tiered Aircraft Selection:** Ibis for short hauls, Tarantula for medium, Chimera for long range, with a fallback that refuses to send a cargo plane across the map when a helicopter is far closer.
