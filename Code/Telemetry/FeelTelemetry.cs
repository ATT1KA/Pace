using Sandbox;
using System;
using System.Collections.Generic;
using System.Linq;

namespace TenPaces;

/// <summary>
/// FEEL TELEMETRY — the Feel Document's §XI Definition of Done, instrumented,
/// plus the Design Document's two most important numbers (the p-curve KPIs).
/// Every hook is cheap, aggregated locally, and flushed as one JSON blob at
/// match end (beta: to log + local file; live: to the stats backend).
///
/// What this file makes measurable, per match:
///   • THE P-CURVE: per-point win attribution → beta analysis validates
///     elite-vs-novice ≥ .75/point and adjacent-percentile ≈ .52–.55/point.
///   • Metronome Test: earliness distribution per player (composure histogram).
///   • Hurt Test: cadence earliness delta while wounded vs healthy.
///   • Toy Test: flourish counts outside combat.
///   • Ledger literacy: reload-punish latency (time from opponent's first
///     ShellInsert heard → aggression state change) — proxy via kill timing.
///   • Technique adoption: slide-plants, mantle-feints, feints, just-frames,
///     hat shots — the discovery curve of the timing layer.
///   • Held Breath Test hook: point tension curve proxied by input density
///     (heart-rate telemetry is a lab-playtest instrument, not shipped code).
/// </summary>
public sealed class FeelTelemetry : Component
{
	public static FeelTelemetry Instance { get; private set; }
	protected override void OnAwake() => Instance = this;
	protected override void OnDestroy() { if ( Instance == this ) Instance = null; }

	// ── Per-match aggregates ──────────────────────────────────────
	readonly Dictionary<Guid, PlayerAgg> _players = new();
	readonly List<PointLog> _points = new();
	PointLog _current;
	bool _pointActive;

	sealed class PlayerAgg
	{
		public int Shots, OnBeat, JustFrames, EarlyFires, FanShots;
		public int PlantedShots, AimedShots;
		public int Plants, Slides, SlidePlants, MantleFeints, Feints, Flourishes;
		public int ShellsLoaded, HatShots, TricksUsed;
		public double EarlinessSum, EarlinessSumWounded;
		public int ShotsWounded;
		public int BestTrainerStreak;
	}

	struct PointLog
	{
		public TennisScore.Side Winner;
		public float Duration;
		public HitZone EndZone;
		public string ScoreBefore;
	}

	PlayerAgg Agg( DuelistController d )
	{
		var id = d.GameObject.Id;
		if ( !_players.TryGetValue( id, out var a ) ) { a = new PlayerAgg(); _players[id] = a; }
		return a;
	}

	// ── Hooks (called from gameplay code) ─────────────────────────

	public void OnPointStart( TennisScore score )
	{
		_current = new PointLog { ScoreBefore = score.Serialize() };
		_pointActive = true;
	}

	public void OnPointEnd( TennisScore.Side winner, float duration, HitZone zone )
	{
		// Guard against an end without a start (edge phases) logging a default PointLog.
		if ( !_pointActive ) return;
		_pointActive = false;
		_current.Winner = winner; _current.Duration = duration; _current.EndZone = zone;
		_points.Add( _current );
	}

	public void OnShotFired( DuelistController d, float earliness, bool justFrame, bool fanning, bool planted, bool aimed )
	{
		var a = Agg( d );
		a.Shots++;
		a.EarlinessSum += earliness;
		if ( earliness <= 0.001f ) a.OnBeat++;
		else a.EarlyFires++;
		if ( justFrame ) a.JustFrames++;
		if ( fanning ) a.FanShots++;
		if ( planted ) a.PlantedShots++;
		if ( aimed ) a.AimedShots++;
		if ( d.Vitals.IsWounded ) { a.ShotsWounded++; a.EarlinessSumWounded += earliness; }
	}

	public void OnPlant( DuelistController d )        => Agg( d ).Plants++;
	public void OnSlide( DuelistController d )        => Agg( d ).Slides++;
	public void OnSlidePlant( DuelistController d )   => Agg( d ).SlidePlants++;
	public void OnMantleFeint( DuelistController d )  => Agg( d ).MantleFeints++;
	public void OnFeint( DuelistController d )        => Agg( d ).Feints++;
	public void OnFlourish( DuelistController d, string f ) => Agg( d ).Flourishes++;
	public void OnShellLoaded( DuelistController d, int loaded ) => Agg( d ).ShellsLoaded++;
	public void OnHatShot( DuelistController shooter, DuelistController victim ) => Agg( shooter ).HatShots++;
	public void OnTrickUsed( DuelistController d, TrickId t ) => Agg( d ).TricksUsed++;
	public void OnTrainerStreak( DuelistController d, int streak, bool justFrame ) =>
		Agg( d ).BestTrainerStreak = Math.Max( Agg( d ).BestTrainerStreak, streak ); // range leaderboard → flushed with the match report

