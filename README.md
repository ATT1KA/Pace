# TEN PACES
*A dueling shooter. A social ladder. A sport.*

Wild-West quick-draw duels × Halo-class arena discipline × tennis's scoring architecture, built for S&box (Source 2, scene system). One revolver, three Forms, honest sound, deterministic bullets, and a match format that converts small per-point edges into near-certain outcomes — verified in this repo (`.50→.500, .55→.910, .75→1.000`).

**Ground truth:** the Design Document and Feel Document (companion files). **Decisions & state:** `BUILD_NOTES.md`. **Every number:** `Code/Tuning.cs`.

## Quick start
1. Open the project in the s&box editor (`tenpaces.sbproj`). Run a compile pass — the code targets the scene-system API (early 2026); expect mechanical renames only.
2. Open/create a scene containing the singletons: `MatchDirector` (+ a `GroundDefinition` with ≥1 gate per side), `Umpire`, `SoundLedger.Broadcaster`, `FeelTelemetry`, `HonestyAuditor`, `LobbySystem` (with duelist prefab), a `Hud` panel, `ScreenEffects` on the camera.
3. Duelist prefab: `DuelistController` (+auto: `CharacterController`, `Revolver`, `Cylinder`, `Vitals`), child `HatComponent`, a `SkinnedModelRenderer`.
4. Add a `DrillBot` component to a second duelist prefab instance to fight The Stranger solo.
5. Press **E** to ready. First two ready players take the ground. Coin flip → Initiative pick → the Approach → live.

## The laws (grep `[LAW]`)
Holstered run is the only fast state · planted-aimed on the beat is TRUE · zero RNG ballistics · wounds never slow · the heartbeat is private · every sound is networked and honest · one hitstop (the kill) · the reload is countable · animation never lies · nothing sold touches a duel.

## Architecture in one paragraph
`TennisScore` (pure, tested) is the match's brain; `MatchDirector` is its host-authoritative body, running the phase machine (CoinFlip → InitiativePick → Approach → Live → Reckoning → Ceremony → Changeover). `DuelistController` owns movement and the accuracy authority (Plant × Aim); `Revolver` owns the hammer's true beat and the only fire path; `Cylinder` is the public shell ledger; `Vitals` adjudicates flesh; every sound routes through `SoundLedger` (the information system), which telemetry, the bot's ears, the tell-cam, and the `HonestyAuditor` all subscribe to — one reality, many listeners. The bot drives the identical controller through a virtual input shim; its skill knobs are discipline, never aim.

## Testing
`Code/Core/TennisScore.cs` compiles standalone (no engine deps). The test battery (rules + Monte-Carlo-vs-closed-form) lives in the build environment's `/scoretest`; port it to your CI as-is.
