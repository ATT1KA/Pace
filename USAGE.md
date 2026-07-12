# Usage Guide

## Prerequisites

- [S&box](https://sbox.game) installed with the scene-system editor (Source 2)
- The code targets the S&box API as of early 2026. The first task on a fresh checkout is a compile pass — expect mechanical fixes (property renames), not design changes.

## Opening the Project

1. Open `tenpaces.sbproj` in the S&box editor.
2. Compile the project. The compiler is configured with root namespace `TenPaces` and define constants `SANDBOX;ADDON;DEBUG`.

## Scene Setup

A playable scene requires the following singleton components and at least one Ground with gates.

### Singletons (one of each in the scene root)

| Component | Purpose |
|-----------|---------|
| `MatchDirector` | Host-authoritative 8-phase match state machine |
| `Umpire` | Sports announcer — derives calls from score state |
| `SoundLedger.Broadcaster` | Networked honest sound — the central information bus |
| `FeelTelemetry` | Per-match KPI collection (p-curve, composure, adoption) |
| `HonestyAuditor` | Runtime CI — logs covenant violations, never auto-fixes |
| `LobbySystem` | Player connection, seating, spectators (set duelist prefab reference) |

### HUD and Camera

- Add a `Hud` Razor panel to the scene (scorecard, shell pips, crosshair, umpire ticker)
- Add `ScreenEffects` to the camera (hitstop, wound feedback, ceremony effects)

### Ground

Add a `GroundDefinition` component with:
- At least 1 spawn gate per side (`GatesSideA`, `GatesSideB`) — three per side is the intended grammar
- A `ResolveVolumeCenter` transform and `ResolveRadius` (default 350u) for the Reckoning
- Two light rigs: `HighNoonLightRig` and `DuskLightRig` (Initiative holder picks the Hour)
- Ambient masking schedule (optional — deterministic train/wind/piano events, learnable by players and bot)

The startup scene is `Assets/scenes/lobby.scene`.

### Duelist Prefab

Create a prefab with:
- `DuelistController` (auto-requires `CharacterController`, `Revolver`, `Cylinder`, `Vitals`)
- Child object with `HatComponent`
- A `SkinnedModelRenderer`

Set this prefab as the reference on `LobbySystem`.

### Solo Play (The Stranger)

Add a `DrillBot` component to a second duelist prefab instance. The bot drives the identical controller through a virtual input shim — same accuracy matrix, same sound ledger, same physics. Its difficulty comes from discipline, not aim.

## Controls

### Keyboard + Mouse

| Action | Key |
|--------|-----|
| Move | WASD |
| Jump | Space |
| Slide | Ctrl |
| Soft Step | Shift (hold) |
| Fire | Mouse 1 |
| Aim | Mouse 2 |
| Draw / Holster | Q |
| Reload | R |
| Pistol Whip | V |
| Trick | F |
| Cylinder Check | T |
| Spin | G |
| Hammer Thumb | H |
| Tip Hat | B |
| Ready | E |
| Scoreboard | Tab |

### Gamepad

| Action | Button |
|--------|--------|
| Jump | A |
| Slide | B |
| Soft Step | Left Stick Click |
| Fire | Right Trigger |
| Aim | Left Trigger |
| Draw / Holster | Y |
| Reload | X |
| Pistol Whip | Right Stick Click |
| Trick | Right Shoulder |
| Cylinder Check | D-pad Up |
| Spin | D-pad Right |
| Hammer Thumb | D-pad Left |
| Tip Hat | D-pad Down |
| Ready | A |
| Scoreboard | Menu Left |

Movement on gamepad uses the left stick.

## Match Flow

1. **Lobby** — press E to ready. First two ready players take the seats; others spectate.
2. **Coin Flip** — opening ceremony determines first Initiative holder.
3. **Initiative Pick** (10s) — holder selects the Hour (High Noon or Dusk) and their spawn gate. Timeout = random.
4. **Approach** (1.5s) — both duelists locked at their gates. The "ten paces" beat.
5. **Live** (up to 90s) — the duel. Draw, move, shoot, reload, whip, trick.
6. **Reckoning** — if the point clock runs out: bell at T-15, pressure damage at T-0 outside the resolve volume (10 hp/s after a 6s grace window).
7. **Point Ceremony** (3s) — umpire call, tableau, score update.
8. **Changeover** (45s, after odd games) — the adaptation window. Swap your Form and Trick here. This is the only time loadout changes are allowed.

Repeat until a match winner is determined by the tennis scoring format.

## Tuning

All gameplay constants live in `Code/Tuning.cs` — the single source of truth. S&box hot-reload means edits to this file retune a live playtest in seconds.

Constants are annotated:
- **`[LAW]`** — Design law. Requires a design-document amendment to change. These are the game's non-negotiable truths.
- **`[BASELINE]`** — Starting values expected to move during beta tuning.

### Tuning categories in `Tuning.cs`

- **Locomotion** — gait speeds, Plant timing, slide, jump, mantle
- **The Revolver** — damage, accuracy cone matrix, draw/beat timing, recoil
- **Reload Ritual** — per-shell gate/eject/insert/close timings
- **Pistol Whip** — range, damage, stagger, knockback, cooldown
- **Wounds & Death** — hitstop, flinch, heartbeat, hat shot, ceremony
- **Match Structure** — points/games/sets targets, point time limit, Reckoning, changeover duration
- **Tricks** — throw speeds, damage, radii
- **Audio Ledger** — audible radius per sound event type
- **Camera** — FOV, eye height, run kick
- **KPI Targets** — per-point winrate targets for skill validation

## The Stranger (Bot Tiers)

Four difficulty tiers, tuned by discipline — not speed or aimbot:

| Tier | Character | What it teaches |
|------|-----------|-----------------|
| **Drifter** | Over-commits, reloads in the open, telegraphs | Teaches by being readable |
| **Deputy** | More disciplined, punishes obvious mistakes | Forces basic competence |
| **Bounty** | Sound-literate, counts shells, exploits tendencies | Rewards information play |
| **The Stranger** | Near-perfect cadence, deliberate pattern-breaking | The aspirational ceiling |

Bot tendencies (route bias, preferred range, aggression, feint rate) persist across points within a game and re-roll at changeovers — the same adaptation cadence humans use.

## Multiplayer

- Host-authoritative networking via S&box lobby system
- Max 8 players: 2 duelists + 6 spectators (in-scene with POV/overhead camera cycling)
- Min 1 player (solo vs DrillBot)
- 128 tick rate
- Hit adjudication: client fires, host resolves trace with plausibility validation, result broadcast to all
- Disconnection mid-match = recorded walkover (forfeit)

## Testing

### Scoring Engine

`Tests/ScoreTest.cs` compiles standalone against `Code/Core/TennisScore.cs` with no engine dependencies. It runs:
- 20 rules assertions covering 4-0 wins, deuce, advantage, break detection, sets, tiebreaks, match points, and score calling
- 100k-match Monte Carlo simulations at multiple p-values, validated against the closed-form probability model

### Honesty Auditor

Runs as a scene component during playtests. Four continuous audits:

1. **Sound Truth** — ledger entry positions match actual source positions (tolerance: 48u)
2. **Animation Truth** — synced mechanical state matches AnimGraph input
3. **Motion Truth** — body speed never exceeds gait cap (outside slide/knockback)
4. **Bot Parity** — The Stranger is ledger-indistinguishable from a human player

Violations are logged, never auto-fixed. The motion audit doubles as an anti-cheat heuristic floor.

## Networking Seams

The codebase is architected for a future dedicated server + lag compensation upgrade. The seam is isolated at two RPC points:
- `Revolver.RequestAdjudication` — gunshot hit resolution
- `Revolver.RequestWhip` — melee hit resolution

Beta runs host-authoritative via S&box lobby. The upgrade path requires changes at these two points only.
