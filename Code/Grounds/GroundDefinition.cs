using Sandbox;
using System.Collections.Generic;
using System.Linq;

namespace TenPaces;

public enum GroundHour { HighNoon, Dusk }

/// <summary>
/// A Ground's gameplay contract, placed once per arena scene:
///   • symmetric spawn gates per side (3 each — the three-route grammar)
///   • the two Hour lighting states (baked light sets toggled, never dynamic)
///   • the Reckoning resolve volume at the arena's heart
///   • the ambient masking schedule (the train, the wind, the piano —
///     exploitable, deterministic sound-cover events)
///
/// Grounds ship as Hammer .vmap geometry + one of these in the scene wiring
/// gates and volumes. The charter (Design Doc §V) is enforced at review, not
/// in code: small, symmetric, three routes, one contested heart, honest tells.
/// </summary>
public sealed class GroundDefinition : Component
{
	[Property] public string GroundName { get; set; } = "Main Street";
	[Property] public List<GameObject> GatesSideA { get; set; } = new();
	[Property] public List<GameObject> GatesSideB { get; set; } = new();
	[Property] public GameObject ResolveVolumeCenter { get; set; }
	[Property] public float ResolveRadius { get; set; } = 350f;
	[Property] public GameObject HighNoonLightRig { get; set; }
	[Property] public GameObject DuskLightRig { get; set; }

	[Property, Group( "Ambient Masking" )] public float MaskEventPeriod { get; set; } = 22f;   // the train's schedule
	[Property, Group( "Ambient Masking" )] public float MaskEventDuration { get; set; } = 4f;
	[Property, Group( "Ambient Masking" )] public string MaskSoundEvent { get; set; } = "tp.ambient.mask";

	[Sync] public GroundHour CurrentHour { get; private set; } = GroundHour.HighNoon;
	[Sync] public bool ReckoningActive { get; private set; }

	public int GateCount => System.Math.Max( 1, System.Math.Min( GatesSideA.Count, GatesSideB.Count ) );

	TimeSince _maskClock = 0;
	bool _maskActive;

	public Transform GetGate( TennisScore.Side side, int index )
	{
		var list = side == TennisScore.Side.A ? GatesSideA : GatesSideB;
		if ( list.Count == 0 ) return new Transform( WorldPosition + (side == TennisScore.Side.A ? Vector3.Forward : Vector3.Backward) * 400f );
		var go = list[index.Clamp( 0, list.Count - 1 )];
		return go.WorldTransform;
	}

	public void ApplyHour( GroundHour hour )
	{
		CurrentHour = hour;
		ReckoningActive = false;
		ApplyHourRpc( (int)hour );
	}

	[Rpc.Broadcast]
	void ApplyHourRpc( int hour )
	{
		var h = (GroundHour)hour;
		if ( HighNoonLightRig is not null ) HighNoonLightRig.Enabled = h == GroundHour.HighNoon;
		if ( DuskLightRig is not null )     DuskLightRig.Enabled     = h == GroundHour.Dusk;
		ISceneEvent<IGroundEvents>.Post( x => x.OnHourApplied( h ) );
	}

	public void BeginReckoning()
	{
		ReckoningActive = true;
		BeginReckoningRpc();
	}

	[Rpc.Broadcast]
	void BeginReckoningRpc()
	{
		// Map-driven cover recession hooks fire here (doors bar, crates sink —
		// each Ground implements its own IGroundEvents listener for theater).
		ISceneEvent<IGroundEvents>.Post( x => x.OnReckoningBegan() );
	}

	public bool InResolveVolume( Vector3 pos )
	{
		var center = ResolveVolumeCenter?.WorldPosition ?? WorldPosition;
		return pos.WithZ( center.z ).Distance( center ) <= ResolveRadius;
	}

	// ── Deterministic ambient masking (host-clocked, honest) ──────
	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost ) return;
		if ( MatchDirector.Instance is { Phase: not MatchPhase.Live } ) return;

		if ( !_maskActive && _maskClock >= MaskEventPeriod )
		{
			_maskActive = true;
			_maskClock = 0;
			MaskRpc( true );
		}
		else if ( _maskActive && _maskClock >= MaskEventDuration )
		{
			_maskActive = false;
			_maskClock = 0;
			MaskRpc( false );
		}
	}

	[Rpc.Broadcast]
	void MaskRpc( bool start )
	{
		if ( start ) Sound.Play( MaskSoundEvent, ResolveVolumeCenter?.WorldPosition ?? WorldPosition );
		ISceneEvent<IGroundEvents>.Post( x => x.OnMaskEvent( start ) );
		// The DrillBot and elite humans alike learn: move when the train passes.
	}
}

public interface IGroundEvents : ISceneEvent<IGroundEvents>
{
	void OnHourApplied( GroundHour hour ) { }
	void OnReckoningBegan() { }
	void OnMaskEvent( bool started ) { }
}
