# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TEN PACES is a Western quick-draw duel game built on S&box (Source 2 scene system). Wild-West gunplay with tennis scoring architecture. C#, namespace `TenPaces`, multiplayer host-authoritative (2 duelists + 6 spectators, 128 tick).

Ground truth lives in the Design Document and Feel Document (companion files outside this repo). In-repo decision log: `BUILD_NOTES.md`. Every tuning constant: `Code/Tuning.cs`.

## Build & Development

**Engine:** S&box (Source 2, scene system). Open `tenpaces.sbproj` in the s&box editor. Compile via the editor — the code targets the scene-system API (early 2026); expect mechanical renames on first compile pass, not design changes.

**Compiler config** (from `tenpaces.sbproj`):
- Root namespace: `TenPaces`
- Define constants: `SANDBOX;ADDON;DEBUG`
- NoWarn: `1701;1702;1591`

**Hot reload:** Editing `Code/Tuning.cs` re-tunes a live playtest in seconds via s&box hot-reload. This file IS the tuning workflow.

## Testing

`Tests/ScoreTest.cs` is the standalone test battery for the scoring engine — compiles against `Code/Core/TennisScore.cs` with no engine dependencies (portable classic .NET). 20 rules assertions + 600k-match Monte Carlo vs closed-form probability. Port to CI as-is; target `/scoretest` in the build environment.

The `HonestyAuditor` component acts as always-on runtime CI during playtests (sound truth, animation truth, gait-speed law, bot parity). It logs violations but never auto-fixes.

## Architecture

The match flows through an 8-phase state machine in `MatchDirector` (singleton, host-authoritative):
**Lobby → CoinFlip → InitiativePick → Approach → Live → Reckoning → PointCeremony → Changeover**

Key architectural relationships:
- `TennisScore` (pure, engine-free) is the match brain — `MatchDirector` is its body. Score state is JSON-serialized and synced; all clients render truth.
- `DuelistController` owns movement and computes the **accuracy authority** (Plant state × Aim state). `Revolver` reads this to determine cone angle — the accuracy matrix lives in `Tuning.cs`, not in Revolver.
- `Revolver` owns the hammer's true beat and the only fire path. `Cylinder` is the public shell ledger (per-shell interruptible reload).
- Every gameplay sound routes through `SoundLedger` (singleton broadcaster). Telemetry, the bot's ears, the tell-cam, and `HonestyAuditor` all subscribe to ledger events — one reality, many listeners.
- `DrillBot` drives the identical `DuelistController` via a virtual input shim (`BotMove`/`BotPress`/`BotHold`). Its skill knobs are discipline (reaction, beat-waiting, plant rate), never aim. No aimbot exists anywhere.
- `Umpire` derives all calls (break point, set point, deuce, etc.) structurally from `TennisScore` queries — no hardcoded strings.

**Duelist prefab wiring:** `DuelistController` (+ `CharacterController`, `Revolver`, `Cylinder`, `Vitals`), child `HatComponent`, a `SkinnedModelRenderer`.

**Scene singletons:** `MatchDirector`, `Umpire`, `SoundLedger.Broadcaster`, `FeelTelemetry`, `HonestyAuditor`, `LobbySystem` (with duelist prefab ref), `Hud` panel, `ScreenEffects` on camera.

## Key Conventions

**Tuning.cs is the single source of truth for all gameplay numbers.** Constants are annotated:
- `[LAW]` — Design law. Requires a design-doc amendment to change. Grep for `[LAW]` across the codebase.
- `[BASELINE]` — Expected to move during beta tuning.

**Design laws** (enforced throughout code):
- Holstered run is the only fast state — drawing forfeits it
- Planted-aimed on the beat = 0.0 degree cone (TRUE)
- Zero RNG in ballistics — deterministic golden-angle spiral
- Wounds never slow (`WoundedSpeedMult = 1.0`)
- Every sound is networked and honest (SoundLedger covenant)
- The reload is countable — every stage is a distinct sound
- Animation never lies — synced mechanical state must match AnimGraph

**Networking patterns:**
- `[Sync]` on public properties for auto-networked state
- `[Rpc.Host]` for host-only methods (hit adjudication, loadout swap)
- `[Rpc.Broadcast]` for narrative events (Umpire calls, bell toll)
- Hit adjudication: client fires, host resolves trace with plausibility validation, result broadcast to all
- Dedicated server upgrade seam is isolated at `Revolver.RequestAdjudication` / `RequestWhip`

**Loadout swaps** (Form + Trick) are legal only during Changeover windows, gated by `MatchDirector.LoadoutSwapAllowed`.

**Sound event naming:** `tp.{sound}.{surface?}` — asset-driven, missing assets fail silent-with-log.
