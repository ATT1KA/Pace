using Sandbox;
using System;

namespace TenPaces;

public enum HandState { Holstered, Drawing, Drawn, Holstering, Feinting, Whipping }

/// <summary>
/// The revolver — the game's protagonist. This component owns:
///   • draw / holster / the holster-cancel feint (first-third window)
///   • the HAMMER: true-beat cadence, slip-hammer early-fire bloom, just-frame
///   • the accuracy matrix (Plant × Aim, from the controller) × Form scaling
///   • deterministic firing: NO RNG in the cone — bloom is a deterministic
///     spiral indexed by shot count, identical on client & host [LAW]
///   • fanning (Form-gated burst), the pistol-whip, the fidget flourishes
///   • host-side hit adjudication with lag-aware rewind hooks
///
/// The true beat is delivered as three synchronized cues (Feel Doc §III):
/// audible click (self + faint at whisper range), viewmodel hammer rest, and a
/// haptic tick — the rhythm system a controller player rides by feel.
/// </summary>
public sealed class Revolver : Component
{
	[RequireComponent] public Cylinder Cylinder { get; private set; }

	[Sync] public HandState HandState { get; private set; } = HandState.Holstered;
	[Sync] public bool IsAiming { get; private set; }
	[Sync] public FormId Form { get; private set; } = FormId.Duelist;
	[Sync] public TrickId Trick { get; private set; } = TrickId.Coin;
	[Sync] public bool TrickSpent { get; private set; }

	public FormDefinition FormDef { get; private set; } = FormDefinition.Baseline( FormId.Duelist );
	public bool IsDrawnOrDrawing => HandState is HandState.Drawn or HandState.Drawing;
	public float CameraRecoilPitch { get; private set; }

	// ── Read-only cadence/aim state (the bot reads these to earn its shot the
	//    same way a human does — never an aim shortcut) ──
	public bool  BeatReady    => _sinceLastShot >= FormDef.TrueBeatInterval; // firing now is on-beat (earliness 0)
	public bool  IsAimSettled => IsAiming && _aimBlend > 0.85f;              // the raise has reached the TRUE cone
	public bool  IsFanning    => _fanQueued > 0;
	public float LastShotEarliness { get; private set; }                     // earliness of the most recent shot

	DuelistController Duelist => Components.GetInParentOrSelf<DuelistController>();

	// hammer / cadence
	TimeSince _sinceLastShot = 999f;
	bool _beatClicked = true;
	int _shotIndex;                  // deterministic bloom spiral index
	TimeSince _handTimer = 999f;     // draw/holster/whip progress
	float _aimBlend;                 // 0..1 raise progress
	float _flinch;
	int _fanQueued;
	TimeSince _sinceFanShot = 999f;
	TimeSince _sinceWhip = 999f;

	public void ResetForPoint()
	{
		Cylinder.ResetForPoint();
		HandState = HandState.Holstered;
		IsAiming = false;
		TrickSpent = false;
		_sinceLastShot = 999f; _beatClicked = true; _shotIndex = 0;
		_aimBlend = 0; _fanQueued = 0; CameraRecoilPitch = 0;
	}

	/// <summary> Loadout swap — legal only in changeover windows (MatchDirector gate). </summary>
	[Rpc.Host]
	public void SetLoadout( FormId form, TrickId trick )
	{
		if ( MatchDirector.Instance is { LoadoutSwapAllowed: false } ) return;
		Form = form;
		Trick = trick;
		SyncFormDef();
	}

	FormId _formDefFor = (FormId)(-1);
	// Rebuild the FormDef only when the synced Form actually changes (loadout swap),
	// not every FixedUpdate — Baseline() allocates, and at 128 tick that was constant
	// GC churn for a value that changes once a changeover. Still called per-tick so the
	// owner picks up a synced Form change, but now it early-returns cheaply.
	void SyncFormDef()
	{
		if ( FormDef is not null && _formDefFor == Form ) return;
		FormDef = FormDefinition.Baseline( Form ); // asset lookup replaces this when .form files ship
		_formDefFor = Form;
	}

	protected override void OnStart() => SyncFormDef();

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		SyncFormDef();

