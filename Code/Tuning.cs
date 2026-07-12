namespace TenPaces;

/// <summary>
/// THE BOOK OF NUMBERS.
/// Every gameplay constant in Ten Paces lives in this single file, grouped by system,
/// with the Feel Document section that owns it. Nothing is tuned anywhere else.
/// Hot-reload in s&box means editing this file re-tunes a live playtest in seconds —
/// this file IS the tuning workflow.
///
/// Values marked [BASELINE] are the Feel Document's starting points and are expected
/// to move during beta. Values marked [LAW] are design law and require a design-doc
/// amendment to change.
/// </summary>
public static class Tuning
{
	// ─────────────────────────────────────────────────────────────
	// LOCOMOTION — Feel Doc §II
	// ─────────────────────────────────────────────────────────────
	public const float HolsteredRunSpeed = 330f;        // [LAW] fastest state, holstered only
	public const float DrawnWalkSpeed    = 230f;        // combat gait
	public const float SoftStepSpeed     = 120f;        // near-silent creep
	public const float AimedMoveSpeed    = 120f;        // aimed movement clamps to soft-step speed
	public const float WoundedSpeedMult  = 1.0f;        // [LAW] wounds NEVER slow you (no snowball)
	public const float CrouchSpeed       = 100f;

	public const float GroundAccel       = 2200f;       // fast-feeling accel, ~0.15s to full run
	public const float GroundFriction    = 7.0f;
	public const float AirControl        = 0.12f;       // [LAW] minimal — no air-strafing meta

	// The Plant — Feel Doc §II ("the single most important locomotion mechanic")
	public const float PlantTime         = 0.18f;       // [BASELINE] deceleration to planted
	public const float PlantCameraDip    = 4f;          // units of settle dip, spring back
	public const float PlantGraceWindow  = 0.05f;       // firing within this of plant-complete counts planted

	// Slide
	public const float SlideDuration     = 0.70f;       // [BASELINE]
	public const float SlideSpeedBoost   = 1.15f;       // initial burst over run speed
	public const float SlideSteerRate    = 30f;         // degrees/sec — commitment, not a drift
	public const float SlideCancelWindow = 0.18f;       // final-quarter window for the slide-plant
	public const float SlideCooldown     = 1.2f;        // no chaining [LAW]
	public const float SlideMinEntrySpeed= 280f;        // must be running

	// Jump / landing / mantle
	public const float JumpVelocity      = 240f;        // low apex ~40u [LAW: humble]
	public const float Gravity           = 850f;
	public const float LandBloomTime     = 0.25f;       // accuracy penalty window on touchdown
	public const float LandHeavyFall     = 120f;        // fall speed threshold for the heavy thud
	public const float MantleTime        = 0.90f;       // chest-high ledge
	public const float MantleBreakWindow = 0.30f;       // first-third mantle-feint window
	public const float MantleMaxHeight   = 56f;
	public const float MantleMinHeight   = 30f;

	// ─────────────────────────────────────────────────────────────
	// THE REVOLVER — Feel Doc §III
	// ─────────────────────────────────────────────────────────────
	public const int   CylinderCapacity  = 6;           // [LAW]

	// Damage model — Design Doc §III: two body hits, or one perfect
	public const float PerfectDamage     = 100f;        // [LAW] head/heart, one shot
	public const float BodyDamage        = 50f;         // [LAW] two-hit body kill
	public const float MaxHealth         = 100f;        // wounded state at <= 50

	// True beat — the hammer metronome (per-Form intervals live in FormDefinition)
	public const float JustFrameWindow   = 0.05f;       // [LAW] connoisseur window, no stat bonus
	public const float EarlyFireMaxBloom = 14f;         // degrees of cone at maximally-early slip-hammer
	public const float BeatBloomExponent = 1.6f;        // earliness→bloom curve shape
	public const float LandBloomCone     = 3f;          // degrees added to the cone during LandBloomTime
	public const float RangeFalloffMaxMult = 3f;        // caps the Form range-falloff cone add (× BeyondRangeConeAdd)

	// Accuracy matrix (cone half-angle, degrees) — Feel Doc §III Plant × Aim
	public const float ConeMovingHip     = 6.5f;
	public const float ConeMovingAimed   = 2.8f;
	public const float ConePlantedHip    = 2.2f;
	public const float ConePlantedAimed  = 0.0f;        // [LAW] planted-aimed on the beat is TRUE

	public const float AimSettleTime     = 0.30f;       // sway death after raise, if planted
	public const float AimFovTighten     = 3f;          // degrees
	public const float AimRaiseTime      = 0.22f;

