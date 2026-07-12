using Sandbox;
using System;
using System.Linq;

namespace TenPaces;

/// <summary>
/// The MatchDirector is the host-authoritative conductor of a Ten Paces match.
/// One exists per scene. It owns:
///   • the phase state machine (the shape of every point and every match)
///   • the TennisScore instance (via json [Sync] so all clients render truth)
///   • Initiative resolution (coin flip → alternation), Hour selection, gate picks
///   • the Reckoning, changeovers, set breaks, and the point-end ceremony
///   • authoritative kill adjudication (the only code path that awards points)
///
/// Everything visual/audible about these beats lives elsewhere (Umpire, UI,
/// CeremonyOverlay); the Director only changes state and broadcasts facts.
/// </summary>
public enum MatchPhase
{
	Lobby,          // waiting for two duelists + ready-up
	CoinFlip,       // opening ceremony — decides first Initiative
	InitiativePick, // holder selects Hour + spawn gate (timed)
	Approach,       // players locked at gates for the "ten paces" beat, then released
	Live,           // the duel — Approach/Draw/Exchange all happen here mechanically
	Reckoning,      // timer expired: forced resolution
	PointCeremony,  // bell, umpire call, tableau
	Changeover,     // 45s adaptation window after odd games
	SetBreak,       // 90s between sets
	MatchEnd,       // victory ceremony, then back to lobby
}

public sealed class MatchDirector : Component, Component.INetworkListener
{
	public static MatchDirector Instance { get; private set; }

	[Property] public GroundDefinition Ground { get; set; }
	[Property] public bool BestOfFive { get; set; } = false;
	[Property] public bool QuickFormat { get; set; } = false; // single game — the callout format

	// ── Synced match truth ────────────────────────────────────────
	[Sync] public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
	[Sync] public string ScoreJson { get; private set; } = "";
	[Sync] public float PhaseEndsAt { get; private set; }          // Time.Now deadline where applicable
	[Sync] public float PointClock { get; private set; }           // seconds elapsed in Live
	[Sync] public Guid DuelistA { get; private set; }              // GameObject ids of seated players
	[Sync] public Guid DuelistB { get; private set; }
	[Sync] public GroundHour Hour { get; private set; } = GroundHour.HighNoon;
	[Sync] public int GateA { get; private set; }
	[Sync] public int GateB { get; private set; }
	[Sync] public string LastCall { get; private set; } = "";      // umpire's last utterance (UI mirror)

	public TennisScore Score { get; private set; } = new();
	TennisScore.Side? _pendingPointWinner;
	TimeSince _phaseTime;
	bool _reckoningBellRung;

	protected override void OnAwake()
	{
		Instance = this;
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null; // don't leave a destroyed singleton dangling on scene reload
	}

	protected override void OnStart()
	{
		if ( Networking.IsHost )
			PushScore();
	}

	// ── Seating ───────────────────────────────────────────────────

	public DuelistController GetDuelist( TennisScore.Side side )
	{
		var id = side == TennisScore.Side.A ? DuelistA : DuelistB;
		return Scene.Directory.FindByGuid( id )?.GetComponent<DuelistController>();
	}

	public TennisScore.Side? SideOf( DuelistController d )
	{
		if ( d.GameObject.Id == DuelistA ) return TennisScore.Side.A;
		if ( d.GameObject.Id == DuelistB ) return TennisScore.Side.B;
		return null;
	}

	/// <summary> Called by LobbySystem when two players have claimed seats and readied. </summary>
	public void SeatAndStart( GameObject a, GameObject b )
	{
		Assert.True( Networking.IsHost );
		DuelistA = a.Id;
		DuelistB = b.Id;
		Score = new TennisScore( BestOfFive ? Tuning.SetsToWinFinal : Tuning.SetsToWinStandard,
			TennisScore.Side.A /* replaced by coin flip */ );
		PushScore();
		EnterPhase( MatchPhase.CoinFlip, 4f );
		Umpire.Instance?.AnnounceMatchStart( NameOf( TennisScore.Side.A ), NameOf( TennisScore.Side.B ), BestOfFive );
	}

