using System;
using System.Text.Json.Serialization;

namespace TenPaces;

/// <summary>
/// The complete tennis scoring model: points → games (with deuce/advantage) →
/// sets (win by two, tiebreak at 6-6) → match. Pure C#, engine-free, fully
/// serializable, unit-testable, and shared verbatim by the game client, the
/// server, the spectator UI, and the backend (match records / the Record).
///
/// This class is the Design Document's central wager rendered executable:
/// the skill-amplification math lives HERE, not in the gunplay.
///
/// It also carries the sports grammar the Umpire speaks: break points,
/// set points, match points, deuce — detected structurally, not hardcoded.
/// </summary>
public sealed class TennisScore
{
	public enum Side { A = 0, B = 1 }

	// ── Live state ────────────────────────────────────────────────
	[JsonInclude] public int[] Points  { get; private set; } = { 0, 0 };  // within current game (or tiebreak)
	[JsonInclude] public int[] Games   { get; private set; } = { 0, 0 };  // within current set
	[JsonInclude] public int[] Sets    { get; private set; } = { 0, 0 };
	[JsonInclude] public bool  InTiebreak { get; private set; }
	[JsonInclude] public Side  Initiative { get; private set; }           // the "serve" — alternates per game
	[JsonInclude] public int   SetsToWin  { get; private set; }
	[JsonInclude] public int   TotalPointsPlayed { get; private set; }
	[JsonInclude] public int   TotalGamesPlayed  { get; private set; }
	[JsonInclude] public bool  MatchOver  { get; private set; }
	[JsonInclude] public Side? Winner     { get; private set; }

	// Set history for the scorecard / the Record (games won per completed set)
	[JsonInclude] public System.Collections.Generic.List<(int a, int b)> SetHistory { get; private set; } = new();

	public TennisScore() : this( Tuning.SetsToWinStandard, Side.A ) { }

	public TennisScore( int setsToWin, Side firstInitiative )
	{
		SetsToWin = setsToWin;
		Initiative = firstInitiative;
	}

	// ── The one mutator ───────────────────────────────────────────

	/// <summary>
	/// Award a point (one duel won). Returns a structured result describing
	/// everything that just happened, so the MatchDirector and Umpire never
	/// re-derive scoring logic.
	/// </summary>
	public PointResult AwardPoint( Side winner )
	{
		if ( MatchOver ) throw new InvalidOperationException( "Match is over." );

		TotalPointsPlayed++;
		Points[(int)winner]++;

		var result = new PointResult { PointWinner = winner };

		bool gameWon = InTiebreak
			? Points[(int)winner] >= Tuning.TiebreakTo && Lead( Points, winner ) >= 2
			: Points[(int)winner] >= Tuning.PointsPerGame && Lead( Points, winner ) >= 2;

		// A point won against the Initiative holder that COULD win the game is a break point
		// conversion; detect before we mutate game state.
		result.WasBreakConversion = gameWon && winner != Initiative && !InTiebreak;

		if ( gameWon )
		{
			result.GameWinner = winner;
			TotalGamesPlayed++;
			Games[(int)winner]++;
			Points[0] = Points[1] = 0;

			bool setWon = InTiebreak
				|| (Games[(int)winner] >= Tuning.GamesPerSet && Lead( Games, winner ) >= 2);

			// enter tiebreak?
			if ( !setWon && Games[0] == Tuning.GamesPerSet && Games[1] == Tuning.GamesPerSet )
			{
				InTiebreak = true;
				result.EnteredTiebreak = true;
			}

			if ( setWon )
			{
				result.SetWinner = winner;
				InTiebreak = false;
				Sets[(int)winner]++;
				SetHistory.Add( (Games[0], Games[1]) );
				Games[0] = Games[1] = 0;

				if ( Sets[(int)winner] >= SetsToWin )
				{
					MatchOver = true;
					Winner = winner;
					result.MatchWinner = winner;
				}
			}

			// Initiative alternates every game (in a tiebreak, tennis alternates
			// serve every two points; we keep the simpler per-game rule inside
			// tiebreaks too via the point-pair rule below).
			if ( !MatchOver && !InTiebreak )
				Initiative = Other( Initiative );

			// Changeover after odd total games within a set (tennis law).
			result.IsChangeover = !MatchOver && result.SetWinner is null
				&& ((Games[0] + Games[1]) % 2 == 1);
		}
		else if ( InTiebreak )
		{
			// Tiebreak initiative: first point by holder, then alternate every two points.
			int played = Points[0] + Points[1];
			if ( played % 2 == 1 ) Initiative = Other( Initiative );
		}

		result.Snapshot = Freeze();
		return result;
	}

