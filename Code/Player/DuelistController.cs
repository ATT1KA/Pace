using Sandbox;
using System;

namespace TenPaces;

public enum Gait { HolsteredRun, DrawnWalk, SoftStep, Crouch }
public enum MoveState { Grounded, Air, Sliding, Mantling, Staggered, Locked, Dead }

/// <summary>
/// The duelist's body: three gaits, the Plant, the slide (with slide-plant and
/// slide-fade), humble jumps with heavy landings, the mantle (with mantle-feint),
/// firm player-vs-player collision, and the camera feel stack (settle dip, run
/// FOV kick, soft-step eye drop).
///
/// Design law enforced here:
///   • Holstered run is the ONLY fast state; drawing iron forfeits it.
///   • Accuracy authority (planted? aimed? bloom timers) is computed here and
///     read by the Revolver — one source of truth for the accuracy matrix.
///   • Every state transition reports to the SoundLedger. Animation never lies
///     because state IS the animation input.
/// </summary>
public sealed class DuelistController : Component
{
	[RequireComponent] public CharacterController Body { get; private set; }
	[Property] public GameObject Eye { get; set; }
	[Property] public GameObject HatSocket { get; set; }

	public Revolver Gun { get; private set; }
	public Vitals Vitals { get; private set; }
	public HatComponent Hat { get; private set; }

	// ── Synced state (what the opponent's client renders — the honesty layer) ──
	[Sync] public Gait CurrentGait { get; private set; } = Gait.HolsteredRun;
	[Sync] public MoveState State { get; private set; } = MoveState.Grounded;
	[Sync] public bool IsPlanted { get; private set; }
	[Sync] public Angles EyeAngles { get; set; }

	// ── Local feel state ──────────────────────────────────────────
	float _plantProgress;            // 0..1, reaches 1 = planted
	TimeSince _sinceLanded = 999;
	TimeSince _sinceSlideStart = 999;
	TimeSince _sinceSlideEnd = 999;
	TimeSince _sinceStagger = 999;
	TimeSince _mantleStart = 999;
	Vector3 _mantleFrom, _mantleTo;
	Vector3 _slideDir;
	float _cameraDip;                // plant settle spring
	float _fovKick;
	float _footstepPhase;
	bool _released;                  // point has begun (Approach freeze over)

	protected override void OnAwake()
	{
		Gun = GetComponent<Revolver>() ?? Components.Create<Revolver>();
		Vitals = GetComponent<Vitals>() ?? Components.Create<Vitals>();
		Hat = Components.GetInChildren<HatComponent>();
		Body.Height = 72f;
		Body.Radius = 14f;
		// FIRM BODIES [LAW]: duelists collide with each other. The 'duelist' tag
		// is included in the controller's collision, never ignored.
		Tags.Add( "duelist" );
	}

	public bool IsLocalControlled => !IsProxy && Network.Owner == Connection.Local && !IsBot;

	// ── Virtual input shim — the bot drives the IDENTICAL controller path ──
	// One body, one physics, one accuracy matrix; the only difference between
	// a human and The Stranger is who fills this buffer. [Honesty Auditor
	// asserts bot ledger output is indistinguishable from human ledger output.]
	public bool IsBot => Components.Get<DrillBot>() is not null;
	Vector3 _botWish;
	readonly System.Collections.Generic.HashSet<string> _botHeld = new();
	// Double-buffered press edges. The bot queues into _botPressedNext; the controller
	// activates them (swap) once at the top of OnFixedUpdate. Reads are idempotent
	// (Contains, not Remove), so a press is visible to EVERY ActionPressed read within
	// the tick — movement AND Revolver — instead of being consumed by the first reader.
	// (The old Remove() consumed the press on first read: bots couldn't mantle because
	// SimulateWalk reads "Jump" twice, and reload-interrupt raced the fire read.)
	System.Collections.Generic.HashSet<string> _botPressed = new();
	System.Collections.Generic.HashSet<string> _botPressedNext = new();

