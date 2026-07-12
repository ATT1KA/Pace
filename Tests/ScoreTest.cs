using System;
using TenPaces;

class ScoreTest
{
	static int fails = 0;
	static void Assert( bool cond, string name )
	{
		if ( cond ) Console.WriteLine( $"  PASS  {name}" );
		else { Console.WriteLine( $"  FAIL  {name}" ); fails++; }
	}

	static void Main()
	{
		Console.WriteLine( "== RULES CORRECTNESS ==" );

		// 1. Clean game: 4 straight points wins a game
		var s = new TennisScore( 2, TennisScore.Side.A );
		PointResult r = default(PointResult);
		for ( int i = 0; i < 4; i++ ) r = s.AwardPoint( TennisScore.Side.A );
		Assert( r.GameWinner.HasValue && r.GameWinner.Value == TennisScore.Side.A && s.Games[0] == 1, "4-0 wins the game" );
		Assert( s.Initiative == TennisScore.Side.B, "Initiative alternates after game" );
		Assert( r.IsChangeover, "changeover after game 1 (odd)" );

		// 2. Deuce mechanics: 3-3 is deuce; must win by two
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int i = 0; i < 3; i++ ) { s.AwardPoint( TennisScore.Side.A ); s.AwardPoint( TennisScore.Side.B ); }
		Assert( s.IsDeuce, "3-3 is deuce" );
		s.AwardPoint( TennisScore.Side.A );
		Assert( s.AdvantageTo.HasValue && s.AdvantageTo.Value == TennisScore.Side.A, "advantage A" );
		s.AwardPoint( TennisScore.Side.B );
		Assert( s.IsDeuce, "back to deuce" );
		s.AwardPoint( TennisScore.Side.B );
		r = s.AwardPoint( TennisScore.Side.B );
		Assert( r.GameWinner.HasValue && r.GameWinner.Value == TennisScore.Side.B, "win by two from deuce" );

