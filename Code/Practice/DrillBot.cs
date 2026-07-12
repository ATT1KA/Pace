using Sandbox;
using System;
using System.Linq;

namespace TenPaces;

public enum StrangerTier
{
	Drifter,    // teaches the verbs: slow, honest, forgiving
	Deputy,     // punishes obvious mistakes, holds simple patterns
	Bounty,     // sound-literate, counts your shells, exploits tendencies
	TheStranger // uncanny: near-perfect cadence, deliberate pattern-breaking
}

/// <summary>
/// THE STRANGER — an AI duelist, and a load-bearing one:
///
///   • ONBOARDING: your first three duels are against the Drifter. The bot
///     teaches by BEING readable — it telegraphs honestly, over-commits to
///     draws, reloads in the open. Learning to beat it IS the tutorial.
///   • COLD START: the Territory is only magic when populated; until your
///     county is, there is always someone to duel. The Stranger keeps every
///     empty region alive.
///   • PATTERN PEDAGOGY [the design's quiet trick]: the bot runs on explicit
///     TENDENCIES (route bias, draw-distance bias, aggression rhythm) that
///     persist across points and only shift at changeovers — exactly like a
///     human. Learning to read The Stranger is literally practicing the skill
///     that beats humans. The bot is a reads-tutor wearing a poncho.
///
/// Deliberate anti-goal: the bot NEVER aimbots. Its accuracy comes from the
/// same accuracy matrix humans use — it plants, it rides the beat, it misses
/// when it fires early. Its skill knobs are DISCIPLINE knobs, which keeps the
/// difficulty curve on-message: better bots are more composed, not faster.
/// </summary>
public sealed class DrillBot : Component, ILedgerEvents
{
	[Property] public StrangerTier Tier { get; set; } = StrangerTier.Drifter;

	DuelistController Body;
	Revolver Gun => Body.Gun;

	// ── Tendencies: the bot's readable soul (re-rolled at changeovers) ──
	float _routeBias;          // -1 left route … +1 right route
	float _preferredRange;     // engagement distance it seeks
	float _aggression;         // 0 patient … 1 pressing
	float _feintRate;          // probability a draw is a feint
	TimeSince _sinceDecision = 0;
	Vector3 _lastHeard;
	TimeSince _sinceHeard = 999;
	bool _heardAnything;
	Vector3 _moveTarget;

	// Discipline knobs by tier — composure, not speed
	float ReactionDelay => Tier switch { StrangerTier.Drifter => 0.55f, StrangerTier.Deputy => 0.38f, StrangerTier.Bounty => 0.26f, _ => 0.19f };
	float BeatDiscipline => Tier switch { StrangerTier.Drifter => 0.5f, StrangerTier.Deputy => 0.75f, StrangerTier.Bounty => 0.92f, _ => 0.99f };
	float PlantDiscipline => Tier switch { StrangerTier.Drifter => 0.4f, StrangerTier.Deputy => 0.7f, StrangerTier.Bounty => 0.9f, _ => 0.98f };
	float AimJitterDeg => Tier switch { StrangerTier.Drifter => 4.5f, StrangerTier.Deputy => 2.6f, StrangerTier.Bounty => 1.2f, _ => 0.5f };

	protected override void OnAwake()
	{
		Body = GetComponent<DuelistController>() ?? Components.Create<DuelistController>();
		RerollTendencies();
	}

	public void RerollTendencies()
	{
		_routeBias = Game.Random.Float( -1f, 1f );
		_preferredRange = Tier == StrangerTier.Drifter ? 500f : Game.Random.Float( 300f, 1400f );
		_aggression = Game.Random.Float( 0.2f, 0.9f );
		_feintRate = Tier >= StrangerTier.Bounty ? Game.Random.Float( 0.1f, 0.35f ) : 0f;
	}

