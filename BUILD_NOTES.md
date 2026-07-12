# TEN PACES — BUILD NOTES
## Gap Decisions, Discoveries, and the Road to Beta

*Companion to the Design Document and Feel Document. This file records every decision made where the documents were silent, every opportunity seized during the build, and the honest state of the project. Nothing here contradicts the ground-truth documents; everything here extends them.*

---

## I. What Was Built

A complete S&box (Source 2, scene system) implementation of the game's mechanical, structural, and informational core — every system from both documents rendered as working C#:

| System | File | Doc Authority |
|---|---|---|
| All tuning constants, single file | `Code/Tuning.cs` | Feel Doc, throughout |
| Tennis scoring engine (pure, tested) | `Code/Core/TennisScore.cs` | Design Doc §VI |
| Match phase machine + Initiative + Reckoning | `Code/Core/MatchDirector.cs` | Design Doc §III, §VI |
| The Umpire (structural sports grammar) | `Code/Core/Umpire.cs` | Feel Doc §VI |
| Movement: gaits, Plant, slide(+plant/fade), mantle(+feint), landings | `Code/Player/DuelistController.cs` | Feel Doc §II |
| Wounds, grazes, private heartbeat, kill adjudication | `Code/Player/Vitals.cs` | Feel Doc §VI |
| Hat physics + the hat shot | `Code/Player/HatComponent.cs` | Feel Doc §VI |
| Forms as data (3 launch schools) | `Code/Weapons/FormDefinition.cs` | Design Doc §IV |
| The revolver: true beat, feint, accuracy matrix, deterministic spread, fanning, whip | `Code/Weapons/Revolver.cs` | Feel Doc §III, §V |
| Per-shell reload ledger | `Code/Weapons/Cylinder.cs` | Feel Doc §IV |
| Coin / Vial / Knife | `Code/Weapons/Tricks.cs` | Design Doc §IV |
| The Honest Sound Ledger | `Code/Audio/SoundLedger.cs` | Feel Doc §VIII |
| Ground contract: gates, Hours, resolve volume, masking events | `Code/Grounds/GroundDefinition.cs` | Design Doc §V |
| Lobby, seating, callout format, spectators | `Code/Net/LobbySystem.cs` | Design Doc §VIII, §XI |
| The Stranger (drill bot, 4 tiers) | `Code/Practice/DrillBot.cs` | new — see §III |
| Cadence Trainer (wordless tutorial) | `Code/Practice/CadenceTrainer.cs` | new — see §III |
| Feel telemetry (the 12 assertions + p-curve) | `Code/Telemetry/FeelTelemetry.cs` | Feel Doc §XI |
| Honesty Auditor (runtime CI for the covenant) | `Code/Telemetry/HonestyAuditor.cs` | new — see §III |
| HUD: scorecard, pips, honest crosshair, overlays | `Code/UI/Hud.razor` + `.scss` | Feel Doc §VII |
| Hitstop, wound grade, ceremony effects | `Code/UI/ScreenEffects.cs` | Feel Doc §VI |

**Verified in this build environment:** the scoring engine was extracted, compiled standalone, and battle-tested — 20/20 rules assertions pass (deuce, tiebreak, breaks, match points, score-calling), and a 600k-match Monte Carlo confirms the closed-form probability model to three decimals. The design's central claim is now a *measured fact of this codebase*:

```
p/point → p/match:   .50→.500   .53→.790   .55→.910   .60→.996   .75→1.000
```

Note the .53 row — it wasn't in either document and it's the most important number found during the build: **a 3-point per-duel edge is a 79% match favorite.** The sorting power of the format is even stronger than the docs claimed. This tightens the tuning brief: the per-point game can afford to be *very* upset-friendly.

---

## II. Gap Decisions (where the documents were silent)

Recorded in the order they were forced. Each is reversible; each has a rationale.

