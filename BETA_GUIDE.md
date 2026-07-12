# TEN PACES — Beta Implementation & Playtest Guide

*How to take this codebase to a playable build in S&box, and how to test the game design with real players once it runs.*

This guide is the bridge between the code (complete) and a beta (a compile pass, greybox scenes, a sound bank, and a cohort away). It assumes the ground-truth Design Document and Feel Document, extends `BUILD_NOTES.md`, and never contradicts either. Two audiences share it: the engineer wiring the scene, and the designer running the playtests. Read Parts 1–4 to build it; Parts 5–6 to learn from it.

---

## Part 0 — What "beta-ready" means here

Feature-complete, greybox, instrumented, and honest. In the box: the full match loop, 3 Forms, 3 Tricks, all movement tech, the complete audio-information system, changeover loadout swaps, The Stranger ×4 tiers, the Cadence Trainer, spectating, the Record, telemetry, honesty auditing, both input devices. Deliberately deferred (seams built): dedicated ranked servers, the Territory map, voice bank, final art. See `BUILD_NOTES.md §IV`.

The remaining path, in order: **(1)** compile pass · **(2)** greybox scenes + prefabs · **(3)** sound bank · **(4)** cohort playtest. Parts 1–3 are the engineer's; Part 6 is the designer's.

---

# PART I — IMPLEMENTATION (S&BOX)

## Part 1 — From checkout to a running build