	// Recoil — feedback, not a control system (Feel Doc §III)
	public const float CameraKickPitch   = 1.2f;        // degrees
	public const float CameraRecenterTime= 0.25f;       // full recenter [LAW: comp readability]
	public const float ViewmodelKickScale= 1.0f;        // juice lives in the weapon

	// Holster / draw shared
	public const float HolsterTime       = 0.90f;       // [LAW] slower than any draw
	public const float DrawFeintWindow   = 0.3333f;     // first third of the draw is cancelable
	public const float FeintRecoveryTime = 0.30f;       // cost of a broken draw

	// Smoke — firing writes your position into the air
	public const float MuzzleSmokeLife   = 1.5f;

	// ─────────────────────────────────────────────────────────────
	// RELOAD RITUAL — Feel Doc §IV (all per-shell, all interruptible)
	// ─────────────────────────────────────────────────────────────
	public const float ReloadGateOpen    = 0.40f;
	public const float ReloadEjectShell  = 0.35f;
	public const float ReloadInsertShell = 0.55f;
	public const float ReloadGateClose   = 0.30f;
	public const float ReloadMovingMult  = 1.30f;       // 30% slower while soft-stepping
	// full 6-shell cycle ≈ 4.5s of loud vulnerability, by design

	// ─────────────────────────────────────────────────────────────
	// PISTOL-WHIP — Feel Doc §V
	// ─────────────────────────────────────────────────────────────
	public const float WhipRange         = 60f;         // ~1.5m lunge reach
	public const float WhipWindup        = 0.40f;
	public const float WhipDamage        = 25f;         // modest [LAW: tempo weapon, not kill route]
	public const float WhipStaggerTime   = 0.55f;
	public const float WhipKnockback     = 130f;        // ~half meter shove
	public const float WhipWhiffRecovery = 0.70f;
	public const float WhipCooldown      = 1.4f;

	// ─────────────────────────────────────────────────────────────
	// WOUNDS & DEATH — Feel Doc §VI
	// ─────────────────────────────────────────────────────────────
	public const float HitstopSeconds    = 0.040f;      // [LAW] the game's ONLY hitstop: the kill
	public const float FlinchDegrees     = 2.0f;
	public const float HeartbeatVolume   = 0.6f;        // private to the wounded player [LAW]
	public const float GrazeCapsuleScale = 1.18f;       // outer graze shell around hit capsule

	// Hit-zone geometry (local, relative to the duelist origin) — Feel Doc §VI.
	// [BASELINE] the geometry that decides Perfect / Body / Graze; feel-critical, so it
	// lives here in the Book of Numbers rather than as literals in Vitals.
	public const float ZoneHeadMinZ      = 58f;         // above this = head (Perfect)
	public const float ZoneHeartMinZ     = 44f;         // heart band, low
	public const float ZoneHeartMaxZ     = 54f;         // heart band, high
	public const float ZoneHeartHeight   = 49f;         // heart disc center height
	public const float ZoneHeartRadius   = 6f;          // heart disc radius
	public const float ZoneHeartForward  = 8f;          // forward offset into the CHEST — front-of-torso
	                                                    // disc (a back shot to the same height is Body)
	public const float ZoneBodyMaxZ      = 72f;         // top of the body capsule
	public const float ZoneBodyMargin    = 1f;          // capsule radius slack for a body hit
	public const float ZoneGrazeMinZ     = -2f;
	public const float ZoneGrazeMaxZ     = 76f;

	// The hat — a crown cap, not armor. It shrinks the instant-headshot surface area at
	// match start; it is NOT sustainable protection. A face/front head shot ignores it.
	public const float HatKnockChanceGraze = 0.55f;
	public const float HatCrownRadius      = 6f;        // horizontal cap radius around the socket axis
	public const float HatCrownDrop        = 4f;        // how far below the socket top the cap reaches
	public const float HatAbsorbFraction   = 0.5f;      // the hat eats HALF the shot, then it's gone

	public const float WoundSaturation   = 0.82f;       // peripheral desaturation while wounded
	public const float WoundGradeLerpRate = 3f;         // how fast the wound grade eases in/out

	// Point-end ceremony
	public const float CeremonyHold      = 3.0f;        // bell → call → tableau → next point
	public const float ScorecardFlipTime = 0.45f;

	// ─────────────────────────────────────────────────────────────
	// MATCH STRUCTURE — Design Doc §VI (tennis)
	// ─────────────────────────────────────────────────────────────
	public const int   PointsPerGame     = 4;           // [LAW] 15/30/40/game, win by two (deuce)
	public const int   GamesPerSet       = 6;           // [LAW] win by two; tiebreak at 6-6
	public const int   TiebreakTo        = 7;           // [LAW] win by two
	public const int   SetsToWinStandard = 2;           // best of three
	public const int   SetsToWinFinal    = 3;           // best of five (pro finals)

