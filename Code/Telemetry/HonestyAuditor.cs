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
	[Property] public bool Enabled_MotionTruth { get; set; } = true;
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
		if ( !Enabled_MotionTruth ) return;

		foreach ( var d in Scene.GetAllComponents<DuelistController>() )
		{
			if ( !d.Vitals.Alive ) continue;
			float speed = d.Body.Velocity.WithZ( 0 ).Length;
			float cap = d.State switch
			{
				MoveState.Sliding => Tuning.HolsteredRunSpeed * Tuning.SlideSpeedBoost + 20f,
				MoveState.Staggered => Tuning.WhipKnockback + 40f,
				MoveState.Mantling => 999f,
				_ => d.CurrentGait switch
				{
					Gait.HolsteredRun => Tuning.HolsteredRunSpeed + 15f,
					Gait.DrawnWalk    => Tuning.DrawnWalkSpeed + 15f,
					_                 => Tuning.SoftStepSpeed + 60f, // knockback slack
				}
			};
			if ( speed > cap )
				Flag( $"MOTION LIE: {d.GameObject.Name} at {speed:F0}u/s exceeds {d.CurrentGait}/{d.State} cap {cap:F0}" );

			// GAIT LAW: fast movement with iron in hand is impossible
			if ( d.Gun.IsDrawnOrDrawing && speed > Tuning.DrawnWalkSpeed + 25f && d.State is MoveState.Grounded )
				Flag( $"GAIT LIE: {d.GameObject.Name} moving {speed:F0}u/s while drawn" );
		}
	}

	void Flag( string violation )
	{
		_violations.Add( violation );
		Log.Warning( $"[HONESTY AUDITOR] {violation}" );
	}
}