1. **Damage numerics.** Perfect = 100, body = 50, whip = 25, knife body = 25 (knife perfect = lethal, per doc law). Graze shell = 1.18× capsule. The whip's damage is deliberately below "two whips + one body = kill" thresholds mattering often — it's a tempo weapon and the numbers keep it one.

2. **Reckoning mechanics.** Rather than literal wall-closing (asset-heavy, netcode-fussy), the Reckoning is a **resolve volume + pressure model**: bell at T-15, at T-0 a 6-second grace to reach the arena's heart, then 10 hp/s outside it. Grounds theatrically dress this (cover recession hooks exist via `IGroundEvents.OnReckoningBegan`) but the mechanic is pure and tunable. Passivity has a hard ceiling; the standoff at center emerges from pressure, not scripting.

3. **Initiative's exact powers.** Holder picks the Hour **and their own gate**; receiver's gate is hidden-random in beta (a receiver-preference UI is a fast follow). Ten-second pick window, auto-random on timeout. Tiebreak Initiative alternates every two points (tennis-correct).

4. **The early trigger is never locked out.** The hammer will slip-fire before the beat — bloomed, on a deterministic earliness curve (exp 1.6). A locked trigger would be a rule; a slipping hammer is a *physical truth*, and panic staying possible is what makes composure measurable.

5. **Deterministic spread implementation.** Golden-angle spiral indexed by shot count. Zero RNG anywhere in ballistics; the "spread" is a fixed, learnable pattern — the CS covenant, kept literally.

6. **Whiff cost mechanics.** A whiffed whip doesn't use an animation lock alone; it *sets the hammer clock back* (the gun is out of position → the next true beat is late). Costs expressed inside the cadence system stay legible to the ear.

7. **Host-authoritative beta netcode.** s&box lobby, host adjudicates all hits with plausibility validation (origin-drift and range checks). Dedicated servers with full lag-compensation rewind are the ranked-season upgrade; the seam is isolated in `Revolver.RequestAdjudication` / `RequestWhip`.

8. **Walkovers are recorded.** Disconnection mid-match forfeits and the Record says so explicitly. Prestige systems die of quiet data corruption; honesty about walkovers is ladder integrity day one.

9. **Spectators are in-scene.** Lobby capacity 8: two seats + a local crowd with POV/overhead camera cycling. The "district watches the title defense" experience is native from the first playtest, not a broadcast add-on.

10. **Crosshair honesty.** The dot's breathing ring is driven by the *actual* accuracy-cone function — the same call the bullet uses. The UI cannot flatter you.

---

## III. Opportunities Seized (new systems the documents didn't ask for)