	// The bot hears through the SAME ledger the players hear through.
	public void OnLedgerSound( Guid sourceId, Vector3 pos, LedgerSound sound, float radius )
	{
		if ( Body is null || sourceId == Body.GameObject.Id ) return;
		if ( pos.Distance( Body.WorldPosition ) > radius ) return;   // honest ears
		_lastHeard = pos;
		_sinceHeard = 0;
		_heardAnything = true;

		// Bounty+ counts shells: a dry opponent flips aggression hard.
		if ( Tier >= StrangerTier.Bounty && sound == LedgerSound.DeadClick )
			_aggression = 1f;
		if ( Tier >= StrangerTier.Bounty && sound is LedgerSound.ShellInsert or LedgerSound.GateOpen )
			_aggression = MathF.Min( 1f, _aggression + 0.4f ); // punish the reload
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || Body is null ) return;
		var director = MatchDirector.Instance;
		if ( director is null ) return;

		if ( director.Phase is MatchPhase.Changeover or MatchPhase.SetBreak && _sinceDecision > 5f )
		{
			RerollTendencies();  // adapts at changeovers — like a human, readably
			_sinceDecision = 0;
		}
		if ( director.Phase is not (MatchPhase.Live or MatchPhase.Reckoning) ) { ReleaseBotHolds(); return; }

		var enemy = Enemy( director );
		if ( enemy is null || !Body.Vitals.Alive ) return;