	// ── Sports-grammar queries (the Umpire's brain) ───────────────

	public bool IsDeuce =>
		!InTiebreak && Points[0] >= 3 && Points[1] >= 3 && Points[0] == Points[1];

	public Side? AdvantageTo =>
		!InTiebreak && Points[0] >= 3 && Points[1] >= 3 && Math.Abs( Points[0] - Points[1] ) == 1
			? (Points[0] > Points[1] ? Side.A : Side.B)
			: null;

	/// <summary> Is the NEXT point a game point for someone? Which side, and is it a break point? </summary>
	public (Side side, bool isBreak)? GamePoint()
	{
		foreach ( Side s in new[] { Side.A, Side.B } )
		{
			bool gp = InTiebreak
				? Points[(int)s] >= Tuning.TiebreakTo - 1 && Lead( Points, s ) >= 1
				: Points[(int)s] >= Tuning.PointsPerGame - 1 && Lead( Points, s ) >= 1;
			if ( gp ) return (s, s != Initiative && !InTiebreak);
		}
		return null;
	}

	/// <summary> Would winning the current game win the set for this side? </summary>
	public bool IsSetGame( Side s ) =>
		InTiebreak || (Games[(int)s] >= Tuning.GamesPerSet - 1 && Lead( Games, s, offset: 1 ) >= 2);

	/// <summary> Would winning the current game win the match for this side? </summary>
	public bool IsMatchGame( Side s ) => IsSetGame( s ) && Sets[(int)s] == SetsToWin - 1;

	/// <summary>
	/// Full stakes for the next point — the Umpire and broadcast layer read this
	/// to call "Break point", "Set point", "Match point — Championship."
	/// </summary>
	public PointStakes NextPointStakes()
	{
		var gp = GamePoint();
		if ( gp is null ) return new PointStakes();
		var (side, isBreak) = gp.Value;
		return new PointStakes
		{
			GamePointFor  = side,
			IsBreakPoint  = isBreak,
			IsSetPoint    = IsSetGame( side ),
			IsMatchPoint  = IsMatchGame( side ),
		};
	}

	/// <summary> "Fifteen — love", "Deuce", "Advantage, Cole", "Six points to four" (tiebreak). </summary>
	public string CallScore( string nameA, string nameB )
	{
		if ( InTiebreak )
			return $"{PointsWord( Points[(int)Initiative] )} — {PointsWord( Points[(int)Other( Initiative )] )}, tiebreak";

		if ( IsDeuce ) return "Deuce";
		if ( AdvantageTo is Side adv ) return $"Advantage, {(adv == Side.A ? nameA : nameB)}";

		// Tennis convention: Initiative-holder's score called first.
		var first  = TennisWord( Points[(int)Initiative] );
		var second = TennisWord( Points[(int)Other( Initiative )] );
		return first == second ? $"{first} — all" : $"{first} — {second}";
	}

	// ── Helpers ───────────────────────────────────────────────────
	static int Lead( int[] arr, Side s, int offset = 0 ) => (arr[(int)s] + offset) - arr[(int)Other( s )];
	public static Side Other( Side s ) => s == Side.A ? Side.B : Side.A;
	static string TennisWord( int p ) => p switch { 0 => "Love", 1 => "Fifteen", 2 => "Thirty", _ => "Forty" };
	static string PointsWord( int p ) => p.ToString();