**1. The Stranger** (`DrillBot.cs`) — the largest addition, solving three problems at once:
- *Onboarding*: the Drifter tier teaches by being readable — it over-commits, reloads in the open, telegraphs honestly. Beating it is the tutorial.
- *Geographic cold start* (Design Doc §XII risk #3): an empty county always has someone to call out.
- *Pattern pedagogy*: the bot runs on explicit **tendencies** (route bias, preferred range, aggression, feint rate) that persist across points and re-roll only at changeovers — exactly the cadence at which humans adapt. Reading The Stranger is literally practicing the skill that beats humans.
Crucially, its difficulty knobs are *discipline knobs* (beat-waiting rate, plant rate, reaction delay) — better bots are more composed, never faster, and it drives the identical controller/accuracy path humans do via a virtual input shim. **No aimbot exists anywhere in this codebase.**

**2. The Cadence Trainer** (`CadenceTrainer.cs`) — a row of bottles that is simultaneously the wordless tutorial (mash → nothing shatters; ride the click → everything does), the daily warm-up ritual, and a streak leaderboard where just-frames count double. The streak measures composure, not luck: bloomed shots that hit anyway reset it. First-party CS-aim-maps, on-message.

**3. The Honesty Auditor** (`HonestyAuditor.cs`) — the Honest World Test as always-on runtime CI. Four continuous audits: sound-position truth, animation-state truth, gait-speed physical law, bot ledger parity. It never fixes anything; it exists so lies cannot ship quietly. The motion audit doubles as the anti-cheat heuristic floor for beta.

**4. Break-point grammar, structural.** The Umpire detects break/set/match points, breaks of Initiative ("Game, Reyes — *the break*"), deuce, and tiebreaks entirely from `TennisScore` queries. A century of tennis broadcast tension ships as ~40 lines of derived state.

**5. The `.53 discovery`** (§I above) — reframes per-point tuning: the duel can be tuned *more* dramatic and upset-prone than the Feel Doc assumed, because the format's sorting is stronger than estimated. Beta tuning brief updated accordingly: chase drama per point; trust the math for justice per match.

**6. Live win-probability for broadcast** — `TennisScore.MatchWinProbability` runs client-side from observed point splits; the telemetry report already emits it. The Watch tab's win-prob graph is a UI task, not a math task.

**7. Deterministic ambient masking** — the Ground's train/wind/piano schedule is host-clocked and broadcast, so "move when the train passes" is a *fair, learnable* skill (and the bot learns it too, via the same event).

---

## IV. Beta Scope (what "feature complete, beta-ready" means here)

**In the box:** full match loop (quick-format single game + best-of-three), 3 Forms, 3 Tricks, all movement tech, the complete audio-information system, changeover loadout swaps, The Stranger ×4 tiers, Cadence Trainer, spectating, the Record (local + s&box stats), telemetry, honesty auditing, both input devices.

**Deliberately deferred (with seams built):**
- *Territory map & regional ladders* → backend service; the callout loop ships as lobby links + friends-list challenges, which is the atomic social action anyway. The Record schema is already the ladder's data model.
- *Dedicated ranked servers + rewind lag-comp* → seam isolated at the two `[Rpc.Host]` adjudication points.
- *Voice bank* (Umpire), *animation sets*, *the two shipped Grounds' art* → asset manifest below.
- *Hat wager tokens, rivalry pages, tell-cam broadcast UI* → post-beta, all reading existing event streams.

**Asset manifest (the non-code work between here and beta):**
- Grounds: *Main Street* and *The Chapel* — greybox first (the code runs on dev-box geometry today), charter-reviewed per Design Doc §V. Two Hour light rigs each.
- Viewmodel revolver: full internals, 3 Form draw/idle variants, reload chain, 4 flourishes.
- Third-person duelist: gait set, draw/holster/whip/mantle/ragdoll, hat socket. AnimGraph parameters already defined by `UpdateAnimationTruth()`.
- Sound bank: every `tp.*` event named in `SoundLedger` + `Tuning` radii — ~30 events; Steam Audio geometric propagation on both Grounds.
- Umpire VO: the call grammar is finite and enumerated by `Umpire.cs`; a recording sheet falls straight out of the code.

**Beta cohort & KPI plan:** 300–500 players, density-concentrated (two campuses + one city Discord), four weeks. The dashboards are already emitted by `FeelTelemetry`:
1. **The p-curve** (existential): per-point winrate by rating gap → validate ≥.75 elite-vs-novice, .52–.55 adjacent.
2. **Composure histogram** (Metronome Test) and **wounded-composure delta** (Hurt Test).
3. **Technique adoption curves**: slide-plants, feints, mantle-feints, just-frames per player-week — the discovery rate of the timing layer.
4. **Camping check**: point-duration distribution + Reckoning frequency (target: <8% of points reach the bell).
5. **Input parity**: every metric above, split by device.
6. **Toy Test**: flourish events outside combat, per session.

---

## V. Honest Statement of Condition

This codebase is the complete mechanical implementation of both documents — every system, every law, every named technique — architected for s&box's scene system (Components, `[Sync]`, `[Rpc.*]`, Razor panels) and organized so that all tuning lives in one hot-reloadable file. The scoring core is compiled and proven in this environment. The remainder of the code targets the s&box API as of early 2026; s&box's API moves quickly, so the first task on an engine-connected machine is a compile pass (expect mechanical fixes — property renames, not design changes), followed by greybox scenes wiring the prefabs this code expects (duelist prefab with controller/vitals/revolver/hat, a Ground with gates and rigs, the singleton objects: MatchDirector, Umpire, Ledger Broadcaster, Telemetry, Auditor, HUD).

What stands between this and a playable beta is, in order: the compile pass, two greybox Grounds, the animation set, and the sound bank. What does *not* stand between this and beta is design work — every decision has been made, recorded, and where possible, proven.

---

## VI. Beta-Hardening Pass — Audit Fixes (post-build)

*A full-codebase audit ahead of beta — combat/player, core/net/telemetry, and audio/grounds/bot/UI, cross-referenced against `Tuning.cs` and the laws — surfaced two critical bugs, a set of high-severity correctness/authority defects, and several inert "flagship" systems (coded but doing nothing). All code-side fixes have landed. The design is unchanged except one recorded amendment (the hat, §VI.3). Nothing here contradicts the ground-truth documents; it makes the code finally tell their truth. The remaining launch gap is still the compile pass + greybox scenes + sound bank (§V) — this pass did not touch that.*

### 1. Critical & match-breaking (multiplayer correctness)

| System | Was | Now |
|---|---|---|
| **Tricks** (`Tricks.cs`, `Revolver.cs`) | `new GameObject` spawned client-locally; a non-host player's thrown **knife never damaged** (object never reached the host) and a **vial's smoke was invisible to the opponent** | `UseTrick` routes through an `[Rpc.Host]`; the host spawns, simulates, and **NetworkSpawns** every trick object, resolves knife damage host-side, and the smoke volume + presentation exist on all clients |
| **Kill hitstop** (`ScreenEffects.cs`) | window measured in *scaled* time while `TimeScale=0.02` → the one-hitstop [LAW] froze the world ~2 seconds | measured in real (unscaled) time → the intended 40 ms |
| **Set history** (`TennisScore.cs`) | `List<(int,int)>` — `System.Text.Json` silently dropped the tuple's fields, so every synced scorecard and every line of **The Record** came out blank | a `SetScore` struct with real properties (implicit tuple conversion preserves call sites); round-trip now covered by a test |
| **MatchEnd** (`MatchDirector.cs`) | terminal deadlock — no duration, no switch case, no reset; the session stuck after one match | timed victory ceremony → `ReturnToLobby` with full score/seat/duelist reset |
| **Reckoning** (`MatchDirector.cs`) | two duelists both standing at the heart bled no one → the point (and match) hung forever | the resolve volume **collapses** past `ReckoningSuddenDeath` (12s) — pressure then applies to everyone; a stand-in always resolves |
| **Walkover** (`LobbySystem.cs`, `FeelTelemetry.cs`) | forfeit recorded, but the Director kept running with a null duelist; telemetry never flushed; a stale pre-match ready-seat blocked all future starts | disconnect calls `MatchDirector.ForfeitMatch` (ends the match the normal way + flushes telemetry); ready-seats self-heal |
| **Hat reset** (`HatComponent.cs`) | host-only, so remote clients kept the worn hat hidden and leaked the loose-hat prop every point — an "animation never lies" [LAW] violation | the per-point reset broadcasts (mirroring the knock-off), restoring the worn hat and clearing the prop on every client |

### 2. Inert flagship systems, made real

- **Form range falloff** (`Revolver.cs`, `FormDefinition.cs`): `EffectiveRange`/`BeyondRangeConeAdd` were authored per-Form but never read — all Forms behaved identically at range. The cone now opens past the Form's effective range, giving **Deadeye** its reach and **Fanning** its short-range knee. (`FormDef` is also cached now instead of re-allocated every tick.)
- **The Stranger** (`DrillBot.cs`): `BeatDiscipline` was dead (`BotBeatReady() => true`) and the bot never held Aim or Attack — it could never wait for the beat, never reach the planted-aimed 0° TRUE cone, and a Fanning bot could never fan. It now reads the real beat clock and drives Aim/Fan through the same input shim a human uses. **No aimbot** still holds — it only steers `EyeAngles`, never injects a shot vector.
- **Bot input shim** (`DuelistController.cs`): `ActionPressed` consumed the press on first read (`HashSet.Remove`), so any action read twice a tick broke for bots — bots literally could not mantle. Double-buffered so a press is visible to every reader within the tick.
- **Cadence Trainer** (`CadenceTrainer.cs`): the streak evaluated every shot as if perfectly on-beat (earliness hard-coded 0), so the Metronome Test it exists to teach wasn't measured. It now feeds the shot's actual earliness — a slip-hammer shot breaks the streak.
- **Honesty Auditor** (`HonestyAuditor.cs`): two of the four advertised audits (Animation Truth, Bot Parity) were unimplemented. Added: Animation-Truth as state-consistency (aiming/reloading while holstered is a lie), and Bot-Parity as ledger-component parity. Slack moved to `Tuning`; violation log ring-buffered.

### 3. Design amendment — the Hat

The hat was blanket headshot immunity: any hit within 7u of the socket became a zero-damage graze, eating even a genuine head Perfect. Amended (and recorded here as the decision): **the hat protects the crown only** — a face/front head shot is still a one-shot kill — and a crown hit **absorbs half the incoming damage** as the hat is knocked off. It shrinks the instant-headshot surface at match start; it is not sustainable protection. The heart disc is unchanged (and now correctly front-of-chest — see below).

### 4. Correctness & law-consistency

- **Heart hitbox** (`Vitals.cs`): the forward offset was a `*0f` placeholder, so the heart was radially symmetric — a back shot killed like a chest shot. It's now a front-of-chest disc; a back shot at the same height is a Body hit.
- **Tuning single-source-of-truth**: the test's shadow copy of the elite KPI (`Tuning2`) is gone — it references the real `Tuning.TargetElitePerPointWinrate`/`TargetAdjacentPerPointWinrate`. Feel-critical numbers that had leaked into code (the entire hit-zone geometry, landing-bloom magnitude, wound saturation, trick offsets/speeds, auditor slack, ceremony grade) were pulled back into the Book of Numbers so they hot-reload.
- **Tiebreak initiative** (`TennisScore.cs`): entering the breaker now passes first serve to the side that didn't serve game 12 (tennis-correct; was a cosmetic off-by-one in the call).
- **`LastCall`** (`Umpire.cs`): the synced umpire mirror was written by a discarded reflection lookup (a no-op) — now host-written so late-joiners see the last call.
- **Robustness**: singleton `Instance` cleared on destroy (Director/Umpire/Telemetry/Ledger); `GetGate` null-slot guard; composure clamped; point-log guarded against an end-without-start; wound grade auto-provides its post-process; crosshair breathing raised 4→30 Hz.

### 5. Test battery — expanded

`Tests/ScoreTest.cs` gained the highest-value missing coverage: a **serialize→deserialize round-trip** (the guard that would have caught the set-history bug), best-of-five, deuce/advantage score-calling, break-point-at-deuce, and tiebreak-initiative. The existing 20 rules assertions + Monte Carlo are unchanged. (Not re-run in this environment — no .NET toolchain here; run `/scoretest` on the engine machine.)

### 6. New `Tuning.cs` constants (all `[BASELINE]`)

Hit-zone geometry (`ZoneHead/Heart/Body/Graze*`), the hat crown (`HatCrownRadius`, `HatCrownDrop`, `HatAbsorbFraction`), `LandBloomCone`, `RangeFalloffMaxMult`, trick numerics (`Trick*Offset`, `*Radius`, `Coin/Vial ThrowLift/Speed`, `KnifeGravity/Lifetime`), Reckoning `SuddenDeath`, `MatchEndHold`, wound grade, and auditor slack — the levers this pass touched now all live where beta tuning expects them.
