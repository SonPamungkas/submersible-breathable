<img width="1078" height="1078" alt="20260623_074835" src="https://github.com/user-attachments/assets/0e2f1968-99a3-4ec7-aa3c-f86d9bb8eb5b" />

Lets surface ships dive and resurface at will like submarines, without dying to the game's normal water-contact kill checks.

## What it does

Ships are normally killed the instant they touch or dip below the sea surface, capsize, or nose-dive (`Ship.CheckShipBuoyancy`). This mod patches that check so any ship you enable it for can submerge and surface freely instead. On load, it scans every `Ship` in the scene and registers a **per-ship waterline slider** and a **per-ship dive/surface hotkey** in BepInEx's Configuration Manager — no manual setup beyond binding a key.

### Per-ship state machine

Each ship cycles through three states via its own hotkey:

```
Off  -->  Submerged  <-->  Surfaced
 (one-way)      (two-way toggle)
```

- **Off** — completely vanilla. The mod does nothing to a ship until its hotkey is pressed for the first time.
- **Submerged** — the ship dives to its configured waterline offset. Water-contact/capsize/nose-dive kills are suppressed; kinetic damage to the bridge/hull still works normally.
- **Surfaced** — the ship returns to its natural floating height and settles there.

Reaching `Off` is permanent per ship-spawn: once a ship dives, it only ever toggles between Submerged and Surfaced afterward. State resets to `Off` for every ship on mission restart (a fresh ship spawn never inherits a previous mission's dive state).

### Physics

- **Waterline offset** — a `-50` to `+50` slider per ship. Positive rides higher out of the water, negative dives deeper, positive flies higher (if you want Flying Dutchman, Helicarrier, or Spaceship). Targets are computed in the engine's origin-invariant global coordinate space, so they stay correct across floating-origin shifts during long missions.
- **Torque suppression while submerged** — the game's real per-part buoyancy torque (which fights an artificially-held draft and would otherwise roll the ship over) is replaced with a lift-only force; only the net buoyant force is kept, no destabilizing torque.
- **Resurface pitch pulse** — a brief, configurable pitch-up torque fires for a short window right after a ship surfaces, then a small angular damping term takes over so the ship actually settles instead of spinning indefinitely.
- **Surface stabilizer** — a continuous, proportional righting torque counters roll/list while a ship sits on the surface.
- **Hard waterline stop + gentle nudge** — vertical velocity is killed the instant a surfacing ship reaches its natural waterline (no overshoot/launching), and a weak corrective force afterward keeps it pinned there if anything nudges it off-target.

## Notes
This is a testbed for submarine mechanics built on top of surface-ship buoyancy code rather than a purpose-built submarine simulation — expect rough edges (e.g. ship-class-specific propulsion/visual quirks while submerged) and treat the physics constants above as starting points to tune per ship class.
