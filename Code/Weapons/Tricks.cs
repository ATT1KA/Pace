using Sandbox;

namespace TenPaces;

/// <summary>
/// The Trick slot — Design Doc §IV: exactly one single-use tool per point.
/// Tricks deepen the INFORMATION game, never replace gunplay:
///   • Coin  — thrown sound decoy: manufactured false information
///   • Vial  — brief small smoke: a curtain for one repositioning
///   • Knife — silent short-range throw: lethal ONLY on a perfect hit
///
/// All physical, all honest (the coin's landing sound is a real ledger entry
/// at the coin's real position — the lie is in the inference, not the data).
/// </summary>
public static class Tricks
{
	/// <summary>
	/// HOST-AUTHORITATIVE. Called from Revolver's [Rpc.Host] path, so this always runs on
	/// the host: it spawns the trick object, NetworkSpawns it so every client renders it,
	/// simulates its flight, and resolves the knife's damage — all on the host. Nothing is
	/// spawned client-locally any more (which is why a non-host knife dealt no damage and a
	/// vial's smoke was invisible to the opponent).
	/// </summary>
	public static void Execute( TrickId id, DuelistController user )
	{
		if ( !Networking.IsHost ) return;
		switch ( id )
		{
			case TrickId.Coin:  ThrowCoin( user );  break;
			case TrickId.Vial:  ThrowVial( user );  break;
			case TrickId.Knife: ThrowKnife( user ); break;
		}
		FeelTelemetry.Instance?.OnTrickUsed( user, id );
	}

	static void ThrowCoin( DuelistController user )
	{
		var coin = new GameObject( true, "trick_coin" );
		coin.WorldPosition = user.EyePosition + user.EyeRotation.Forward * Tuning.TrickThrowOffset;
		var rb = coin.AddComponent<Rigidbody>();
		var col = coin.AddComponent<SphereCollider>(); col.Radius = Tuning.TrickCoinRadius;
		rb.Velocity = user.EyeRotation.Forward * Tuning.CoinThrowSpeed + Vector3.Up * Tuning.CoinThrowLift;
		coin.AddComponent<CoinLanding>();
		coin.NetworkSpawn();
	}

	sealed class CoinLanding : Component, Component.ICollisionListener
	{
		bool _reported;
		public void OnCollisionStart( Collision c )
		{
			if ( !Networking.IsHost || _reported ) return; // host owns the landing position
			_reported = true;
			// A footstep-class report AT THE COIN — real sound (broadcast), false inference.
			SoundLedger.ReportAt( WorldPosition, LedgerSound.CoinLand, Tuning.CoinSoundRadius );
			GameObject.DestroyAsync( Tuning.CoinLandDestroyDelay );
		}
	}

	static void ThrowVial( DuelistController user )
	{
		var vial = new GameObject( true, "trick_vial" );
		vial.WorldPosition = user.EyePosition + user.EyeRotation.Forward * Tuning.TrickThrowOffset;
		var rb = vial.AddComponent<Rigidbody>();
		var col = vial.AddComponent<SphereCollider>(); col.Radius = Tuning.TrickVialRadius;
		rb.Velocity = user.EyeRotation.Forward * Tuning.VialThrowSpeed + Vector3.Up * Tuning.VialThrowLift;
		vial.AddComponent<VialBreak>();
		vial.NetworkSpawn();
		SoundLedger.Report( user, LedgerSound.VialThrow, Tuning.RadiusWalk );
	}

	sealed class VialBreak : Component, Component.ICollisionListener
	{
		bool _broken;
		public void OnCollisionStart( Collision c )
		{
			if ( !Networking.IsHost || _broken ) return;
			_broken = true;
			SoundLedger.ReportAt( WorldPosition, LedgerSound.VialBreak, Tuning.RadiusWalk );
			// Smoke volume: brief, small, genuinely occluding (a curtain for ONE move).
			// NetworkSpawned so the "smoke" gameplay volume exists on EVERY client — the
			// whole point of the trick is blocking the OPPONENT's view.
			var smoke = new GameObject( true, "vial_smoke" );
			smoke.WorldPosition = WorldPosition;
			smoke.Tags.Add( "smoke" );
			var vol = smoke.AddComponent<SphereCollider>();
			vol.Radius = Tuning.VialSmokeRadius;
			vol.IsTrigger = true;
			smoke.AddComponent<SmokeCloud>(); // posts the presentation event on every client it spawns on
			smoke.NetworkSpawn();
			smoke.DestroyAsync( Tuning.VialSmokeLife );
			GameObject.Destroy();
		}
	}

	// Rides the NetworkSpawned smoke object so the OnSmoke presentation fires on EVERY
	// client the smoke exists on (particles/occlusion hook) — not just the thrower's.
	sealed class SmokeCloud : Component
	{
		protected override void OnStart() =>
			ISceneEvent<ITrickEvents>.Post( x => x.OnSmoke( WorldPosition, Tuning.VialSmokeRadius, Tuning.VialSmokeLife ) );
	}

	static void ThrowKnife( DuelistController user )
	{
		SoundLedger.Report( user, LedgerSound.KnifeThrow, Tuning.RadiusSoftStep ); // near-SILENT — that's the point
		var knife = new GameObject( true, "trick_knife" );
		knife.WorldPosition = user.EyePosition + user.EyeRotation.Forward * Tuning.TrickKnifeOffset;
		knife.WorldRotation = user.EyeRotation;
		var proj = knife.AddComponent<KnifeProjectile>();
		proj.Owner = user;
		proj.Velocity = user.EyeRotation.Forward * Tuning.KnifeSpeed;
		knife.NetworkSpawn();
	}

	sealed class KnifeProjectile : Component
	{
		public DuelistController Owner;
		public Vector3 Velocity;
		TimeSince _alive = 0;

		protected override void OnFixedUpdate()
		{
			// Host owns the flight AND the damage; clients just see the synced transform.
			if ( !Networking.IsHost ) return;
			if ( _alive > Tuning.KnifeLifetime ) { GameObject.Destroy(); return; }
			var from = WorldPosition;
			var to = from + Velocity * Time.Delta + Vector3.Down * Tuning.KnifeGravity * Time.Delta * _alive; // gentle arc
			var tr = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( Owner.GameObject ).Run();
			if ( tr.Hit )
			{
				var victim = tr.GameObject?.Components.GetInParentOrSelf<DuelistController>();
				if ( victim is not null && victim != Owner )
					victim.Vitals.ResolveBulletHit( tr.HitPosition, Owner, wasKnife: true );
				SoundLedger.ReportAt( tr.HitPosition, LedgerSound.KnifeImpact, Tuning.RadiusSoftStep * 2f );
				GameObject.Destroy();
				return;
			}
			WorldPosition = to;
		}
	}
}

public interface ITrickEvents : ISceneEvent<ITrickEvents>
{
	void OnSmoke( Vector3 pos, float radius, float life ) { }
}
