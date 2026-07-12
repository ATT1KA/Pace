using Sandbox;

namespace TenPaces;

/// <summary>
/// The Umpire is the match's audio anchor and the game's narrator of stakes —
/// a period-voiced official whose calls are generated structurally from
/// TennisScore, never hardcoded per situation. "Fifteen — love." "Deuce."
/// "Break point." "Match point — for the championship."
///
/// Beta ships with text calls + placeholder VO hooks; every call routes through
/// Say() so the voice bank drops in without code changes. The Umpire is also
/// deliberately part of the INFORMATION game: calls are public and identical
/// for both players — the Umpire never reveals anything positional.
/// </summary>
public sealed class Umpire : Component
{
	public static Umpire Instance { get; private set; }

	[Property] public SoundEvent BellToll { get; set; }
	[Property] public SoundEvent CrowdSting { get; set; }

	protected override void OnAwake() => Instance = this;
	protected override void OnDestroy() { if ( Instance == this ) Instance = null; }

	// ── Core speech path ──────────────────────────────────────────
	[Rpc.Broadcast]
	public void Say( string line, UmpireTone tone = UmpireTone.Neutral )
	{
		// Host writes the synced mirror so late-joiners see the last call (the live HUD
		// updates from the event below). Was a discarded reflection lookup — a no-op.
		MatchDirector.Instance?.SetLastCall( line );

		ISceneEvent<IUmpireEvents>.Post( x => x.OnUmpireCall( line, tone ) );
		// VO hook: Sound.Play( VoiceBank.Resolve( line ) ) once the voice bank exists.
		Log.Info( $"[UMPIRE] {line}" );
	}

	[Rpc.Broadcast]
	public void Bell()
	{
		if ( BellToll is not null ) Sound.Play( BellToll );
		ISceneEvent<IUmpireEvents>.Post( x => x.OnBell() );
	}

	// ── Match narration ───────────────────────────────────────────

	public void AnnounceMatchStart( string a, string b, bool bestOfFive )
	{
		Say( $"{a} versus {b}. Best of {(bestOfFive ? "five" : "three")} sets. Duelists to your marks.", UmpireTone.Ceremonial );
	}

	public void CallCoinFlip( string winnerName )
	{
		Say( $"The coin favors {winnerName}. {winnerName} holds Initiative.", UmpireTone.Ceremonial );
	}

	/// <summary> Called before each point releases — the stakes, in sports grammar. </summary>
	public void CallPointStakes( PointStakes stakes, System.Func<TennisScore.Side, string> nameOf )
	{
		if ( !stakes.Any ) return;
		var side = stakes.GamePointFor.Value;
		string call =
			stakes.IsMatchPoint ? $"Match point, {nameOf( side )}." :
			stakes.IsSetPoint   ? $"Set point, {nameOf( side )}." :
			stakes.IsBreakPoint ? $"Break point, {nameOf( side )}." :
			                      $"Game point, {nameOf( side )}.";
		Say( call, stakes.IsMatchPoint ? UmpireTone.Grave : UmpireTone.Neutral );
	}

	/// <summary> The full post-point sequence: bell → score/game/set/match calls. </summary>
	public void CallAfterPoint( PointResult result, TennisScore score, System.Func<TennisScore.Side, string> nameOf )
	{
		Bell();

		if ( result.MatchWinner is TennisScore.Side mw )
		{
			Say( $"Game, set, and match — {nameOf( mw )}. " +
			     $"{score.Sets[(int)mw]} sets to {score.Sets[(int)TennisScore.Other( mw )]}.", UmpireTone.Ceremonial );
			return;
		}

		if ( result.SetWinner is TennisScore.Side sw )
		{
			var last = score.SetHistory[^1];
			Say( $"Game and set, {nameOf( sw )}. {System.Math.Max( last.a, last.b )} games to {System.Math.Min( last.a, last.b )}.", UmpireTone.Ceremonial );
			return;
		}

		if ( result.GameWinner is TennisScore.Side gw )
		{
			string breakTag = result.WasBreakConversion ? " — the break" : "";
			Say( $"Game, {nameOf( gw )}{breakTag}. Games {score.Games[0]}–{score.Games[1]}.", 
				result.WasBreakConversion ? UmpireTone.Grave : UmpireTone.Neutral );
			if ( result.EnteredTiebreak ) Say( "Six games all. Tiebreak.", UmpireTone.Grave );
			if ( result.IsChangeover )    Say( "Changeover.", UmpireTone.Neutral );
			return;
		}

		Say( score.CallScore( nameOf( TennisScore.Side.A ), nameOf( TennisScore.Side.B ) ), UmpireTone.Neutral );
	}

	public void ReckoningWarning() => Say( "Fifteen seconds. Settle it, or the sun will.", UmpireTone.Grave );
	public void CallReckoning()
	{
		Bell();
		Say( "The Reckoning. To the center.", UmpireTone.Grave );
	}
}

public enum UmpireTone { Neutral, Ceremonial, Grave }

public interface IUmpireEvents : ISceneEvent<IUmpireEvents>
{
	void OnUmpireCall( string line, UmpireTone tone ) { }
	void OnBell() { }
}