	public string NameOf( TennisScore.Side s ) =>
		GetDuelist( s )?.Network.Owner?.DisplayName ?? (s == TennisScore.Side.A ? "Duelist A" : "Duelist B");

	/// <summary> Host-authoritative mirror of the Umpire's last utterance, for late-joiner UI. </summary>
	public void SetLastCall( string line ) { if ( Networking.IsHost ) LastCall = line; }

	// ── Phase machine ─────────────────────────────────────────────

	void EnterPhase( MatchPhase phase, float duration = 0 )
	{
		Phase = phase;
		_phaseTime = 0;
		PhaseEndsAt = duration > 0 ? Time.Now + duration : 0;
		BroadcastPhase( phase );
	}

	[Rpc.Broadcast]
	void BroadcastPhase( MatchPhase phase )
	{
		ISceneEvent<IMatchEvents>.Post( x => x.OnPhaseChanged( phase ) );
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;

		switch ( Phase )
		{
			case MatchPhase.CoinFlip:
				if ( PhaseExpired() ) ResolveCoinFlip();
				break;

			case MatchPhase.InitiativePick:
				if ( PhaseExpired() ) FinalizeInitiativePick( autoResolved: true );
				break;

			case MatchPhase.Approach:
				if ( PhaseExpired() ) BeginLive();
				break;

			case MatchPhase.Live:
				PointClock += Time.Delta;
				if ( !_reckoningBellRung && PointClock >= Tuning.ReckoningWarning )
				{
					_reckoningBellRung = true;
					Umpire.Instance?.ReckoningWarning();
				}
				if ( PointClock >= Tuning.PointTimeLimit )
					BeginReckoning();
				break;

			case MatchPhase.Reckoning:
				TickReckoning();
				break;

			case MatchPhase.PointCeremony:
				if ( PhaseExpired() ) AfterCeremony();
				break;

			case MatchPhase.Changeover:
			case MatchPhase.SetBreak:
				if ( PhaseExpired() ) SetupNextPoint();
				break;

			case MatchPhase.MatchEnd:
				if ( PhaseExpired() ) ReturnToLobby();
				break;
		}
	}

	bool PhaseExpired() => PhaseEndsAt > 0 && Time.Now >= PhaseEndsAt;

	// ── Coin flip → Initiative ────────────────────────────────────

	void ResolveCoinFlip()
	{
		var first = Game.Random.Int( 0, 1 ) == 0 ? TennisScore.Side.A : TennisScore.Side.B;
		Score = new TennisScore( Score.SetsToWin, first );
		PushScore();
		Umpire.Instance?.CallCoinFlip( NameOf( first ) );
		BeginInitiativePick();
	}

	void BeginInitiativePick()
	{
		EnterPhase( MatchPhase.InitiativePick, Tuning.InitiativePickTime );
		// Holder's client shows the pick UI (Hour + gate). Non-holders see "opponent choosing".
		var holder = GetDuelist( Score.Initiative );
		if ( holder is not null )
			NotifyInitiativePick( holder.GameObject.Id );
	}

	[Rpc.Broadcast]
	void NotifyInitiativePick( Guid holderId ) =>
		ISceneEvent<IMatchEvents>.Post( x => x.OnInitiativePickStarted( holderId ) );

	/// <summary> Called (via RPC from the holder's client) with their choices. </summary>
	[Rpc.Host]
	public void SubmitInitiativePick( GroundHour hour, int gateIndex )
	{
		if ( Phase != MatchPhase.InitiativePick ) return;
		var holder = GetDuelist( Score.Initiative );
		if ( holder is null || Rpc.Caller != holder.Network.Owner ) return; // only the holder chooses

		Hour = hour;
		int gates = Ground?.GateCount ?? 3;
		int pick = Math.Clamp( gateIndex, 0, gates - 1 );
		if ( Score.Initiative == TennisScore.Side.A ) GateA = pick; else GateB = pick;
		FinalizeInitiativePick( autoResolved: false );
	}