	public void BotMove( Vector3 dir ) => _botWish = dir;
	public void BotPress( string action ) => _botPressedNext.Add( action );
	public void BotHold( string action, bool held ) { if ( held ) _botHeld.Add( action ); else _botHeld.Remove( action ); }

	void BotActivatePresses()
	{
		(_botPressed, _botPressedNext) = (_botPressedNext, _botPressed);
		_botPressedNext.Clear();
	}

	public bool ActionPressed( string a ) => IsBot ? _botPressed.Contains( a ) : (IsLocalControlled && Input.Pressed( a ));
	public bool ActionDown( string a )    => IsBot ? (_botHeld.Contains( a ) || _botPressed.Contains( a )) : (IsLocalControlled && Input.Down( a ));
	public bool ActionReleased( string a )=> !IsBot && IsLocalControlled && Input.Released( a );

	// ── Point lifecycle hooks (MatchDirector) ─────────────────────

	public void ResetForPoint( Transform gate )
	{
		Vitals.ResetForPoint();
		Gun.ResetForPoint();
		Hat?.ResetForPoint();
		WorldPosition = gate.Position;
		EyeAngles = gate.Rotation.Angles().WithPitch( 0 );
		Body.Velocity = 0;
		State = MoveState.Locked;
		CurrentGait = Gait.HolsteredRun;
		IsPlanted = true;
		_released = false;
	}

	public void Release()
	{
		State = MoveState.Grounded;
		_released = true;
	}

	public void ApplyStagger()
	{
		_sinceStagger = 0;
		State = MoveState.Staggered;
		Gun.OnStaggered();     // interrupts reload mid-shell, breaks aim
	}

	// ── Input & simulation ────────────────────────────────────────

	protected override void OnUpdate()
	{
		if ( IsLocalControlled && State != MoveState.Dead )
		{
			// Look
			var look = Input.AnalogLook;
			var e = EyeAngles;
			e.pitch = Math.Clamp( e.pitch + look.pitch, -85f, 85f );
			e.yaw += look.yaw;
			e.roll = 0;
			EyeAngles = e;
		}

		UpdateCameraFeel();
		UpdateAnimationTruth();
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy ) return;
		// Activate this tick's bot presses before any reader (movement + Revolver) runs.
		if ( IsBot ) BotActivatePresses();
		// Locked (gate freeze) / Dead: fully stationary. The previous WithZ(Velocity.z) was
		// a no-op, so a body that died mid-run kept its horizontal velocity.
		if ( State is MoveState.Dead or MoveState.Locked ) { Body.Velocity = Vector3.Zero; return; }

		switch ( State )
		{
			case MoveState.Staggered: SimulateStagger(); break;
			case MoveState.Sliding:   SimulateSlide();   break;
			case MoveState.Mantling:  SimulateMantle();  break;
			default:                  SimulateWalk();    break;
		}