		Sense( enemy );
		Decide( enemy );
	}

	DuelistController Enemy( MatchDirector d )
	{
		var side = d.SideOf( Body );
		return side is null ? null : d.GetDuelist( TennisScore.Side.Other( side.Value ) );
	}

	bool _canSeeEnemy;
	Vector3 _enemyPos;

	void Sense( DuelistController enemy )
	{
		var tr = Scene.Trace.Ray( Body.EyePosition, enemy.EyePosition )
			.IgnoreGameObjectHierarchy( Body.GameObject ).Run();
		_canSeeEnemy = tr.GameObject?.Components.GetInParentOrSelf<DuelistController>() == enemy;
		if ( _canSeeEnemy ) { _enemyPos = enemy.WorldPosition; _lastHeard = enemy.WorldPosition; _sinceHeard = 0; }
	}

	void Decide( DuelistController enemy )
	{
		ReleaseBotHolds(); // default: not aiming/fanning unless BotFight re-asserts this tick
		float dist = Body.WorldPosition.Distance( enemy.WorldPosition );

		if ( _canSeeEnemy )
		{
			FaceToward( enemy.EyePosition - Vector3.Up * (Game.Random.Float() < 0.15f ? 0f : 14f) ); // mostly center-mass; sometimes hunts the perfect zone
			bool wantFight = dist <= _preferredRange * 1.4f || _aggression > 0.7f;

			if ( wantFight )
			{
				if ( Gun.HandState == HandState.Holstered ) BotDraw();
				else if ( Gun.HandState == HandState.Drawn ) BotFight( dist );
			}
			else
			{
				// reposition toward preferred range; discipline decides whether we do it planted
				MoveToward( PreferredStandoff( enemy ) );
			}
		}
		else
		{
			// Information game: chase the last sound, or claim space on route bias.
			if ( _sinceHeard < 4f && _heardAnything )
				MoveToward( _lastHeard );
			else
				Patrol();

			// Reload hygiene when unseen — the bot models cylinder discipline.
			if ( !Gun.Cylinder.IsReloading && Gun.Cylinder.Loaded < 4 && Gun.HandState == HandState.Drawn )
				Gun.Cylinder.BeginReload();
		}

		if ( MatchDirector.Instance.Phase == MatchPhase.Reckoning )
			MoveToward( MatchDirector.Instance.Ground?.ResolveVolumeCenter?.WorldPosition ?? _lastHeard );
	}

	void BotDraw()
	{
		if ( _sinceDecision < ReactionDelay ) return;
		_sinceDecision = 0;
		// Bounty+ occasionally sells a feint — a REAL draw genuinely begun.
		if ( Game.Random.Float() < _feintRate ) BotFeint();
		else SimulatePress( "DrawHolster" );
	}

	void BotFeint()
	{
		SimulatePress( "DrawHolster" );
		_pendingFeintCancel = 0.1f;
	}
	float _pendingFeintCancel = -1;

	void BotFight( float dist )
	{
		// Plant discipline: composed bots stop before they shoot.
		bool willPlant = Game.Random.Float() < PlantDiscipline;
		if ( willPlant && !Body.CountsAsPlanted ) StopMoving();

		// Fanning form closes and bursts at short range: hip-fire, never aimed.
		bool fan = Gun.FormDef.CanFan && dist <= Gun.FormDef.EffectiveRange && _aggression > 0.6f;

		// AIM: holding Aim is how the bot reaches the planted-aimed 0-degree TRUE cone that
		// humans get. Without ever holding Aim the bot was permanently stuck at hip accuracy.
		Body.BotHold( "Aim", willPlant && !fan );
		// Sustain an in-progress fan — TickFan continues only while Attack is held down.
		Body.BotHold( "Attack", fan && Gun.IsFanning );
		if ( fan && Gun.IsFanning ) return; // let the current burst play out

		// Beat discipline: composed bots wait for the true beat — a REAL check now.
		if ( !BotBeatReady() && Game.Random.Float() < BeatDiscipline ) return;
		// If aiming, wait for the raise to settle so the shot is actually TRUE.
		if ( willPlant && !fan && !Gun.IsAimSettled ) return;

		if ( _sinceDecision < ReactionDelay * 0.6f ) return;
		_sinceDecision = 0;

		// aim jitter — human hands, not an aimbot
		var jitter = Vector3.Random * AimJitterDeg * 0.01f;
		FaceToward( _enemyPos + Vector3.Up * 44f + jitter * 100f );

		if ( fan ) Body.BotHold( "Attack", true ); // held as the burst begins
		SimulatePress( "Attack" );                 // the trigger edge: fires, or starts the fan
	}

	// Firing on the beat means earliness 0 (no slip-hammer bloom). The Revolver owns the
	// beat clock; the bot reads it and CHOOSES to wait (weighted by BeatDiscipline) — it
	// never bypasses the bloom, exactly like a disciplined human.
	bool BotBeatReady() => Gun.BeatReady;

	void ReleaseBotHolds()
	{
		Body.BotHold( "Aim", false );
		Body.BotHold( "Attack", false );
	}

	// ── Movement / facing plumbing (drives the same controller humans use) ──
	// The bot writes to a virtual input layer on the controller — one body,
	// one physics, one accuracy matrix, zero special cases. [Honesty Auditor
	// verifies the bot emits ledger sounds identically to humans.]

	void MoveToward( Vector3 target ) { _moveTarget = target; Body.BotMove( (target - Body.WorldPosition).WithZ( 0 ).Normal ); }
	void StopMoving() => Body.BotMove( Vector3.Zero );
	void Patrol()
	{
		if ( _moveTarget == default || Body.WorldPosition.Distance( _moveTarget ) < 60f )
		{
			var center = MatchDirector.Instance.Ground?.ResolveVolumeCenter?.WorldPosition ?? Vector3.Zero;
			var right = Rotation.FromYaw( Game.Random.Float( 0, 360 ) ).Forward;
			_moveTarget = center + right * Game.Random.Float( 150f, 500f ) + Vector3.Right * _routeBias * 200f;
		}
		Body.BotMove( (_moveTarget - Body.WorldPosition).WithZ( 0 ).Normal );
	}

	void FaceToward( Vector3 worldPos )
	{
		var dir = (worldPos - Body.EyePosition).Normal;
		var target = Rotation.LookAt( dir ).Angles();
		var e = Body.EyeAngles;
		float speed = 220f * Time.Delta; // degrees/sec — a neck, not a servo
		e.yaw = e.yaw.LerpDegreesTo( target.yaw, speed / 60f );
		e.pitch = e.pitch.LerpTo( target.pitch, speed / 60f );
		Body.EyeAngles = e;
	}

	void SimulatePress( string action ) => Body.BotPress( action );

	protected override void OnUpdate()
	{
		if ( _pendingFeintCancel > 0 )
		{
			_pendingFeintCancel -= Time.Delta;
			if ( _pendingFeintCancel <= 0 ) SimulatePress( "DrawHolster" ); // cancels inside the feint window
		}
	}
}