	public string Serialize() => System.Text.Json.JsonSerializer.Serialize( this );
	public static TennisScore Deserialize( string json ) =>
		System.Text.Json.JsonSerializer.Deserialize<TennisScore>( json ) ?? new TennisScore();
	TennisScore Freeze() => Deserialize( Serialize() );

	// ─────────────────────────────────────────────────────────────
	// The Design Document's core claim, executable: per-point edge p
	// compounds through this structure into match probability. Used by
	// broadcast win-probability and by beta KPI validation.
	// ─────────────────────────────────────────────────────────────
	public static double GameWinProbability( double p )
	{
		// Standard tennis game closed form (win by 2 from deuce as geometric series)
		double q = 1 - p;
		double pDeuceWin = (p * p) / (p * p + q * q);
		double win = Math.Pow( p, 4 ) * (1 + 4 * q + 10 * q * q)
		           + 20 * Math.Pow( p, 3 ) * Math.Pow( q, 3 ) * pDeuceWin;
		return win;
	}

	public static double SetWinProbability( double p ) => SetRec( p, 0, 0 );
	static double SetRec( double p, int a, int b )
	{
		if ( a >= Tuning.GamesPerSet && a - b >= 2 ) return 1;
		if ( b >= Tuning.GamesPerSet && b - a >= 2 ) return 0;
		if ( a == Tuning.GamesPerSet && b == Tuning.GamesPerSet ) return TiebreakWinProbability( p );
		double g = GameWinProbability( p );
		return g * SetRec( p, a + 1, b ) + (1 - g) * SetRec( p, a, b + 1 );
	}

	public static double TiebreakWinProbability( double p ) => RaceWinBy2( p, Tuning.TiebreakTo );

	public static double MatchWinProbability( double p, int setsToWin = 2 )
	{
		double s = SetWinProbability( p );
		return RaceNoDeuce( s, setsToWin );
	}

	static double RaceWinBy2( double p, int target )
	{
		// win-by-two race via DP with deuce closure
		double q = 1 - p;
		double deuce = (p * p) / (p * p + q * q);
		return RaceRec( p, 0, 0, target, deuce );
	}
	static double RaceRec( double p, int a, int b, int t, double deuce )
	{
		if ( a >= t && a - b >= 2 ) return 1;
		if ( b >= t && b - a >= 2 ) return 0;
		if ( a >= t - 1 && b >= t - 1 && a == b ) return deuce;
		return p * RaceRec( p, a + 1, b, t, deuce ) + (1 - p) * RaceRec( p, a, b + 1, t, deuce );
	}
	static double RaceNoDeuce( double s, int target )
	{
		double total = 0;
		for ( int losses = 0; losses < target; losses++ )
			total += Binomial( target - 1 + losses, losses ) * Math.Pow( s, target ) * Math.Pow( 1 - s, losses );
		return total;
	}
	static double Binomial( int n, int k )
	{
		double r = 1;
		for ( int i = 1; i <= k; i++ ) r = r * (n - k + i) / i;
		return r;
	}
}

public struct PointResult
{
	public TennisScore.Side PointWinner;
	public TennisScore.Side? GameWinner;
	public TennisScore.Side? SetWinner;
	public TennisScore.Side? MatchWinner;
	public bool WasBreakConversion;   // won the game on opponent's Initiative — "BREAK"
	public bool EnteredTiebreak;
	public bool IsChangeover;         // odd-game changeover follows
	public TennisScore Snapshot;
}

public struct PointStakes
{
	public TennisScore.Side? GamePointFor;
	public bool IsBreakPoint;
	public bool IsSetPoint;
	public bool IsMatchPoint;
	public bool Any => GamePointFor.HasValue;
}