	public const float PointTimeLimit    = 90f;         // [LAW] then the Reckoning
	public const float ReckoningWarning  = 75f;         // bell warning at T-15
	public const float ReckoningGrace    = 6f;          // time to reach the resolve volume
	public const float ReckoningTickDmg  = 10f;         // per second outside the volume after grace
	public const float ReckoningSuddenDeath = 12f;      // phase seconds: the resolve volume COLLAPSES —
	                                                    // pressure then applies to EVERYONE so a mutual
	                                                    // stand-in at the heart can never hang the match
	public const float ChangeoverTime    = 45f;         // [LAW] after odd games — the adaptation window
	public const float SetBreakTime      = 90f;
	public const float ApproachFreeze    = 1.5f;        // both players locked at gates, "ten paces" beat
	public const float InitiativePickTime= 10f;         // Hour + gate selection window
	public const float MatchEndHold      = 8f;          // victory ceremony before the session returns to lobby

	// ─────────────────────────────────────────────────────────────
	// TRICKS — Design Doc §IV (one per point)
	// ─────────────────────────────────────────────────────────────
	public const float CoinThrowSpeed    = 700f;
	public const float CoinThrowLift     = 120f;        // upward kick on the toss
	public const float CoinSoundRadius   = 900f;        // footstep-class report where it lands
	public const float CoinLandDestroyDelay = 4f;       // coin lingers briefly after it lands
	public const float VialThrowSpeed    = 500f;
	public const float VialThrowLift     = 100f;
	public const float VialSmokeLife     = 3.5f;
	public const float VialSmokeRadius   = 90f;
	public const float KnifeSpeed        = 1400f;
	public const float KnifeGravity      = 200f;        // gentle arc on the thrown blade
	public const float KnifeLifetime     = 3f;          // seconds before a missed knife despawns
	public const float KnifeBodyDamage   = 25f;         // lethal ONLY on perfect hit [LAW]
	public const float KnifeWindup       = 0.35f;

	public const float TrickThrowOffset  = 20f;         // spawn ahead of the eye (coin / vial)
	public const float TrickKnifeOffset  = 24f;
	public const float TrickCoinRadius   = 1.5f;
	public const float TrickVialRadius   = 2f;

	// ─────────────────────────────────────────────────────────────
	// AUDIO LEDGER — Feel Doc §VIII (loudness classes → audible radius, units)
	// The ledger is HONEST [LAW]: every entry networked, none suppressed, none faked.
	// ─────────────────────────────────────────────────────────────
	public const float RadiusGunshot     = 100000f;     // map-wide, always
	public const float RadiusRun         = 900f;
	public const float RadiusWalk        = 450f;
	public const float RadiusSoftStep    = 90f;
	public const float RadiusSlide       = 800f;
	public const float RadiusDraw        = 600f;        // the declaration
	public const float RadiusHolster     = 450f;
	public const float RadiusReloadClink = 500f;        // countable by ear [LAW]
	public const float RadiusMantle      = 700f;
	public const float RadiusLandHeavy   = 700f;
	public const float RadiusWhip        = 550f;
	public const float RadiusHammerClick = 120f;        // faint true-beat tick, very close only

	// ─────────────────────────────────────────────────────────────
	// CAMERA — Feel Doc §VII
	// ─────────────────────────────────────────────────────────────
	public const float DefaultFov        = 95f;
	public const float MinFov            = 85f;
	public const float MaxFov            = 105f;
	public const float RunFovKick        = 3f;
	public const float EyeHeight         = 64f;
	public const float EyeHeightCrouch   = 40f;
	public const float EyeHeightSoftStep = 60f;         // camera drops a few units when creeping

	// ─────────────────────────────────────────────────────────────
	// KPI TARGETS — Design Doc §XII (the game's most important numbers)
	// ─────────────────────────────────────────────────────────────
	public const float TargetElitePerPointWinrate    = 0.75f;  // elite vs novice, per point
	public const float TargetAdjacentPerPointWinrate = 0.535f; // adjacent percentiles ≈ 52-55%

	// ─────────────────────────────────────────────────────────────
	// HONESTY AUDITOR — motion-cap slack (net interp + knockback overshoot)
	// ─────────────────────────────────────────────────────────────
	public const float AuditSlideSlack     = 20f;
	public const float AuditStaggerSlack   = 40f;
	public const float AuditGaitSlack      = 15f;
	public const float AuditKnockbackSlack = 60f;
	public const float AuditDrawnSlack     = 25f;
	public const int   AuditMaxViolations  = 256;  // ring-buffer cap over a long session
}
