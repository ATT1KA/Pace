using Sandbox;

namespace TenPaces;

/// <summary>
/// Camera-adjacent presentation: the kill hitstop (the game's only hitstop
/// [LAW]), the wound vignette/desaturation, the graze crack-past, and the
/// point-end tableau timing. Kept out of the HUD panel so the information
/// frame stays pure — this component touches time, tonemapping, and audio
/// mix, never text.
/// </summary>
public sealed class ScreenEffects : Component, IGunEvents, IVitalsEvents, IMatchEvents
{
	TimeSince _sinceHitstop = 999;
	bool _hitstopActive;

	// ── The kill hitstop — 40ms world-hang, shooter's screen only ──
	public void OnKillHitstop( DuelistController shooter )
	{
		if ( !shooter.IsLocalControlled ) return;
		_hitstopActive = true;
		_sinceHitstop = 0;
		Scene.TimeScale = 0.02f;
	}

	protected override void OnUpdate()
	{
		if ( _hitstopActive && _sinceHitstop >= Tuning.HitstopSeconds )
		{
			_hitstopActive = false;
			Scene.TimeScale = 1f;
		}

		ApplyWoundGrade();
	}

	// ── Wound state: slight peripheral desaturation, private heartbeat handled by Vitals ──
	void ApplyWoundGrade()
	{
		var local = FindLocal();
		var grading = Scene.Camera?.GetComponent<ColorAdjustments>();
		if ( grading is null ) return;

		bool wounded = local?.Vitals.IsWounded ?? false;
		float targetSat = wounded ? 0.82f : 1f;
		grading.Saturation = grading.Saturation.LerpTo( targetSat, 3f * Time.Delta );
	}

	// ── Graze: crack-past + cloth-tug hook ──
	public void OnGraze( DuelistController d, Vector3 at )
	{
		if ( !d.IsLocalControlled ) return;
		Sound.Play( "tp.crack_past" ); // 2D whip-crack, direction baked by mixer later
	}

	// ── Death: the loser's dignified frame ──
	public void OnDuelistDied( DuelistController d, HitZone zone )
	{
		if ( !d.IsLocalControlled ) return;
		// brief locked frame of the final view — camera stays; no kill-cam mockery [LAW]
		// (input already dead via MoveState.Dead; ambience duck handled in ceremony)
	}

	// ── Point ceremony: ambience duck for the bell/call/tableau beat ──
	public void OnKillCeremony( System.Guid victimId, HitZone zone )
	{
		// -6dB ambience duck for the ceremony window; mixer hook:
		// Audio.SetMixVolume("ambience", 0.5f) → restore on next Approach.
		// Ragdoll: victim prefab's ModelPhysics enables here with directional
		// impulse (prefab wiring; dust bloom particle on ground contact).
	}

	DuelistController FindLocal()
	{
		foreach ( var d in Scene.GetAllComponents<DuelistController>() )
			if ( d.IsLocalControlled ) return d;
		return null;
	}
}