	void FinalizeInitiativePick( bool autoResolved )
	{
		int gates = Ground?.GateCount ?? 3;
		if ( autoResolved )
		{
			Hour = Game.Random.Int( 0, 1 ) == 0 ? GroundHour.HighNoon : GroundHour.Dusk;
			if ( Score.Initiative == TennisScore.Side.A ) GateA = Game.Random.Int( 0, gates - 1 );
			else GateB = Game.Random.Int( 0, gates - 1 );
		}
		// Receiver's gate: mirrored index by default; receiver may pre-set a preference later (beta: random)
		if ( Score.Initiative == TennisScore.Side.A ) GateB = Game.Random.Int( 0, gates - 1 );
		else GateA = Game.Random.Int( 0, gates - 1 );

		Ground?.ApplyHour( Hour );
		BeginApproach();
	}

	// ── Point lifecycle ───────────────────────────────────────────

	void BeginApproach()
	{
		_pendingPointWinner = null;
		_reckoningBellRung = false;
		PointClock = 0;

		var a = GetDuelist( TennisScore.Side.A );
		var b = GetDuelist( TennisScore.Side.B );
		if ( a is null || b is null ) { EnterPhase( MatchPhase.Lobby ); return; }

		a.ResetForPoint( Ground.GetGate( TennisScore.Side.A, GateA ) );
		b.ResetForPoint( Ground.GetGate( TennisScore.Side.B, GateB ) );

		EnterPhase( MatchPhase.Approach, Tuning.ApproachFreeze );
		Umpire.Instance?.CallPointStakes( Score.NextPointStakes(), NameOf );
	}

	void BeginLive()
	{
		var a = GetDuelist( TennisScore.Side.A );
		var b = GetDuelist( TennisScore.Side.B );
		a?.Release(); b?.Release();
		EnterPhase( MatchPhase.Live );
		FeelTelemetry.Instance?.OnPointStart( Score );
	}

	void BeginReckoning()
	{
		EnterPhase( MatchPhase.Reckoning );
		Umpire.Instance?.CallReckoning();
		Ground?.BeginReckoning();
	}

	void TickReckoning()
	{
		// After the grace window, standing outside the resolve volume bleeds.
		if ( _phaseTime < Tuning.ReckoningGrace ) return;

		// Hard backstop: past the sudden-death cap the resolve volume COLLAPSES and
		// pressure applies to everyone, so two duelists both standing at the heart
		// can never hang the point (and the match) indefinitely.
		bool collapsed = _phaseTime >= Tuning.ReckoningSuddenDeath;

		foreach ( var side in new[] { TennisScore.Side.A, TennisScore.Side.B } )
		{
			var d = GetDuelist( side );
			if ( d is null || !d.Vitals.Alive ) continue;
			bool outside = Ground is null || !Ground.InResolveVolume( d.WorldPosition );
			if ( collapsed || outside )
				d.Vitals.ApplyReckoningPressure( Tuning.ReckoningTickDmg * Time.Delta );
		}
	}

	/// <summary>
	/// THE only point-award path. Called by Vitals on the host when a duelist dies.
	/// </summary>
	public void OnDuelistKilled( DuelistController victim, DuelistController killer, HitZone zone )
	{
		if ( !Networking.IsHost ) return;
		if ( Phase != MatchPhase.Live && Phase != MatchPhase.Reckoning ) return;
		var victimSide = SideOf( victim );
		if ( victimSide is null ) return;

		_pendingPointWinner = TennisScore.Other( victimSide.Value );
		FeelTelemetry.Instance?.OnPointEnd( _pendingPointWinner.Value, PointClock, zone );

		// The ceremony: hitstop is client-side (Revolver fires it), here we run the bell/call beat.
		EnterPhase( MatchPhase.PointCeremony, Tuning.CeremonyHold );
		BroadcastKillCeremony( victim.GameObject.Id, (int)zone );
	}

	[Rpc.Broadcast]
	void BroadcastKillCeremony( Guid victimId, int zone ) =>
		ISceneEvent<IMatchEvents>.Post( x => x.OnKillCeremony( victimId, (HitZone)zone ) );