	// ── Flush at match end ────────────────────────────────────────

	public string BuildReport( MatchDirector director )
	{
		var report = new
		{
			ground = director.Ground?.GroundName,
			finalScore = director.ClientScore.Serialize(),
			points = _points.Select( p => new { winner = p.Winner.ToString(), p.Duration, zone = p.EndZone.ToString() } ),
			players = _players.ToDictionary(
				kv => kv.Key.ToString(),
				kv => new
				{
					kv.Value.Shots, kv.Value.OnBeat, kv.Value.JustFrames, kv.Value.EarlyFires,
					composure = kv.Value.Shots > 0 ? Math.Clamp( 1.0 - (kv.Value.EarlinessSum / kv.Value.Shots), 0.0, 1.0 ) : 1.0,
					composureWounded = kv.Value.ShotsWounded > 0 ? Math.Clamp( 1.0 - (kv.Value.EarlinessSumWounded / kv.Value.ShotsWounded), 0.0, 1.0 ) : 1.0,
					plantedShotRate = kv.Value.Shots > 0 ? (double)kv.Value.PlantedShots / kv.Value.Shots : 0,
					kv.Value.Plants, kv.Value.Slides, kv.Value.SlidePlants, kv.Value.MantleFeints,
					kv.Value.Feints, kv.Value.Flourishes, kv.Value.ShellsLoaded, kv.Value.HatShots, kv.Value.TricksUsed,
					kv.Value.BestTrainerStreak,
				} ),
			// Broadcast layer / beta dashboard: theoretical match prob at observed per-point split
			winProbCheck = _points.Count > 0
				? TennisScore.MatchWinProbability( (double)_points.Count( p => p.Winner == TennisScore.Side.A ) / _points.Count )
				: 0.5,
		};
		return System.Text.Json.JsonSerializer.Serialize( report );
	}

	public void Flush( MatchDirector director )
	{
		var json = BuildReport( director );
		Log.Info( $"[TELEMETRY] {json}" );
		try { FileSystem.Data.WriteAllText( $"telemetry_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json", json ); }
		catch ( Exception e ) { Log.Warning( $"telemetry write failed: {e.Message}" ); }
		_players.Clear();
		_points.Clear();
	}
}

/// <summary>
/// THE RECORD — every duelist's permanent public history begins here.
/// Beta: local file + s&box stats; live: the backend service that also runs
/// the Territory ladder. The schema is the product: career W/L, per-match
/// score lines, walkovers honestly marked, telemetry attached.
/// </summary>
public static class MatchRecord
{
	public static void WriteResult( MatchDirector director, TennisScore score )
	{
		var entry = new
		{
			at = DateTime.UtcNow,
			ground = director.Ground?.GroundName,
			a = director.NameOf( TennisScore.Side.A ),
			b = director.NameOf( TennisScore.Side.B ),
			winner = score.Winner?.ToString(),
			sets = score.SetHistory,
			forfeit = false,
		};
		Persist( entry );
		FeelTelemetry.Instance?.Flush( director );
	}

	public static void WriteForfeit( MatchDirector director, TennisScore.Side winner )
	{
		var entry = new
		{
			at = DateTime.UtcNow,
			ground = director.Ground?.GroundName,
			a = director.NameOf( TennisScore.Side.A ),
			b = director.NameOf( TennisScore.Side.B ),
			winner = winner.ToString(),
			sets = director.ClientScore.SetHistory,
			forfeit = true,
		};
		Persist( entry );
		// Flush telemetry on forfeit too — otherwise a walkover match is never persisted
		// and its aggregates bleed into the next match's numbers.
		FeelTelemetry.Instance?.Flush( director );
	}

	static void Persist( object entry )
	{
		try
		{
			var json = System.Text.Json.JsonSerializer.Serialize( entry );
			Log.Info( $"[RECORD] {json}" );
			var existing = FileSystem.Data.FileExists( "the_record.jsonl" )
				? FileSystem.Data.ReadAllText( "the_record.jsonl" ) : "";
			FileSystem.Data.WriteAllText( "the_record.jsonl", existing + json + "\n" );
		}
		catch ( Exception e ) { Log.Warning( $"record write failed: {e.Message}" ); }
	}
}