		TickHandState();
		TickHammer();
		TickAim();
		TickFan();
		CameraRecoilPitch = CameraRecoilPitch.LerpTo( 0f, Time.Delta / Tuning.CameraRecenterTime * 3f );
		_flinch = _flinch.LerpTo( 0f, 4f * Time.Delta );

		if ( !Duelist.IsLocalControlled && !Duelist.IsBot ) return;
		if ( MatchDirector.Instance is { Phase: not (MatchPhase.Live or MatchPhase.Reckoning) } ) { if ( Duelist.IsLocalControlled ) HandleFlourishes(); return; }

		HandleCombatInput();
		HandleFlourishes();
	}

	// ── Input ─────────────────────────────────────────────────────

	void HandleCombatInput()
	{
		// Draw / holster toggle — and aiming or firing while holstered begins the draw.
		if ( Duelist.ActionPressed( "DrawHolster" )
			|| (HandState == HandState.Holstered && (Duelist.ActionPressed( "Attack" ) || Duelist.ActionDown( "Aim" ))) )
		{
			if ( HandState == HandState.Holstered ) BeginDraw();
			else if ( HandState == HandState.Drawn && !Cylinder.IsReloading ) BeginHolster();
			else if ( HandState == HandState.Drawing && _handTimer < FormDef.DrawTime * Tuning.DrawFeintWindow )
				BeginFeint(); // the holster-cancel: a draw genuinely begun, honestly aborted
		}

		IsAimingInput = Duelist.ActionDown( "Aim" );

		if ( Duelist.ActionPressed( "Attack" ) && HandState == HandState.Drawn )
		{
			if ( FormDef.CanFan && !IsAiming && Duelist.ActionDown( "Attack" ) )
				TryBeginFan();
			else
				TryFire();
		}

		if ( Duelist.ActionPressed( "Reload" ) && HandState == HandState.Drawn )
			Cylinder.BeginReload();
		if ( Duelist.ActionReleased( "Reload" ) )
			Cylinder.RequestInterrupt();
		if ( Cylinder.IsReloading && (Duelist.ActionPressed( "Attack" ) || Duelist.ActionPressed( "Aim" )) )
			Cylinder.RequestInterrupt();

		if ( Duelist.ActionPressed( "Whip" ) && HandState == HandState.Drawn && _sinceWhip > Tuning.WhipCooldown )
			BeginWhip();

		if ( Duelist.ActionPressed( "Trick" ) && !TrickSpent )
			UseTrick();
	}

	bool IsAimingInput;

	void HandleFlourishes()
	{
		// The Toy Principle: available anywhere, gameplay-free, beautiful.
		if ( Input.Pressed( "Spin" ) && HandState == HandState.Drawn )
			PlayFlourish( "spin" );
		if ( Input.Pressed( "HammerThumb" ) && HandState == HandState.Drawn )
			PlayFlourish( "hammer_thumb" );
		if ( Input.Pressed( "CylinderCheck" ) && HandState == HandState.Drawn && !Cylinder.IsReloading )
			PlayFlourish( "cylinder_check" ); // ALSO the primary ammo instrument — toy and tool are the same object
		if ( Input.Pressed( "TipHat" ) )
			PlayFlourish( "tip_hat" );
	}

	[Rpc.Broadcast]
	void PlayFlourish( string name )
	{
		ISceneEvent<IGunEvents>.Post( x => x.OnFlourish( Duelist, name ) );
		FeelTelemetry.Instance?.OnFlourish( Duelist, name );
	}

	// ── Draw / holster / feint ────────────────────────────────────

	void BeginDraw()
	{
		HandState = HandState.Drawing;
		_handTimer = 0;
		// The declaration: audible at range, Form-signed [LAW: identifiable by ear]
		SoundLedger.Report( Duelist, LedgerSound.Draw, Tuning.RadiusDraw, FormDef.DrawSound );
	}

	void BeginHolster()
	{
		HandState = HandState.Holstering;
		_handTimer = 0;
		IsAiming = false;
		SoundLedger.Report( Duelist, LedgerSound.Holster, Tuning.RadiusHolster );
	}

	void BeginFeint()
	{
		HandState = HandState.Feinting;
		_handTimer = 0;
		FeelTelemetry.Instance?.OnFeint( Duelist );
		// No sound: the feint's cost is time + your own unreadiness. The draw
		// sound already fired — that sound was TRUE (a draw genuinely began).
	}

	void TickHandState()
	{
		switch ( HandState )
		{
			case HandState.Drawing when _handTimer >= FormDef.DrawTime:
				HandState = HandState.Drawn;
				break;
			case HandState.Holstering when _handTimer >= Tuning.HolsterTime:
				HandState = HandState.Holstered;
				break;
			case HandState.Feinting when _handTimer >= Tuning.FeintRecoveryTime:
				HandState = HandState.Holstered;
				break;
			case HandState.Whipping when _handTimer >= Tuning.WhipWindup:
				ResolveWhip();
				HandState = HandState.Drawn;
				_sinceWhip = 0;
				break;
		}
	}

	// ── The hammer: true beat ─────────────────────────────────────

	void TickHammer()
	{
		if ( _beatClicked || _sinceLastShot < FormDef.TrueBeatInterval ) return;
		_beatClicked = true;
		// The three synchronized cues:
		SoundLedger.Report( Duelist, LedgerSound.HammerClick, Tuning.RadiusHammerClick ); // audible (self + whisper range)
		ISceneEvent<IGunEvents>.Post( x => x.OnTrueBeat( Duelist ) );                     // viewmodel hammer rest
		if ( Duelist.IsLocalControlled ) Input.TriggerHaptics( 0f, 0.25f, 0.03f );        // haptic tick — the pad rides the beat
	}

	/// <summary> How early is this trigger pull, 0 (on/after beat) → 1 (instant re-pull)? </summary>
	float Earliness()
	{
		if ( _sinceLastShot >= FormDef.TrueBeatInterval ) return 0f;
		return 1f - (_sinceLastShot / FormDef.TrueBeatInterval);
	}

	bool IsJustFrame() =>
		_sinceLastShot >= FormDef.TrueBeatInterval
		&& _sinceLastShot <= FormDef.TrueBeatInterval + Tuning.JustFrameWindow;

	// ── Aim ───────────────────────────────────────────────────────

	void TickAim()
	{
		bool wantsAim = IsAimingInput && HandState == HandState.Drawn && !Cylinder.IsReloading;
		IsAiming = wantsAim;
		float target = wantsAim ? 1f : 0f;
		_aimBlend = _aimBlend.LerpTo( target, Time.Delta / Tuning.AimRaiseTime );
	}

	// ── The accuracy matrix — one function, the whole gospel ──────

	public float CurrentConeDegrees( float earliness, bool fanning = false, float targetDistance = 0f )
	{
		float cone;
		bool planted = Duelist.CountsAsPlanted;
		bool aimed = IsAiming && _aimBlend > 0.85f;

		if ( fanning ) cone = FormDef.FanConeDegrees;
		else if ( planted && aimed ) cone = Tuning.ConePlantedAimed;   // TRUE [LAW]
		else if ( planted )          cone = Tuning.ConePlantedHip;
		else if ( aimed )            cone = Tuning.ConeMovingAimed;
		else                         cone = Tuning.ConeMovingHip;

		cone *= FormDef.ConeScale;

		// Form range falloff: past the Form's effective range the cone opens up. This is
		// the lever that gives Deadeye its reach (2400u knee, gentle add) and makes Fanning
		// a short-range animal (700u knee, steep add). Distance 0 = no falloff (e.g. the
		// crosshair, which shows the near-range cone).
		if ( targetDistance > FormDef.EffectiveRange && FormDef.EffectiveRange > 0f )
		{
			float over = (targetDistance - FormDef.EffectiveRange) / FormDef.EffectiveRange;
			cone += FormDef.BeyondRangeConeAdd * MathF.Min( over, Tuning.RangeFalloffMaxMult );
		}

		// slip-hammer: early fire blooms on a deterministic curve
		cone += Tuning.EarlyFireMaxBloom * MathF.Pow( earliness, Tuning.BeatBloomExponent );

		// landing bloom
		if ( Duelist.HasLandingBloom ) cone += Tuning.LandBloomCone;
		// flinch from being hit
		cone += _flinch;

		return cone;
	}

	// ── Fire ──────────────────────────────────────────────────────

	void TryFire()
	{
		if ( Cylinder.IsReloading ) { Cylinder.RequestInterrupt(); return; }
		if ( !Cylinder.ConsumeShell() ) return;

		float earliness = Earliness();
		bool justFrame = IsJustFrame();
		_sinceLastShot = 0;
		_beatClicked = false;
		_shotIndex++;

		Fire( earliness, justFrame, fanning: false );
	}

	void TryBeginFan()
	{
		if ( Cylinder.IsReloading || Cylinder.IsEmpty ) return;
		_fanQueued = Math.Min( FormDef.FanMaxShells, Cylinder.Loaded );
		_sinceFanShot = 999f;
	}

	void TickFan()
	{
		if ( _fanQueued <= 0 ) return;
		if ( _sinceFanShot < FormDef.FanInterval ) return;
		if ( !Duelist.ActionDown( "Attack" ) ) { _fanQueued = 0; return; } // release ends the fan
		if ( !Cylinder.ConsumeShell() ) { _fanQueued = 0; return; }

		_sinceFanShot = 0;
		_fanQueued--;
		_sinceLastShot = 0;
		_beatClicked = false;
		_shotIndex++;
		Fire( earliness: 0f, justFrame: false, fanning: true );
	}

	/// <summary>
	/// DETERMINISTIC ballistics [LAW]: the shot's offset inside the cone is a
	/// fixed spiral indexed by shot count — zero RNG, identical everywhere,
	/// fully learnable. Every miss is the player's miss.
	/// </summary>
	void Fire( float earliness, bool justFrame, bool fanning )
	{
		var origin = Duelist.EyePosition;
		var baseDir = Duelist.EyeRotation.Forward;

		LastShotEarliness = earliness;

		// Estimate range to whatever is down the barrel so the Form's range falloff can apply.
		float targetDistance = EstimateTargetDistance( origin, baseDir );
		float cone = CurrentConeDegrees( earliness, fanning, targetDistance );
		Vector3 dir = ApplyDeterministicSpread( baseDir, cone, _shotIndex );

		// presentation, everywhere
		BroadcastMuzzle( origin, dir, justFrame );

		// recoil: viewmodel big, camera modest & recentering [LAW]
		CameraRecoilPitch += Tuning.CameraKickPitch;

		// gunshots are map-wide, always [LAW]
		SoundLedger.Report( Duelist, LedgerSound.Gunshot, Tuning.RadiusGunshot );
		FeelTelemetry.Instance?.OnShotFired( Duelist, earliness, justFrame, fanning, Duelist.CountsAsPlanted, IsAiming );

		// adjudication on host
		if ( Networking.IsHost ) AdjudicateShot( origin, dir );
		else RequestAdjudication( origin, dir );
	}

	float EstimateTargetDistance( Vector3 origin, Vector3 dir )
	{
		var tr = Scene.Trace.Ray( origin, origin + dir * 100000f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "loose_hat" )
			.Run();
		return tr.Hit ? tr.Distance : 100000f;
	}

	static Vector3 ApplyDeterministicSpread( Vector3 dir, float coneDeg, int index )
	{
		if ( coneDeg <= 0.001f ) return dir;
		// golden-angle spiral: dense, even, and utterly predictable
		float angle = index * 2.399963f; // golden angle, radians
		float radius = coneDeg * (0.35f + 0.65f * ((index * 37 % 100) / 100f));
		var rot = Rotation.LookAt( dir );
		var offset = rot.Up * MathF.Sin( angle ) + rot.Right * MathF.Cos( angle );
		return (dir + offset * MathF.Tan( radius.DegreeToRadian() )).Normal;
	}

	[Rpc.Host]
	void RequestAdjudication( Vector3 origin, Vector3 dir )
	{
		// Host validates the claim before tracing: origin must be near the
		// shooter's replicated eye, direction must be sane. (Beta anti-cheat
		// floor; dedicated-server rewind buffer is the post-beta upgrade.)
		if ( origin.Distance( Duelist.EyePosition ) > 24f ) return;
		AdjudicateShot( origin, dir );
	}

	void AdjudicateShot( Vector3 origin, Vector3 dir )
	{
		var tr = Scene.Trace.Ray( origin, origin + dir * 100000f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( "loose_hat" )
			.Run();

		if ( !tr.Hit ) return;

		BroadcastImpact( tr.HitPosition, tr.Normal, tr.Surface?.Name ?? "dirt" );

		var victim = tr.GameObject?.Components.GetInParentOrSelf<DuelistController>();
		if ( victim is not null && victim != Duelist )
		{
			var zone = victim.Vitals.ResolveBulletHit( tr.HitPosition, Duelist );
			if ( zone == HitZone.Perfect )
				BroadcastHitstop(); // the game's ONLY hitstop: the kill, shooter-side [LAW]
		}
	}

	[Rpc.Broadcast]
	void BroadcastMuzzle( Vector3 origin, Vector3 dir, bool justFrame )
	{
		// muzzle flash single-frame bloom + drifting smoke that genuinely, slightly occludes
		ISceneEvent<IGunEvents>.Post( x => x.OnMuzzle( Duelist, origin, dir, justFrame ) );
	}

	[Rpc.Broadcast]
	void BroadcastImpact( Vector3 pos, Vector3 normal, string surface )
	{
		// splinters on wood, dirt kicks on ground — MISSES ARE VISIBLE INFORMATION
		ISceneEvent<IGunEvents>.Post( x => x.OnImpact( pos, normal, surface ) );
	}

	[Rpc.Broadcast]
	void BroadcastHitstop()
	{
		ISceneEvent<IGunEvents>.Post( x => x.OnKillHitstop( Duelist ) );
	}

	// ── Whip ──────────────────────────────────────────────────────

	void BeginWhip()
	{
		HandState = HandState.Whipping;
		_handTimer = 0;
		SoundLedger.Report( Duelist, LedgerSound.WhipSwing, Tuning.RadiusWhip );
	}

	void ResolveWhip()
	{
		var origin = Duelist.EyePosition;
		var dir = Duelist.EyeRotation.Forward;
		var tr = Scene.Trace.Ray( origin, origin + dir * Tuning.WhipRange )
			.IgnoreGameObjectHierarchy( GameObject ).Run();

		var victim = tr.GameObject?.Components.GetInParentOrSelf<DuelistController>();
		if ( victim is not null && victim != Duelist )
		{
			if ( Networking.IsHost ) victim.Vitals.ApplyWhip( Duelist );
			else RequestWhip( victim.GameObject.Id );
			ISceneEvent<IGunEvents>.Post( x => x.OnWhipConnect( Duelist, victim ) ); // the dense thock
		}
		else
		{
			// whiff recovery: gun out of firing position
			_sinceLastShot = -(Tuning.WhipWhiffRecovery - FormDef.TrueBeatInterval);
			_beatClicked = false;
		}
	}

	[Rpc.Host]
	void RequestWhip( Guid victimId )
	{
		var victim = Scene.Directory.FindByGuid( victimId )?.GetComponent<DuelistController>();
		if ( victim is null ) return;
		if ( victim.WorldPosition.Distance( Duelist.WorldPosition ) > Tuning.WhipRange + 30f ) return;
		victim.Vitals.ApplyWhip( Duelist );
	}

	// ── Tricks ────────────────────────────────────────────────────

	void UseTrick()
	{
		TrickSpent = true;
		RequestTrick(); // host spawns/simulates the trick authoritatively
	}

	// The trick object must be host-authoritative (its damage/smoke are gameplay, not
	// just presentation) and NetworkSpawned so every client sees it. Spawning it on the
	// caller's client — as the old code did — meant a non-host knife never reached the
	// host to deal damage, and a vial's smoke never appeared for the opponent.
	[Rpc.Host]
	void RequestTrick() => Tricks.Execute( Trick, Duelist );

	// ── External hooks ────────────────────────────────────────────

	public void OnStaggered()
	{
		Cylinder.ForceInterrupt();
		IsAiming = false;
		_aimBlend = 0;
	}

	public void ApplyFlinch( float degrees ) => _flinch += degrees;
}

public interface IGunEvents : ISceneEvent<IGunEvents>
{
	void OnMuzzle( DuelistController shooter, Vector3 origin, Vector3 dir, bool justFrame ) { }
	void OnImpact( Vector3 pos, Vector3 normal, string surface ) { }
	void OnKillHitstop( DuelistController shooter ) { }
	void OnTrueBeat( DuelistController shooter ) { }
	void OnWhipConnect( DuelistController attacker, DuelistController victim ) { }
	void OnFlourish( DuelistController d, string flourish ) { }
}
