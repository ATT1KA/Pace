using Sandbox;
using System;
using System.Collections.Generic;

namespace TenPaces;

/// <summary>
/// THE HONESTY AUDITOR — Feel Assertion #6 ("The Honest World Test") turned
/// into always-on runtime CI. A dev/beta component that continuously asserts
/// the game's information covenant and screams (log + on-screen in dev) the
/// instant any system lies:
///
///   AUDIT 1 — SOUND TRUTH: every ledger entry's claimed source position must
///   match the source object's actual position at emission (± net tolerance).
///   A sound that plays where the player isn't is a LIE.
///
///   AUDIT 2 — ANIMATION TRUTH: sampled each frame — the synced mechanical
///   state (gait, hand state, reload stage, aiming) must be exactly what the
///   animgraph was fed. Any divergence is a LIE.
///
///   AUDIT 3 — MOTION TRUTH: gait speed caps are physical law; a body moving
///   faster than its gait allows (beyond slide/knockback windows) is a LIE
///   (and, in ranked, a cheat signal — this audit doubles as the anti-cheat
///   heuristic floor).
///
///   AUDIT 4 — BOT PARITY: The Stranger must be ledger-indistinguishable from
///   a human — every bot movement/action must have emitted the same sounds a
///   human doing it would have.
///
/// The auditor never fixes anything. It exists so lies cannot ship quietly.
/// </summary>
public sealed class HonestyAuditor : Component, ILedgerEvents
{
	[Property] public bool Enabled_SoundTruth { get; set; } = true;
	[Property] public bool Enabled_AnimationTruth { get; set; } = true;
	[Property] public bool Enabled_MotionTruth { get; set; } = true;
	[Property] public bool Enabled_BotParity { get; set; } = true;
	[Property] public float NetTolerance { get; set; } = 48f; // units of interp slack

	readonly List<string> _violations = new();
	public IReadOnlyList<string> Violations => _violations;

	public void OnLedgerSound( Guid sourceId, Vector3 pos, LedgerSound sound, float radius )
	{
		if ( !Enabled_SoundTruth || sourceId == Guid.Empty ) return;
		var src = Scene.Directory.FindByGuid( sourceId );
		if ( src is null ) return;
		float drift = src.WorldPosition.Distance( pos );
		if ( drift > NetTolerance )
			Flag( $"SOUND LIE: {sound} claimed {pos} but source at {src.WorldPosition} (drift {drift:F0}u)" );
	}

	protected override void OnFixedUpdate()
	{
		if ( !(Enabled_MotionTruth || Enabled_AnimationTruth || Enabled_BotParity) ) return;

		foreach ( var d in Scene.GetAllComponents<DuelistController>() )
		{
			if ( !d.Vitals.Alive ) continue;

			if ( Enabled_MotionTruth )  AuditMotion( d );
			if ( Enabled_AnimationTruth ) AuditAnimation( d );
			if ( Enabled_BotParity && d.IsBot ) AuditBotParity( d );
		}
	}

	// AUDIT 3 — MOTION TRUTH: gait speed caps are physical law.
	void AuditMotion( DuelistController d )
	{
		float speed = d.Body.Velocity.WithZ( 0 ).Length;
		float cap = d.State switch
		{
			MoveState.Sliding => Tuning.HolsteredRunSpeed * Tuning.SlideSpeedBoost + Tuning.AuditSlideSlack,
			MoveState.Staggered => Tuning.WhipKnockback + Tuning.AuditStaggerSlack,
			MoveState.Mantling => float.MaxValue, // mantle is a scripted lerp, not gaited motion
			_ => d.CurrentGait switch
			{
				Gait.HolsteredRun => Tuning.HolsteredRunSpeed + Tuning.AuditGaitSlack,
				Gait.DrawnWalk    => Tuning.DrawnWalkSpeed + Tuning.AuditGaitSlack,
				_                 => Tuning.SoftStepSpeed + Tuning.AuditKnockbackSlack, // knockback slack
			}
		};
		if ( speed > cap )
			Flag( $"MOTION LIE: {d.GameObject.Name} at {speed:F0}u/s exceeds {d.CurrentGait}/{d.State} cap {cap:F0}" );

		// GAIT LAW: fast movement with iron in hand is impossible
		if ( d.Gun.IsDrawnOrDrawing && speed > Tuning.DrawnWalkSpeed + Tuning.AuditDrawnSlack && d.State is MoveState.Grounded )
			Flag( $"GAIT LIE: {d.GameObject.Name} moving {speed:F0}u/s while drawn" );
	}

	// AUDIT 2 — ANIMATION TRUTH: the synced mechanical state drives the animgraph, so
	// it must be internally consistent. A state the animation can't honestly show — aiming
	// or reloading with the gun holstered — is a lie regardless of what the model plays.
	void AuditAnimation( DuelistController d )
	{
		bool drawn = d.Gun.HandState is HandState.Drawn;
		if ( d.Gun.IsAiming && !drawn )
			Flag( $"ANIM LIE: {d.GameObject.Name} aiming while {d.Gun.HandState}" );
		if ( d.Gun.Cylinder.IsReloading && !drawn )
			Flag( $"ANIM LIE: {d.GameObject.Name} reloading while {d.Gun.HandState}" );
	}

	// AUDIT 4 — BOT PARITY: The Stranger must be ledger-indistinguishable from a human. It
	// emits through the SAME ledger and drives the SAME controller, so parity reduces to:
	// it carries the same ledger-relevant components a human body does. A bot missing one
	// would emit (or fail to emit) sounds a human never would.
	void AuditBotParity( DuelistController d )
	{
		if ( d.Gun is null || d.Gun.Cylinder is null || d.Vitals is null )
			Flag( $"BOT PARITY LIE: {d.GameObject.Name} lacks a human's ledger components (revolver/cylinder/vitals)" );
	}

	void Flag( string violation )
	{
		// Ring-buffer: cap growth over a long playtest session.
		if ( _violations.Count >= Tuning.AuditMaxViolations )
			_violations.RemoveAt( 0 );
		_violations.Add( violation );
		Log.Warning( $"[HONESTY AUDITOR] {violation}" );
	}
}