		SimulatePlant();
		SimulateFootsteps();
	}

	// ── Gait resolution ───────────────────────────────────────────

	Gait ResolveGait()
	{
		bool drawn = Gun.IsDrawnOrDrawing;
		if ( ActionDown( "SoftStep" ) || Gun.IsAiming ) return Gait.SoftStep;
		if ( drawn ) return Gait.DrawnWalk;
		return Gait.HolsteredRun; // [LAW] fastest state exists only holstered
	}

	float GaitSpeed( Gait g ) => g switch
	{
		Gait.HolsteredRun => Tuning.HolsteredRunSpeed,
		Gait.DrawnWalk    => Tuning.DrawnWalkSpeed,
		Gait.SoftStep     => Tuning.SoftStepSpeed,
		Gait.Crouch       => Tuning.CrouchSpeed,
		_ => Tuning.DrawnWalkSpeed
	};

	Vector3 WishDir()
	{
		if ( IsBot ) return _botWish.WithZ( 0 );
		var input = Input.AnalogMove;
		if ( input.IsNearlyZero() ) return Vector3.Zero;
		var rot = Rotation.FromYaw( EyeAngles.yaw );
		return (rot * new Vector3( input.x, input.y, 0 )).WithZ( 0 ).Normal;
	}

	// ── Walk / run / air ──────────────────────────────────────────

	void SimulateWalk()
	{
		CurrentGait = ResolveGait();
		var wish = WishDir();
		float speed = GaitSpeed( CurrentGait ) * Vitals.SpeedMultiplier; // wounds never slow [LAW: mult == 1]

		if ( Body.IsOnGround )
		{
			State = MoveState.Grounded;
			Body.Velocity = Body.Velocity.WithZ( 0 );
			Body.Accelerate( wish * speed );
			Body.ApplyFriction( Tuning.GroundFriction );

			// Jump — humble by design
			if ( ActionPressed( "Jump" ) && _released )
			{
				Body.Punch( Vector3.Up * Tuning.JumpVelocity );
				SoundLedger.Report( this, LedgerSound.Jump, Tuning.RadiusWalk );
				State = MoveState.Air;
			}

			// Slide entry — requires genuine run speed
			if ( ActionPressed( "Slide" )
				&& CurrentGait == Gait.HolsteredRun
				&& Body.Velocity.Length >= Tuning.SlideMinEntrySpeed
				&& _sinceSlideEnd > Tuning.SlideCooldown )
			{
				BeginSlide();
			}

			// Mantle probe
			if ( ActionPressed( "Jump" ) && TryBeginMantle() ) return;
		}
		else
		{
			State = MoveState.Air;
			Body.Velocity += Vector3.Down * Tuning.Gravity * Time.Delta;
			// minimal air control [LAW]
			Body.Accelerate( wish * speed * Tuning.AirControl );

			float fallSpeed = -Body.Velocity.z;
			Body.Move();
			if ( Body.IsOnGround )
			{
				OnLanded( fallSpeed );
				State = MoveState.Grounded;
			}
			return;
		}

		Body.Move();
	}

	void OnLanded( float fallSpeed )
	{
		_sinceLanded = 0;
		if ( fallSpeed > Tuning.LandHeavyFall )
		{
			SoundLedger.Report( this, LedgerSound.LandHeavy, Tuning.RadiusLandHeavy );
			_cameraDip = MathF.Min( 8f, fallSpeed / 40f );
		}
	}

	// ── The Plant — Feel Doc §II ──────────────────────────────────

	void SimulatePlant()
	{
		bool stationary = Body.IsOnGround
			&& Body.Velocity.WithZ( 0 ).Length < 10f
			&& WishDir().IsNearlyZero()
			&& State is MoveState.Grounded;

		if ( stationary )
		{
			if ( !IsPlanted )
			{
				float prev = _plantProgress;
				_plantProgress += Time.Delta / Tuning.PlantTime;
				if ( prev <= 0f )
				{
					SoundLedger.Report( this, LedgerSound.BootScuff, Tuning.RadiusSoftStep );
					_cameraDip = Tuning.PlantCameraDip; // the settle
				}
				if ( _plantProgress >= 1f )
				{
					IsPlanted = true;
					FeelTelemetry.Instance?.OnPlant( this );
				}
			}
		}
		else
		{
			_plantProgress = 0f;
			IsPlanted = false;
		}
	}

	/// <summary>
	/// The Revolver's accuracy authority. Landing bloom and the plant grace
	/// window are resolved here so the accuracy matrix has one owner.
	/// </summary>
	public bool CountsAsPlanted =>
		IsPlanted || _plantProgress >= 1f - (Tuning.PlantGraceWindow / Tuning.PlantTime);

	public bool HasLandingBloom => _sinceLanded < Tuning.LandBloomTime;

	// ── Slide — commitment verb with three techs ──────────────────

	void BeginSlide()
	{
		State = MoveState.Sliding;
		_sinceSlideStart = 0;
		_slideDir = Body.Velocity.WithZ( 0 ).Normal;
		Body.Velocity = _slideDir * Tuning.HolsteredRunSpeed * Tuning.SlideSpeedBoost;
		SoundLedger.Report( this, LedgerSound.Slide, Tuning.RadiusSlide ); // spurs & dirt, through walls
		FeelTelemetry.Instance?.OnSlide( this );
	}

	void SimulateSlide()
	{
		float t = _sinceSlideStart / Tuning.SlideDuration;

		// limited steering — commitment, not a drift
		var wish = WishDir();
		if ( !wish.IsNearlyZero() )
		{
			float maxTurn = Tuning.SlideSteerRate * Time.Delta;
			_slideDir = Vector3.Slerp( _slideDir, wish, maxTurn / 57.3f ).WithZ( 0 ).Normal;
		}

		float speed = MathX.Lerp( Tuning.HolsteredRunSpeed * Tuning.SlideSpeedBoost, Tuning.SoftStepSpeed, t * t );
		Body.Velocity = _slideDir * speed + Vector3.Down * Tuning.Gravity * Time.Delta;
		Body.Move();

		// SLIDE-PLANT: canceling in the final window drops straight into a plant.
		bool inCancelWindow = (Tuning.SlideDuration - _sinceSlideStart) <= Tuning.SlideCancelWindow;
		if ( inCancelWindow && (ActionReleased( "Slide" ) || ActionPressed( "SoftStep" )) )
		{
			EndSlide();
			Body.Velocity = Vector3.Zero;
			_plantProgress = 1f;   // frame-perfect arrival: already accurate
			IsPlanted = true;
			FeelTelemetry.Instance?.OnSlidePlant( this );
			return;
		}

		// SLIDE-FADE: releasing early feathers the slide short (bait the pre-fire).
		if ( !inCancelWindow && ActionReleased( "Slide" ) && _sinceSlideStart > 0.15f )
		{
			EndSlide();
			return;
		}

		if ( _sinceSlideStart >= Tuning.SlideDuration )
			EndSlide();
	}

	void EndSlide()
	{
		_sinceSlideEnd = 0;
		State = MoveState.Grounded;
	}

	// ── Mantle — with the mantle-feint ────────────────────────────

	bool TryBeginMantle()
	{
		var forward = Rotation.FromYaw( EyeAngles.yaw ).Forward;
		var chest = WorldPosition + Vector3.Up * 44f;
		var wall = Scene.Trace.Ray( chest, chest + forward * 30f )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( !wall.Hit ) return false;

		// Find the ledge top
		var over = wall.HitPosition + forward * 8f + Vector3.Up * Tuning.MantleMaxHeight;
		var down = Scene.Trace.Ray( over, over + Vector3.Down * Tuning.MantleMaxHeight )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		if ( !down.Hit ) return false;

		float ledgeHeight = down.HitPosition.z - WorldPosition.z;
		if ( ledgeHeight < Tuning.MantleMinHeight || ledgeHeight > Tuning.MantleMaxHeight ) return false;

		State = MoveState.Mantling;
		_mantleStart = 0;
		_mantleFrom = WorldPosition;
		_mantleTo = down.HitPosition + Vector3.Up * 1f;
		Body.Velocity = 0;
		SoundLedger.Report( this, LedgerSound.Mantle, Tuning.RadiusMantle ); // loud & directional [LAW]
		return true;
	}

	void SimulateMantle()
	{
		float t = _mantleStart / Tuning.MantleTime;

		// MANTLE-FEINT: break in the first third — the sound was already sold.
		if ( t < Tuning.MantleBreakWindow / Tuning.MantleTime
			&& (ActionPressed( "Slide" ) || ActionPressed( "Backward" )) )
		{
			State = MoveState.Air;
			FeelTelemetry.Instance?.OnMantleFeint( this );
			return;
		}

		// Ease curve: coil then commit (back-weighted, like the draw)
		float eased = t * t * (3f - 2f * t);
		WorldPosition = Vector3.Lerp( _mantleFrom, _mantleTo, eased )
			+ Vector3.Up * MathF.Sin( t * MathF.PI ) * 8f;

		if ( t >= 1f )
		{
			// Final frames blend into a plant if the player releases into stillness.
			State = MoveState.Grounded;
			if ( WishDir().IsNearlyZero() ) { _plantProgress = 0.6f; }
		}
	}

	void SimulateStagger()
	{
		Body.Velocity = Body.Velocity.LerpTo( Vector3.Zero, 8f * Time.Delta )
			+ Vector3.Down * Tuning.Gravity * Time.Delta;
		Body.Move();
		if ( _sinceStagger >= Tuning.WhipStaggerTime )
			State = MoveState.Grounded;
	}

	// ── Footsteps → the honest ledger ─────────────────────────────

	void SimulateFootsteps()
	{
		if ( State != MoveState.Grounded ) return;
		float speed = Body.Velocity.WithZ( 0 ).Length;
		if ( speed < 20f ) { _footstepPhase = 0; return; }

		float strideLen = CurrentGait == Gait.HolsteredRun ? 62f : CurrentGait == Gait.DrawnWalk ? 48f : 40f;
		_footstepPhase += speed * Time.Delta;
		if ( _footstepPhase >= strideLen )
		{
			_footstepPhase = 0;
			(LedgerSound snd, float radius) = CurrentGait switch
			{
				Gait.HolsteredRun => (LedgerSound.FootstepRun,  Tuning.RadiusRun),
				Gait.DrawnWalk    => (LedgerSound.FootstepWalk, Tuning.RadiusWalk),
				_                 => (LedgerSound.FootstepSoft, Tuning.RadiusSoftStep),
			};
			SoundLedger.Report( this, snd, radius, SurfaceUnder() );
		}
	}

	string SurfaceUnder()
	{
		var tr = Scene.Trace.Ray( WorldPosition + Vector3.Up * 4f, WorldPosition + Vector3.Down * 12f )
			.IgnoreGameObjectHierarchy( GameObject ).Run();
		return tr.Surface?.Name ?? "dirt";
	}

	// ── Camera feel — Feel Doc §VII ───────────────────────────────

	void UpdateCameraFeel()
	{
		if ( !IsLocalControlled || Eye is null ) return;

		// eye height: soft-step drops the camera; crouch drops it more
		float targetEye = CurrentGait == Gait.SoftStep ? Tuning.EyeHeightSoftStep : Tuning.EyeHeight;
		if ( State == MoveState.Sliding ) targetEye = 34f;

		// plant/landing settle dip — spring back
		_cameraDip = _cameraDip.LerpTo( 0f, 10f * Time.Delta );

		// run FOV kick
		float targetKick = (CurrentGait == Gait.HolsteredRun && Body.Velocity.Length > 200f) ? Tuning.RunFovKick : 0f;
		targetKick -= Gun.IsAiming ? Tuning.AimFovTighten : 0f;
		_fovKick = _fovKick.LerpTo( targetKick, 8f * Time.Delta );

		var cam = Scene.Camera;
		if ( cam is not null )
		{
			cam.WorldPosition = WorldPosition + Vector3.Up * (targetEye - _cameraDip);
			cam.WorldRotation = EyeAngles.ToRotation() * Rotation.FromPitch( -Gun.CameraRecoilPitch );
			cam.FieldOfView = Preferences.FieldOfView.Clamp( Tuning.MinFov, Tuning.MaxFov ) + _fovKick;
		}
	}

	/// <summary>
	/// ANIMATION NEVER LIES [LAW]: the third-person model reads state directly
	/// from synced properties; there is no client-side animation state that can
	/// desync from mechanics. This method only pushes those synced facts into
	/// the animgraph parameters.
	/// </summary>
	void UpdateAnimationTruth()
	{
		var renderer = Components.GetInChildren<SkinnedModelRenderer>();
		if ( renderer is null ) return;
		renderer.Set( "gait", (int)CurrentGait );
		renderer.Set( "move_state", (int)State );
		renderer.Set( "planted", IsPlanted );
		renderer.Set( "gun_state", (int)Gun.HandState );
		renderer.Set( "aiming", Gun.IsAiming );
		renderer.Set( "reloading", Gun.Cylinder.IsReloading );
		renderer.Set( "wounded", Vitals.IsWounded );
	}

	public Vector3 EyePosition => WorldPosition + Vector3.Up *
		(CurrentGait == Gait.SoftStep ? Tuning.EyeHeightSoftStep : Tuning.EyeHeight);
	public Rotation EyeRotation => EyeAngles.ToRotation();
}
