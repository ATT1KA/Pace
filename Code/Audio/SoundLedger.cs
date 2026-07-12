using Sandbox;
using System;

namespace TenPaces;

public enum LedgerSound
{
	FootstepRun, FootstepWalk, FootstepSoft, BootScuff,
	Jump, LandHeavy, Slide, Mantle,
	Draw, Holster, Gunshot, HammerClick, DeadClick,
	GateOpen, ShellEject, ShellInsert, ShellDropped, GateClose,
	WhipSwing, WhipConnect,
	CoinLand, VialThrow, VialBreak, KnifeThrow, KnifeImpact,
}

/// <summary>
/// THE HONEST LEDGER [LAW] — Feel Doc §VIII. Every gameplay sound in Ten Paces
/// routes through this single static gate. The covenant it enforces:
///
///   1. Every entry is networked. No client-local gameplay sounds exist —
///      what you hear, your opponent's simulation also emitted.
///   2. Nothing is suppressed, nothing is faked. The Coin's deception is real
///      audio at a real position; the lie is the listener's inference.
///   3. Radius is data (Tuning), attenuation and occlusion are the audio
///      engine's (Steam Audio via .sound assets) — the ledger decides WHO
///      could hear; physics decides HOW it sounds.
///   4. Every entry also posts a scene event: the Tell-Cam, the DrillBot's
///      "ears", the Honesty Auditor, and FeelTelemetry all subscribe to the
///      same stream the players hear. One reality, many listeners.
///
/// Sounds are keyed "tp.{sound}.{surface?}" so the asset bank fills in without
/// code changes; missing assets fail silent-with-log during greybox.
/// </summary>
public static class SoundLedger
{
	public static void Report( DuelistController source, LedgerSound sound, float radius, string overrideEvent = null )
	{
		if ( source is null ) return;
		Broadcaster.Instance?.Emit( source.GameObject.Id, source.WorldPosition, sound, radius, overrideEvent );
	}

	public static void ReportAt( Vector3 position, LedgerSound sound, float radius )
	{
		Broadcaster.Instance?.Emit( Guid.Empty, position, sound, radius, null );
	}

	/// <summary> Scene singleton doing the actual RPC fan-out. Drop one in every scene. </summary>
	public sealed class Broadcaster : Component
	{
		public static Broadcaster Instance { get; private set; }
		protected override void OnAwake() => Instance = this;

		public void Emit( Guid sourceId, Vector3 pos, LedgerSound sound, float radius, string overrideEvent )
		{
			// Emit locally + to everyone. (Optimization for later: distance-cull
			// per-connection server-side; for a duel, blanket broadcast is fine
			// and keeps the ledger trivially honest.)
			BroadcastEntry( sourceId, pos, (int)sound, radius, overrideEvent ?? "" );
		}

		[Rpc.Broadcast]
		void BroadcastEntry( Guid sourceId, Vector3 pos, int soundInt, float radius, string overrideEvent )
		{
			var sound = (LedgerSound)soundInt;

			// Post to all non-audio listeners (telemetry, bot ears, tell-cam, auditor)
			ISceneEvent<ILedgerEvents>.Post( x => x.OnLedgerSound( sourceId, pos, sound, radius ) );

			// Audibility gate for THIS client: is my listener within radius?
			var cam = Game.ActiveScene?.Camera;
			if ( cam is null ) return;
			if ( cam.WorldPosition.Distance( pos ) > radius ) return;

			string ev = string.IsNullOrEmpty( overrideEvent ) ? EventName( sound ) : overrideEvent;
			var handle = Sound.Play( ev, pos );
			if ( handle is null )
				Log.Info( $"[LEDGER] (no asset) {ev} @ {pos} r={radius}" ); // greybox visibility
		}

		static string EventName( LedgerSound s ) => s switch
		{
			LedgerSound.FootstepRun  => "tp.footstep.run",
			LedgerSound.FootstepWalk => "tp.footstep.walk",
			LedgerSound.FootstepSoft => "tp.footstep.soft",
			LedgerSound.BootScuff    => "tp.bootscuff",
			LedgerSound.Jump         => "tp.jump",
			LedgerSound.LandHeavy    => "tp.land_heavy",
			LedgerSound.Slide        => "tp.slide",
			LedgerSound.Mantle       => "tp.mantle",
			LedgerSound.Draw         => "tp.draw_duelist",
			LedgerSound.Holster      => "tp.holster",
			LedgerSound.Gunshot      => "tp.gunshot",
			LedgerSound.HammerClick  => "tp.hammer_click",
			LedgerSound.DeadClick    => "tp.dead_click",
			LedgerSound.GateOpen     => "tp.gate_open",
			LedgerSound.ShellEject   => "tp.shell_eject",
			LedgerSound.ShellInsert  => "tp.shell_insert",
			LedgerSound.ShellDropped => "tp.shell_dropped",
			LedgerSound.GateClose    => "tp.gate_close",
			LedgerSound.WhipSwing    => "tp.whip_swing",
			LedgerSound.WhipConnect  => "tp.whip_connect",
			LedgerSound.CoinLand     => "tp.coin_land",
			LedgerSound.VialThrow    => "tp.vial_throw",
			LedgerSound.VialBreak    => "tp.vial_break",
			LedgerSound.KnifeThrow   => "tp.knife_throw",
			LedgerSound.KnifeImpact  => "tp.knife_impact",
			_ => "tp.generic",
		};
	}
}

public interface ILedgerEvents : ISceneEvent<ILedgerEvents>
{
	void OnLedgerSound( Guid sourceId, Vector3 pos, LedgerSound sound, float radius ) { }
}