	void AfterCeremony()
	{
		if ( _pendingPointWinner is null ) { SetupNextPoint(); return; }

		var result = Score.AwardPoint( _pendingPointWinner.Value );
		PushScore();

		Umpire.Instance?.CallAfterPoint( result, Score, NameOf );

		if ( result.MatchWinner is TennisScore.Side mw )
		{
			EnterPhase( MatchPhase.MatchEnd, Tuning.MatchEndHold );
			BroadcastMatchEnd( mw == TennisScore.Side.A ? DuelistA : DuelistB );
			MatchRecord.WriteResult( this, Score );
			return;
		}

		if ( QuickFormat && result.GameWinner is TennisScore.Side gw )
		{
			// Callout format: one game IS the match.
			EnterPhase( MatchPhase.MatchEnd, Tuning.MatchEndHold );
			BroadcastMatchEnd( gw == TennisScore.Side.A ? DuelistA : DuelistB );
			MatchRecord.WriteResult( this, Score );
			return;
		}

		if ( result.SetWinner is not null )      EnterPhase( MatchPhase.SetBreak, Tuning.SetBreakTime );
		else if ( result.IsChangeover )          EnterPhase( MatchPhase.Changeover, Tuning.ChangeoverTime );
		else                                     SetupNextPoint();
	}

	void SetupNextPoint()
	{
		// New game (points at 0-0) → the Initiative holder re-picks Hour/gate.
		if ( Score.Points[0] == 0 && Score.Points[1] == 0 && !Score.InTiebreak )
			BeginInitiativePick();
		else
			BeginApproach();
	}

	/// <summary>
	/// End the match immediately as a walkover (mid-match disconnect). Called by
	/// LobbySystem on the host. Runs the same MatchEnd → Lobby path as a natural
	/// finish so the session never gets stranded, and records the forfeit honestly.
	/// </summary>
	public void ForfeitMatch( TennisScore.Side winner )
	{
		if ( !Networking.IsHost ) return;
		if ( Phase is MatchPhase.Lobby or MatchPhase.MatchEnd ) return;

		EnterPhase( MatchPhase.MatchEnd, Tuning.MatchEndHold );
		BroadcastMatchEnd( winner == TennisScore.Side.A ? DuelistA : DuelistB );
		MatchRecord.WriteForfeit( this, winner );
	}

	/// <summary> Victory ceremony over: reset match state and re-open the lobby for a new callout. </summary>
	void ReturnToLobby()
	{
		DuelistA = Guid.Empty;
		DuelistB = Guid.Empty;
		_pendingPointWinner = null;
		_reckoningBellRung = false;
		PointClock = 0;
		Score = new TennisScore( BestOfFive ? Tuning.SetsToWinFinal : Tuning.SetsToWinStandard, TennisScore.Side.A );
		PushScore();
		EnterPhase( MatchPhase.Lobby );
	}

	[Rpc.Broadcast]
	void BroadcastMatchEnd( Guid winnerId ) =>
		ISceneEvent<IMatchEvents>.Post( x => x.OnMatchEnd( winnerId ) );

	void PushScore() => ScoreJson = Score.Serialize();

	/// <summary> Clients rebuild a readable score from the synced json. </summary>
	public TennisScore ClientScore =>
		Networking.IsHost ? Score : (string.IsNullOrEmpty( ScoreJson ) ? new TennisScore() : TennisScore.Deserialize( ScoreJson ));

	// ── Changeover Form/Trick swaps ───────────────────────────────
	public bool LoadoutSwapAllowed =>
		Phase is MatchPhase.Changeover or MatchPhase.SetBreak or MatchPhase.Lobby or MatchPhase.CoinFlip;
}

/// <summary> Scene-wide match event bus. UI, audio, telemetry, bot all subscribe. </summary>
public interface IMatchEvents : ISceneEvent<IMatchEvents>
{
	void OnPhaseChanged( MatchPhase phase ) { }
	void OnInitiativePickStarted( Guid holderId ) { }
	void OnKillCeremony( Guid victimId, HitZone zone ) { }
	void OnMatchEnd( Guid winnerId ) { }
}
