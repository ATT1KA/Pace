# TEN PACES

*A dueling shooter. A social ladder. A sport.*

Wild-West quick-draw duels meet arena-shooter discipline and tennis's scoring architecture. Two players, one revolver each, three shooting Forms, honest sound, deterministic bullets, and a match format mathematically proven to convert small per-point edges into decisive outcomes.

Built for [S&box](https://sbox.game) (Source 2, scene system) in C#.

```
p/point edge → match win probability
     .50     →  .500  (coin flip)
     .53     →  .790  (3% edge = 79% match favorite)
     .55     →  .910
     .75     → 1.000
```

## The Game

Two duelists face off in small, symmetric arenas. A coin flip decides Initiative — the right to choose the Hour (lighting) and your spawn gate. Both players approach from opposite ends. Then it's live.

You have a six-shooter. One revolver, no pickups, no loadout asymmetry. Your gun has a **true beat** — a hammer cadence that rewards timing over mashing. Fire on the beat for perfect accuracy. Fire early and the hammer slips, blooming your shot on a deterministic curve. The spread pattern is a learnable golden-angle spiral. There is zero RNG in ballistics.

Points are scored through kills. Points accumulate into games (15-30-40-deuce-advantage), games into sets (first to 6, win by 2, tiebreak at 6-6), sets into matches (best of 3 or 5). The tennis format amplifies skill: a player who wins just 53% of individual duels wins 79% of matches.

### Movement

- **Holstered Run** (330 u/s) — the only fast state; drawing your weapon forfeits it
- **Drawn Walk** (230 u/s) — combat gait
- **Soft Step** (120 u/s) — near-silent creep, held via Shift
- **The Plant** — stop moving to plant your feet. Planted + aimed + on the beat = 0.0° cone (perfect accuracy). This is the game's most important mechanic.
- **Slide** — 0.70s commitment with a speed burst; slide-plant in the final quarter for a tech option
- **Mantle** — chest-high ledges, cancelable in the first third for mantle-feints

### The Revolver

Three Forms define your shooting school (swappable at changeovers):

| Form | Draw | Beat | Cone | Range | Special |
|------|------|------|------|-------|---------|
| **Duelist** | 0.45s | 0.50s | ×1.0 | 1600u | Balanced |
| **Deadeye** | 0.70s | 0.65s | ×0.75 | 2400u | Precision, no fanning |
| **Fanning** | 0.60s | 0.55s | ×1.25 | 700u | Burst up to 3 @ 0.12s |

The reload is a per-shell, interruptible ritual — gate open, eject, insert, gate close — every stage a distinct, countable sound. A full six-shell reload takes ~4.5 seconds of loud vulnerability.

### Tricks

One per point, chosen at changeover alongside your Form:

- **Coin** — silent deceptive throw; lands with a footstep-class sound at a position of your choosing
- **Vial** — smoke grenade; glass break is audible, smoke provides concealment
- **Knife** — thrown blade; silent, lethal on perfect hit, 25 damage on body

### The Sound Ledger

Every gameplay sound is networked and honest. Nothing is suppressed, nothing is faked. Drawing your weapon is a declaration heard at 600 units. Reloading is countable by ear. Running footsteps carry at 900 units; soft steps at 90. The sound system is the information system — the bot's ears, the spectator camera, and the honesty auditor all subscribe to the same ledger.

### Match Structure

Each point runs on a 90-second clock. If the timer expires, the **Reckoning** begins: a bell tolls at T-15, and at T-0 both players have 6 seconds to reach the arena's heart or take 10 hp/s pressure damage. Passive play has a hard ceiling.

After odd-numbered games, a 45-second **Changeover** window opens — the adaptation break. This is the only time you can swap your Form and Trick. Read your opponent, adjust, re-engage.

## The Laws

Design laws enforced throughout the codebase (grep `[LAW]`):

- Holstered run is the only fast state
- Planted-aimed on the beat is TRUE (0.0° cone)
- Zero RNG ballistics
- Wounds never slow you
- The heartbeat is private (only the wounded player hears it)
- Every sound is networked and honest
- One hitstop: the kill
- The reload is countable
- Animation never lies
- Nothing sold touches a duel

## Project Structure

```
tenpaces.sbproj              S&box project file
Code/
  Tuning.cs                  Every gameplay constant — the single source of truth
  Core/
    TennisScore.cs            Pure scoring engine (standalone, no engine deps, verified)
    MatchDirector.cs          8-phase host-authoritative state machine
    Umpire.cs                 Sports grammar — calls derived structurally from score state
  Player/
    DuelistController.cs      Movement, gaits, Plant, accuracy authority
    Vitals.cs                 Health, wounds, kill adjudication
    HatComponent.cs           Hat physics + hat shot system
  Weapons/
    Revolver.cs               Hammer beat, accuracy matrix, deterministic spread, forms
    Cylinder.cs               6-shell reload ledger
    FormDefinition.cs         3 shooting schools as data
    Tricks.cs                 Coin / Vial / Knife
  Audio/
    SoundLedger.cs            Networked honest sound — the information system
  Grounds/
    GroundDefinition.cs       Arena contract: gates, Hours, resolve volume, masking
  Net/
    LobbySystem.cs            Lobby, seating, spectators
  Practice/
    DrillBot.cs               The Stranger — 4-tier bot (discipline knobs, not aimbot)
    CadenceTrainer.cs         Wordless tutorial + daily warm-up + streak board
  Telemetry/
    FeelTelemetry.cs          12 KPIs per match (p-curve, composure, adoption)
    HonestyAuditor.cs         Always-on runtime CI for the honesty covenant
  UI/
    Hud.razor / .scss         Scorecard, shell pips, honest crosshair
    ScreenEffects.cs          Hitstop, wound feedback, ceremony effects
Tests/
  ScoreTest.cs                20 rules assertions + 600k Monte Carlo verification
Assets/
  ASSET_MANIFEST.md           Non-code work checklist for beta
```

## Getting Started

See [USAGE.md](USAGE.md) for setup instructions, scene wiring, controls, and tuning workflow.

## Testing

`TennisScore.cs` compiles standalone with no engine dependencies. The test battery in `Tests/ScoreTest.cs` covers rules correctness (20 assertions) and Monte Carlo validation against the closed-form probability model (600k matches). Port to CI as-is.

The `HonestyAuditor` runs as always-on runtime CI during playtests — four continuous audits (sound truth, animation truth, gait-speed law, bot parity) that log violations without auto-fixing.

## Documentation

| File | Purpose |
|------|---------|
| `README.md` | This file — project overview |
| [USAGE.md](USAGE.md) | Setup, controls, scene wiring, tuning |
| [BETA_GUIDE.md](BETA_GUIDE.md) | Beta implementation (S&box) + testing the design with real players |
| [BUILD_NOTES.md](BUILD_NOTES.md) | Gap decisions, discoveries, beta scope, project state |
| [CLAUDE.md](CLAUDE.md) | Guidance for Claude Code agents |
| [Assets/ASSET_MANIFEST.md](Assets/ASSET_MANIFEST.md) | Non-code asset work checklist |
| `Code/Tuning.cs` | Every gameplay number in one file |

## License

All rights reserved.