### 1.1 Prerequisites
- [S&box](https://sbox.game) with the scene-system editor (Source 2).
- The code targets the S&box API as of early 2026. S&box moves fast — **the first task is a compile pass.** Expect mechanical fixes (property/method renames), never design changes. Compiler config (from `tenpaces.sbproj`): root namespace `TenPaces`, defines `SANDBOX;ADDON;DEBUG`, `NoWarn 1701;1702;1591`.

### 1.2 The compile pass — likely touch-points
The API surfaces most likely to have drifted (audit before assuming a bug):
- `[Sync]` / `[Rpc.Host]` / `[Rpc.Broadcast]` attribute shapes and `Rpc.Caller`.
- `Scene.Trace.Ray(...).Run()` and `SceneTraceResult` members (`.Hit`, `.GameObject`, `.HitPosition`, `.Normal`, `.Surface`, `.Distance`).
- `CharacterController` (`Accelerate`, `ApplyFriction`, `Move`, `Punch`, `Velocity`, `IsOnGround`).
- `GameObject.NetworkSpawn()` and `Clone(...)` — used by the lobby and (new this pass) by trick spawning.
- `ColorAdjustments`, `SkinnedModelRenderer.Set(...)`, Razor `PanelComponent` / `BuildHash`.
- Input action names (see `tenpaces.sbproj` bindings and Part 8).

### 1.3 Scene assembly — the singletons
One playable scene needs one of each (in the scene root) plus a Ground and a duelist prefab reference:

| Component | Role |
|---|---|
| `MatchDirector` | Host-authoritative 8-phase state machine; owns `TennisScore` (JSON-synced) |
| `Umpire` | Structural sports grammar — calls derived from score state |
| `SoundLedger.Broadcaster` | The networked honest-sound bus (every listener subscribes here) |
| `FeelTelemetry` | Per-match KPI collection → flushed to `telemetry_*.json` |
| `HonestyAuditor` | Always-on runtime CI (4 audits) |
| `LobbySystem` | Connection, seating, spectators — **set `DuelistPrefab`** |

Plus: a `Hud` Razor panel in the scene, and `ScreenEffects` on the camera (it now auto-adds `ColorAdjustments` if absent). For the practice range, add a `CadenceTrainer` with a `BottleRowRoot`.

### 1.4 The duelist prefab
- `DuelistController` (auto-requires `CharacterController`; auto-creates `Revolver`, `Cylinder`, `Vitals` — but author them explicitly for wiring). Set `Eye` and `HatSocket` object references.
- Child object with `HatComponent` (set `HatModel`, `Socket`).
- A `SkinnedModelRenderer` (the AnimGraph target — see 3.2).
- Assign as `LobbySystem.DuelistPrefab`. Body is sized in code (`Height 72`, `Radius 14`) and tagged `duelist` (firm bodies [LAW]).

### 1.5 The Ground (greybox first)
Add `GroundDefinition`:
- ≥1 gate per side (`GatesSideA`/`GatesSideB`); **three per side** is the intended route grammar.
- `ResolveVolumeCenter` transform + `ResolveRadius` (default 350u) — the Reckoning heart.
- `HighNoonLightRig` + `DuskLightRig` (Initiative holder picks the Hour).
- Optional ambient masking (`MaskEventPeriod/Duration/Sound`) — deterministic, host-clocked, learnable cover.

The code runs on dev-box geometry today; greybox *Main Street* and *The Chapel* are the first two Grounds (`BUILD_NOTES §IV`).

### 1.6 Solo play (The Stranger)
Add a `DrillBot` (pick a `Tier`) to a second duelist-prefab instance. It drives the identical `DuelistController` through the virtual input shim (`BotMove`/`BotPress`/`BotHold`) — same accuracy matrix, same ledger, same physics. Difficulty is discipline, never aim. Start players against **Drifter**: it teaches by being readable.

### 1.7 Startup
`tenpaces.sbproj` `StartupScene = /Assets/scenes/lobby.scene` (create it). `MapList` references `ground_mainstreet` / `ground_chapel`.

---

## Part 2 — Architecture you must preserve (the contracts)

Changing any of these is a design-doc amendment, not a refactor.

### 2.1 The 8-phase state machine (`MatchDirector`, host-authoritative)
`Lobby → CoinFlip → InitiativePick → Approach → Live → Reckoning → PointCeremony → Changeover` (+ `SetBreak`, `MatchEnd`). `MatchEnd` now times out (`Tuning.MatchEndHold`) → resets and returns to Lobby. Every phase timeout and transition is host-only; `OnFixedUpdate` returns immediately off-host.

- **`TennisScore` is the brain, the Director is the body.** Score is JSON-serialized into `[Sync] ScoreJson`; clients rebuild via `ClientScore`. All scoring logic lives in `TennisScore` — never re-derive it elsewhere.
- **`OnDuelistKilled` is the *only* point-award path** (called by `Vitals` on the host). Do not add a second.

### 2.2 The networking model (beta = host-authoritative)
- `[Sync]` public props for auto-networked state; `[Rpc.Host]` for host-only authority (adjudication, loadout, initiative pick, **tricks**); `[Rpc.Broadcast]` for narrative/presentation (umpire calls, muzzle, impact, hitstop, hat, smoke).
- **Hit adjudication:** client fires → computes deterministic spread locally → host resolves the trace with plausibility validation (origin within 24u) → result broadcast. Determinism [LAW]: the spread is a fixed golden-angle spiral indexed by shot count — zero RNG, identical everywhere.
- **The dedicated-server upgrade seam is exactly two RPC points:** `Revolver.RequestAdjudication` (gunshot) and `Revolver.RequestWhip` (melee). Full lag-comp rewind plugs in there and *nowhere else*. Keep it that way.
- **Tricks are host-authoritative** (fixed this pass): `Revolver.UseTrick` → `[Rpc.Host] RequestTrick` → host spawns + `NetworkSpawn`s the coin/vial/knife, simulates flight, resolves knife damage host-side. Never spawn gameplay objects client-locally — that broke non-host knives and opponent-visible smoke.

### 2.3 The Sound Ledger covenant (`SoundLedger`) [LAW]
Every gameplay sound routes through `SoundLedger.Report/ReportAt` → `Broadcaster` `[Rpc.Broadcast]`. Nothing is client-local, suppressed, or faked. Four listeners subscribe to the same stream via `ISceneEvent<ILedgerEvents>`: **FeelTelemetry, the DrillBot's ears, the tell-cam, the HonestyAuditor.** One reality, many listeners. Radius is data (`Tuning`); attenuation/occlusion is the audio engine's. When you add a sound, it goes through the ledger — a bare `Sound.Play` for gameplay is a covenant break the auditor should eventually catch.

### 2.4 `Tuning.cs` is the single source of truth
Every gameplay number lives there, annotated `[LAW]` (design law, needs an amendment) or `[BASELINE]` (expected to move in beta). Hot-reload means editing it retunes a live playtest in seconds — **this file is the tuning workflow.** This pass pulled leaked feel-numbers (hit-zone geometry, trick numerics, landing bloom, auditor slack, wound grade) back into it. Keep new numbers here; SCSS/AnimGraph values that can't reference C# are the only sanctioned duplicates (flag them).

### 2.5 The laws (grep `[LAW]`) — the non-negotiables
Holstered run is the only fast state · planted-aimed on the beat = 0.0° TRUE · zero-RNG ballistics · wounds never slow (`WoundedSpeedMult = 1.0`) · the heartbeat is private to the wounded player · every sound is networked and honest · one hitstop: the kill (40 ms) · the reload is countable · animation never lies (synced state *is* the animgraph input) · nothing sold touches a duel.

---

## Part 3 — Assets required for beta

Non-code work; the code runs asset-less-with-log in greybox so playtests aren't blocked on art.

### 3.1 The sound bank (~30 `tp.*` events)
Every event name is enumerated in `SoundLedger.EventName` and the Forms' `DrawSound`. Author `.sound` assets; missing ones fail silent-with-log. Audible radii are in `Tuning` (Audio Ledger section) — the ledger decides *who could hear*, the `.sound` asset + Steam Audio decides *how it sounds*:

| Class | Events | Radius (u) |
|---|---|---|
| Gunshot | `tp.gunshot` | map-wide |
| Run / Walk / Soft | `tp.footstep.run/walk/soft` | 900 / 450 / 90 |
| Draw (per Form) | `tp.draw_duelist/deadeye/fanning` | 600 |
| Reload chain | `tp.gate_open/shell_eject/shell_insert/shell_dropped/gate_close` | ~500 (countable [LAW]) |
| Movement | `tp.jump/land_heavy/slide/mantle/bootscuff` | 700–900 / 90 |
| Melee | `tp.whip_swing/whip_connect` | 550 |
| Beat | `tp.hammer_click` (self+whisper) / `tp.dead_click` | 120 |
| Tricks | `tp.coin_land/vial_throw/vial_break/knife_throw/knife_impact` | per-trick |
| Private/UI | `tp.heartbeat` (2D, wounded only), `tp.hat_off`, `tp.crack_past`, `tp.bottle_shatter`, `tp.streak_chime`, `tp.ambient.mask` | — |

Steam Audio geometric propagation on both Grounds. The Umpire's call grammar is finite and enumerated by `Umpire.cs` — a VO recording sheet falls straight out of the code.

### 3.2 Animation set — the AnimGraph contract
`DuelistController.UpdateAnimationTruth()` pushes synced state into these params every frame (animation never lies [LAW]). Author the AnimGraph to consume exactly these; the HonestyAuditor's Animation-Truth audit asserts consistency (no aiming/reloading while holstered):

| Param | Type | Source |
|---|---|---|
| `gait` | int | `CurrentGait` |
| `move_state` | int | `State` |
| `planted` | bool | `IsPlanted` |
| `gun_state` | int | `Gun.HandState` |
| `aiming` | bool | `Gun.IsAiming` |
| `reloading` | bool | `Cylinder.IsReloading` |
| `wounded` | bool | `Vitals.IsWounded` |

Third-person needs: gait set, draw/holster/whip/mantle/ragdoll, hat socket. Viewmodel: full internals, 3 Form draw/idle variants, the interruptible reload chain, 4 flourishes.

### 3.3 Grounds
*Main Street* and *The Chapel* — greybox first (the code runs on dev geometry), charter-reviewed per Design Doc §V: small, symmetric, three routes, one contested heart, honest tells. Two Hour light rigs each.

---

## Part 4 — Testing infrastructure (build it before the cohort)

### 4.1 Scoring engine → CI
`Tests/ScoreTest.cs` compiles standalone against `Code/Core/TennisScore.cs` — **no engine dependencies** (portable classic .NET). It runs the rules battery (now including a serialize→deserialize round-trip, best-of-five, deuce/advantage calling, break-at-deuce, tiebreak initiative) plus a 100k×6 Monte Carlo validated against the closed-form probability model. Port to CI as-is; target `/scoretest`. A green run proves the Design Document's central wager is a measured fact of the build.

```
# example CI step (classic .NET / dotnet):
dotnet run --project Tests   # exits non-zero on any failed assertion
```

### 4.2 HonestyAuditor — always-on runtime CI (4 audits)
Runs as a scene component during every playtest; logs violations, never auto-fixes:
1. **Sound Truth** — ledger entry position vs source position (`NetTolerance` 48u).
2. **Animation Truth** — synced mechanical state is internally consistent.
3. **Motion Truth** — gait-speed caps are physical law (doubles as the anti-cheat floor; slack in `Tuning.Audit*`).
4. **Bot Parity** — The Stranger carries the same ledger components a human does.
Surface `HonestyAuditor.Violations` on a dev overlay. Zero violations across a full match is a release gate.

### 4.3 FeelTelemetry — the dashboards, emitted
Flushed to `telemetry_<utc>.json` at match end (and on forfeit, fixed this pass). Per-player aggregates: shots, on-beat/early split, **composure** (`1 − meanEarliness`, clamped), composure-while-wounded, planted-shot rate, plants/slides/slide-plants/mantle-feints/feints/flourishes, shells loaded, hat shots, tricks, best trainer streak. Per-point: winner, duration, end zone. Plus `winProbCheck` (theoretical match prob at observed point split). This is the beta dashboard as data — a notebook over these files IS the analysis.

### 4.4 The Record — the product's data model
`the_record.jsonl` (append-only): timestamp, ground, both names, winner, **set lines** (now real — the tuple-serialization bug is fixed), and `forfeit` flag. Walkovers are honestly marked. The schema is already the ladder's data model.

---

# PART II — TESTING THE GAME DESIGN WITH REAL PLAYERS

## Part 5 — The design under test

Beta is not a bug hunt (that's Part 4). It is an experiment with one hypothesis and several sub-claims.

### 5.1 The central wager (the existential test)
The tennis format converts a small per-duel edge into a decisive match outcome. Verified in the scoring engine, to be verified in the wild:

```
p/point edge → match win probability
   .50 → .500   (coin flip)
   .53 → .790   (a 3-point edge is a 79% match favorite)
   .55 → .910
   .60 → .996
   .75 → 1.000
```

The **`.53` discovery** (`BUILD_NOTES §I`) reframes tuning: because the format's sorting is *stronger* than the docs assumed, the per-point duel can be tuned **more dramatic and upset-prone**. Chase drama per point; trust the math for justice per match.

### 5.2 What players are actually learning (the skill layers to watch adopt)
- **The Plant** — stop → plant → aim → fire on the beat = 0.0° TRUE. The single most important mechanic.
- **The true beat** — the hammer cadence; fire early and the slip-hammer blooms on a deterministic curve. Composure is measurable here.
- **The Forms** — Duelist (balanced, 1600u), Deadeye (precision, 2400u reach, no fan), Fanning (burst, 700u knee). Range falloff now real — Forms genuinely shift *where* the point is won.
- **Sound literacy** — counting an opponent's reload by ear, reading the draw's Form signature, moving when the train passes.
- **Tricks** — Coin (false information), Vial (a curtain for one move), Knife (silent, lethal only on a perfect).
- **The Reckoning** — passive play has a hard ceiling; the volume now collapses at T+12s so stand-ins always resolve.
- **The hat** (amended this pass) — the crown absorbs half a shot and comes off; it shrinks the instant-headshot surface at the start, it is not armor. A face shot still one-shots.

---

## Part 6 — Playtesting with real players

### 6.1 Cohort
300–500 players, **density-concentrated** (two campuses + one city Discord), four weeks. Density matters more than headcount: the callout loop and the "the district is watching" spectator experience only ignite where players overlap in time. Ship the callout as lobby links + friends-list challenges — the atomic social action.

### 6.2 The onboarding funnel (measure drop-off at each step)
1. **Cadence Trainer** — wordless: mash → nothing shatters; ride the click → everything does. The First-Miss and Metronome tests as one object. The streak (now measured by real earliness) is the daily warm-up leaderboard.
2. **Drifter** — the first duels. It over-commits, reloads in the open, telegraphs. Beating it *is* the tutorial.
3. **Deputy → Bounty → The Stranger** — discipline ramps (beat-waiting, plant rate, sound literacy, pattern-breaking), never aim. Reading The Stranger is literally practicing the skill that beats humans.
4. **First human duel** — the callout.

### 6.3 Session protocol
- Fixed builds per week; change **one tuning axis at a time** (hot-reload makes same-session A/Bs possible, but log every change).
- Every session runs the HonestyAuditor; a violation spike is a red flag, investigate before trusting that session's feel data.
- Capture: telemetry JSON, the Record, a short structured survey (clarity, fairness, "did you feel outplayed or cheated?"), and 2–3 think-aloud sessions per week.
- Split **every** metric by input device (KB/M vs pad) — parity is a launch requirement, not a nice-to-have.

### 6.4 The six dashboards (targets & how to read them)

| # | Dashboard | Source | Target / read |
|---|---|---|---|
| 1 | **The p-curve** (existential) | per-point win by rating gap | elite-vs-novice ≥ **.75/point** (`Tuning.TargetElitePerPointWinrate`); adjacent percentiles ≈ **.52–.55** (`0.535`). Too flat → skill invisible; too steep → upsets dead. |
| 2 | **Composure histogram** (Metronome) | earliness distribution | mass near 0 earliness = players riding the beat. A fat early tail early in the cohort that thins over weeks = the beat is being learned. |
| 3 | **Wounded-composure delta** (Hurt) | composure − composureWounded | small delta = players hold their nerve hurt. A large delta means the private heartbeat is doing its job *too* well (or not enough). |
| 4 | **Technique adoption** | slide-plants, feints, mantle-feints, just-frames per player-week | should rise week over week — the discovery rate of the timing layer. Flat = a mechanic isn't being found; check its readability. |
| 5 | **Camping check** | point-duration distribution + Reckoning frequency | **< 8% of points reach the bell.** Above that, the Reckoning pressure or the point clock needs tuning. |
| 6 | **Toy Test** | flourishes outside combat per session | non-zero and social = the gun is a beloved object, not just a tool. |

### 6.5 The tuning loop
- All levers are `[BASELINE]` constants in `Tuning.cs`; edit and hot-reload. Never touch `[LAW]` without a design-doc amendment.
- **Tune per-point drama up, trust the format for match justice** (the `.53` mandate). If the p-curve is healthy but duels feel tame, widen cones/shorten beats before touching match structure.
- First knobs to reach for, by symptom: *camping* → `PointTimeLimit`, `ReckoningTickDmg`, `ReckoningSuddenDeath`; *beat too punishing/forgiving* → `EarlyFireMaxBloom`, `BeatBloomExponent`, per-Form `TrueBeatInterval`; *Forms feel same-y* → `EffectiveRange`, `BeyondRangeConeAdd`, `ConeScale`; *hat feels wrong* → `HatCrownRadius`, `HatAbsorbFraction`; *bot too hard/soft* → the tier discipline knobs in `DrillBot` (reaction, beat, plant, jitter).

### 6.6 Go / no-go for wider release
- p-curve within target band (dashboard 1), on **both** input devices.
- Camping < 8% (dashboard 5).
- Zero HonestyAuditor violations across a representative match sample.
- `/scoretest` green in CI.
- Onboarding funnel: a majority of new players reach their first human duel within their first session.
- No walkover/rematch/reconnect dead-ends across the session (the state-machine fixes from this pass; re-verify with 2-client disconnect drills).

---

## Part 7 — Deferrals & the upgrade seam
Post-beta, all reading existing event streams: Territory map & regional ladders (backend; the Record schema is already the model), dedicated ranked servers + rewind lag-comp (the two `[Rpc.Host]` seams), voice bank, final art, hat-wager tokens, rivalry pages, tell-cam broadcast UI, live win-probability graph (the math already ships in `TennisScore.MatchWinProbability`).

## Part 8 — Controls (quick reference)
KB/M: Move WASD · Jump Space · Slide Ctrl · Soft Step Shift(hold) · Fire M1 · Aim M2 · Draw/Holster Q · Reload R · Whip V · Trick F · Cylinder Check T · Spin G · Hammer Thumb H · Tip Hat B · Ready E · Scoreboard Tab. Gamepad bindings in `tenpaces.sbproj` and `USAGE.md`.

## Appendix — Tuning cheat sheet (by system)
`Tuning.cs` groups: Locomotion · The Revolver (beat, accuracy matrix, range falloff) · Reload Ritual · Pistol-Whip · Wounds & Death (incl. hit-zone geometry, the crown hat) · Match Structure (incl. Reckoning sudden-death, MatchEndHold) · Tricks (throw numerics) · Audio Ledger radii · Camera · KPI Targets · Honesty Auditor slack. Per-Form handling lives in `FormDefinition` (the three baselines double as asset-less defaults).

---

*Companion documents: `README.md` (overview) · `USAGE.md` (setup & controls) · `BUILD_NOTES.md` (gap decisions, §VI records the beta-hardening fixes) · `CLAUDE.md` (agent guidance) · `Assets/ASSET_MANIFEST.md` (asset checklist) · `Code/Tuning.cs` (every number).*
