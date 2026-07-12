using Sandbox;
using Sandbox.Network;
using System;
using System.Linq;

namespace TenPaces;

/// <summary>
/// Connection & seating for a duel session. The topology (beta):
///   • s&box lobby, host-authoritative. First two ready players take the seats;
///     everyone else is a spectator (local crowd — a design feature, not a
///     limitation: "the Sheriff is defending tonight" is watched IN the client).
///   • Callout links: s&box lobby join URIs ARE the callout's shareable card —
///     "accept this callout" is literally the install/join flow.
///   • Dedicated servers (ranked, latency-refereed) are the post-beta upgrade;
///     nothing here assumes host trust beyond what Revolver's host validation
///     already enforces.
/// </summary>
public sealed class LobbySystem : Component, Component.INetworkListener
{
	[Property] public GameObject DuelistPrefab { get; set; }
	[Property] public bool QuickFormat { get; set; } = false;

	[Sync] public Guid ReadyA { get; private set; }
	[Sync] public Guid ReadyB { get; private set; }

	protected override async void OnStart()
	{
		if ( !Networking.IsHost ) return;
		if ( !Networking.IsActive )
		{
			// Open the lobby — the callout is live.
			Networking.CreateLobby( new LobbyConfig
			{
				MaxPlayers = 8,           // 2 duelists + local crowd
				Privacy = LobbyPrivacy.FriendsOnly,
				Name = "Ten Paces — Callout",
			} );
		}
		await Task.Frame();
	}

	// INetworkListener — someone answered the callout (or came to watch)
	public void OnActive( Connection conn )
	{
		if ( !Networking.IsHost ) return;

		var player = DuelistPrefab?.Clone( new Transform( WorldPosition + Vector3.Up * 8f ) )
			?? new GameObject( true, $"duelist_{conn.DisplayName}" );
		if ( player.GetComponent<DuelistController>() is null )
			player.AddComponent<DuelistController>();
		player.NetworkSpawn( conn );

		Umpire.Instance?.Say( $"{conn.DisplayName} has entered the grounds.", UmpireTone.Neutral );
	}

	public void OnDisconnected( Connection conn )
	{
		if ( !Networking.IsHost ) return;
		// A seated duelist leaving mid-match forfeits — the Record is honest about walkovers.
		var director = MatchDirector.Instance;
		if ( director is null || director.Phase == MatchPhase.Lobby ) return;

		foreach ( var side in new[] { TennisScore.Side.A, TennisScore.Side.B } )
		{
			var d = director.GetDuelist( side );
			if ( d is not null && d.Network.Owner == conn )
			{
				Umpire.Instance?.Say( $"{conn.DisplayName} has left the grounds. Walkover.", UmpireTone.Grave );
				// End the match through the normal MatchEnd → Lobby path (also records the
				// forfeit + flushes telemetry). Without this the Director keeps running in a
				// phase with a null duelist and the session strands.
				director.ForfeitMatch( TennisScore.Other( side ) );
			}
		}
	}

	protected override void OnFixedUpdate()
	{
		var director = MatchDirector.Instance;
		if ( director is null || director.Phase != MatchPhase.Lobby ) return;

		// Ready-up: any duelist presses Ready; first two claim the seats.
		if ( !IsProxy && Input.Pressed( "Ready" ) )
			RequestReady();

		if ( Networking.IsHost )
		{
			// Self-heal stale ready seats: a readied player who disconnects leaves a Guid
			// that no longer resolves, which would otherwise block every future start.
			if ( ReadyA != Guid.Empty && Scene.Directory.FindByGuid( ReadyA ) is null ) ReadyA = Guid.Empty;
			if ( ReadyB != Guid.Empty && Scene.Directory.FindByGuid( ReadyB ) is null ) ReadyB = Guid.Empty;
		}

		if ( Networking.IsHost && ReadyA != Guid.Empty && ReadyB != Guid.Empty )
		{
			var a = Scene.Directory.FindByGuid( ReadyA );
			var b = Scene.Directory.FindByGuid( ReadyB );
			if ( a is not null && b is not null )
			{
				director.QuickFormat = QuickFormat;
				director.SeatAndStart( a, b );
				ReadyA = Guid.Empty; ReadyB = Guid.Empty;
			}
		}
	}

	[Rpc.Host]
	void RequestReady()
	{
		var caller = Rpc.Caller;
		var body = Scene.GetAllComponents<DuelistController>()
			.FirstOrDefault( d => d.Network.Owner == caller );
		if ( body is null ) return;

		var id = body.GameObject.Id;
		if ( ReadyA == id || ReadyB == id ) return;
		if ( ReadyA == Guid.Empty ) { ReadyA = id; Umpire.Instance?.Say( $"{caller.DisplayName} stands ready.", UmpireTone.Neutral ); }
		else if ( ReadyB == Guid.Empty ) { ReadyB = id; Umpire.Instance?.Say( $"{caller.DisplayName} answers.", UmpireTone.Neutral ); }
	}
}

/// <summary>
/// Spectator camera for the local crowd: cycles duelist POVs and a director
/// overhead. The tell-cam (broadcast layer) subscribes to ILedgerEvents to
/// mark information moments; beta ships POV-cycling + overhead.
/// </summary>
public sealed class SpectatorCamera : Component
{
	int _mode; // 0 = overhead, 1 = duelist A POV, 2 = duelist B POV

	protected override void OnUpdate()
	{
		var director = MatchDirector.Instance;
		if ( director is null ) return;

		// Only drive the camera if the local player is NOT a seated duelist.
		var seatA = director.GetDuelist( TennisScore.Side.A );
		var seatB = director.GetDuelist( TennisScore.Side.B );
		bool seated = (seatA?.IsLocalControlled ?? false) || (seatB?.IsLocalControlled ?? false);
		if ( seated ) return;

		if ( Input.Pressed( "Jump" ) ) _mode = (_mode + 1) % 3;

		var cam = Scene.Camera;
		if ( cam is null ) return;

		switch ( _mode )
		{
			case 1 when seatA is not null:
				cam.WorldPosition = seatA.EyePosition; cam.WorldRotation = seatA.EyeRotation; break;
			case 2 when seatB is not null:
				cam.WorldPosition = seatB.EyePosition; cam.WorldRotation = seatB.EyeRotation; break;
			default:
				var center = director.Ground?.ResolveVolumeCenter?.WorldPosition ?? Vector3.Zero;
				cam.WorldPosition = center + Vector3.Up * 900f + Vector3.Backward * 300f;
				cam.WorldRotation = Rotation.LookAt( center - cam.WorldPosition );
				break;
		}
	}
}