		// 3. Break detection: B wins a game while A holds Initiative
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int i = 0; i < 4; i++ ) r = s.AwardPoint( TennisScore.Side.B );
		Assert( r.WasBreakConversion, "break of Initiative detected" );

		// 4. Set: 6 games, win by two
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int g = 0; g < 6; g++ )
			for ( int p = 0; p < 4; p++ ) r = s.AwardPoint( TennisScore.Side.A );
		Assert( r.SetWinner.HasValue && r.SetWinner.Value == TennisScore.Side.A && s.Sets[0] == 1, "6-0 wins the set" );
		Assert( s.SetHistory.Count == 1 && s.SetHistory[0].a == 6, "set history recorded" );

		// 5. 6-6 enters tiebreak; tiebreak to 7 win-by-2 takes set
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int g = 0; g < 5; g++ )
		{
			WinGame( s, TennisScore.Side.A );
			WinGame( s, TennisScore.Side.B );
		}
		WinGame( s, TennisScore.Side.A );              // 6-5
		r = WinGame( s, TennisScore.Side.B );          // 6-6
		Assert( r.EnteredTiebreak && s.InTiebreak, "6-6 enters tiebreak" );
		for ( int i = 0; i < 7; i++ ) r = s.AwardPoint( TennisScore.Side.B );
		Assert( r.SetWinner.HasValue && r.SetWinner.Value == TennisScore.Side.B, "tiebreak 7-0 wins the set" );
		Assert( s.SetHistory[s.SetHistory.Count - 1].b == 7 || s.SetHistory[s.SetHistory.Count - 1].b == 6, "tiebreak set recorded" );

		// 6. Match: two sets wins best-of-three
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int set = 0; set < 2; set++ )
			for ( int g = 0; g < 6; g++ ) r = WinGame( s, TennisScore.Side.A );
		Assert( r.MatchWinner.HasValue && r.MatchWinner.Value == TennisScore.Side.A && s.MatchOver, "two sets takes the match" );
		bool threw = false;
		try { s.AwardPoint( TennisScore.Side.A ); } catch { threw = true; }
		Assert( threw, "no points after match over" );

		// 7. Stakes grammar: 40-0 on server is game point (not break); 0-40 is break point
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int i = 0; i < 3; i++ ) s.AwardPoint( TennisScore.Side.A );
		var st = s.NextPointStakes();
		Assert( st.GamePointFor.HasValue && st.GamePointFor.Value == TennisScore.Side.A && !st.IsBreakPoint, "40-0 game point, not break" );
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int i = 0; i < 3; i++ ) s.AwardPoint( TennisScore.Side.B );
		st = s.NextPointStakes();
		Assert( st.GamePointFor.HasValue && st.GamePointFor.Value == TennisScore.Side.B && st.IsBreakPoint, "0-40 is break point" );

		// 8. Match point detection at 5-x games, 1 set up, 40-x
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int g = 0; g < 6; g++ ) WinGame( s, TennisScore.Side.A );          // set 1
		for ( int g = 0; g < 5; g++ ) WinGame( s, TennisScore.Side.A );          // 5-0 in set 2
		for ( int i = 0; i < 3; i++ ) s.AwardPoint( TennisScore.Side.A );        // 40-0
		st = s.NextPointStakes();
		Assert( st.IsMatchPoint, "match point detected" );

		// 9. Score calling
		s = new TennisScore( 2, TennisScore.Side.A );
		s.AwardPoint( TennisScore.Side.A );
		Assert( s.CallScore( "Cole", "Reyes" ) == "Fifteen — Love", "call check" );
		s.AwardPoint( TennisScore.Side.B );
		Assert( s.CallScore( "Cole", "Reyes" ) == "Fifteen — all", "call check" );

		// 10. Serialization round-trip preserves set history AND live state.
		//     This is the guard for the ValueTuple-drops-set-history bug: a tuple list
		//     round-trips as [(0,0)], a SetScore list survives.
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int g = 0; g < 6; g++ ) WinGame( s, TennisScore.Side.A );                                       // set 1: 6-0
		for ( int g = 0; g < 3; g++ ) { WinGame( s, TennisScore.Side.A ); WinGame( s, TennisScore.Side.B ); } // set 2: 3-3
		var round = TennisScore.Deserialize( s.Serialize() );
		Assert( round.SetHistory.Count == 1 && round.SetHistory[0].a == 6 && round.SetHistory[0].b == 0, "set history survives serialization" );
		Assert( round.Games[0] == 3 && round.Games[1] == 3 && round.Sets[0] == 1, "live game/set state survives serialization" );

		// 11. Best-of-five (SetsToWin=3) needs three sets, not two.
		s = new TennisScore( 3, TennisScore.Side.A );
		for ( int set = 0; set < 2; set++ )
			for ( int g = 0; g < 6; g++ ) r = WinGame( s, TennisScore.Side.A );
		Assert( !s.MatchOver, "two sets does not win best-of-five" );
		for ( int g = 0; g < 6; g++ ) r = WinGame( s, TennisScore.Side.A );
		Assert( r.MatchWinner.HasValue && s.MatchOver, "three sets wins best-of-five" );

		// 12. Deuce / Advantage score calling.
		s = new TennisScore( 2, TennisScore.Side.A );
		for ( int i = 0; i < 3; i++ ) { s.AwardPoint( TennisScore.Side.A ); s.AwardPoint( TennisScore.Side.B ); }
		Assert( s.CallScore( "Cole", "Reyes" ) == "Deuce", "deuce called" );
		s.AwardPoint( TennisScore.Side.A );
		Assert( s.CallScore( "Cole", "Reyes" ) == "Advantage, Cole", "advantage called" );

		// 13. From deuce, the receiver's game point is a BREAK point.
		s = new TennisScore( 2, TennisScore.Side.A );   // A holds Initiative
		for ( int i = 0; i < 3; i++ ) { s.AwardPoint( TennisScore.Side.A ); s.AwardPoint( TennisScore.Side.B ); }
		s.AwardPoint( TennisScore.Side.B );             // advantage B (the receiver)
		st = s.NextPointStakes();
		Assert( st.GamePointFor.HasValue && st.GamePointFor.Value == TennisScore.Side.B && st.IsBreakPoint, "advantage-receiver is a break point" );

		// 14. Entering the tiebreak passes first serve to the side that didn't serve game 12.
		s = new TennisScore( 2, TennisScore.Side.A );   // A serves game 1 (odd games A, even games B)
		for ( int g = 0; g < 5; g++ ) { WinGame( s, TennisScore.Side.A ); WinGame( s, TennisScore.Side.B ); }
		WinGame( s, TennisScore.Side.A );               // 6-5, A served game 11
		r = WinGame( s, TennisScore.Side.B );           // 6-6, B served game 12 → tiebreak
		Assert( r.EnteredTiebreak && s.Initiative == TennisScore.Side.A, "tiebreak opens with the non-server of game 12" );

		Console.WriteLine( "\n== MONTE CARLO vs CLOSED FORM (100,000 matches per p) ==" );
		Console.WriteLine( "  The Design Document's central wager, verified:\n" );
		Console.WriteLine( "   p/point | game(cf) | set(cf) | match(cf) | match(sim) " );
		var rng = new Random( 1848 );
		foreach ( double p in new[] { 0.50, 0.53, 0.55, 0.60, 0.65, 0.75 } )
		{
			double g = TennisScore.GameWinProbability( p );
			double set = TennisScore.SetWinProbability( p );
			double match = TennisScore.MatchWinProbability( p );
			int wins = 0, N = 100000;
			for ( int i = 0; i < N; i++ ) if ( SimMatch( p, rng ) ) wins++;
			double sim = (double)wins / N;
			Console.WriteLine( $"    {p:F2}   |  {g:F3}   |  {set:F3}  |   {match:F3}   |   {sim:F3}" );
			Assert( Math.Abs( sim - match ) < 0.012, $"closed form matches simulation at p={p:F2}" );
		}

		// The dual mandate, as numbers:
		double fair = TennisScore.MatchWinProbability( 0.50 );
		double elite = TennisScore.MatchWinProbability( Tuning.TargetElitePerPointWinrate );
		Assert( Math.Abs( fair - 0.5 ) < 1e-9, $"average vs average = {fair:F4} (the 50-50 guarantee)" );
		Assert( elite > 0.999, $"elite vs novice (p=.75) = {elite:F6} (the near-certainty guarantee)" );
		double adjacent = TennisScore.MatchWinProbability( Tuning.TargetAdjacentPerPointWinrate );
		Console.WriteLine( $"\n  adjacent-percentile (p=.535) match winrate: {adjacent:F3} — skill visible, upsets alive" );

		Console.WriteLine( fails == 0 ? "\nALL TESTS PASSED" : $"\n{fails} FAILURES" );
		Environment.Exit( fails == 0 ? 0 : 1 );
	}

	static PointResult WinGame( TennisScore s, TennisScore.Side side )
	{
		PointResult r = default(PointResult);
		while ( true )
		{
			r = s.AwardPoint( side );
			if ( r.GameWinner != null ) return r;
		}
	}

	static bool SimMatch( double p, Random rng )
	{
		var s = new TennisScore( 2, TennisScore.Side.A );
		while ( !s.MatchOver )
			s.AwardPoint( rng.NextDouble() < p ? TennisScore.Side.A : TennisScore.Side.B );
		return s.Winner.HasValue && s.Winner.Value == TennisScore.Side.A;
	}
}
