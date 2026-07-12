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
	public static void Execute( TrickId id, DuelistController user )
	{
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
		coin.WorldPosition = user.EyePosition + user.EyeRotation.Forward * 20f;
		var rb = coin.AddComponent<Rigidbody>();
		var col = coin.AddComponent<SphereCollider>(); col.Radius = 1.5f;
		rb.Velocity = user.EyeRotation.Forward * Tuning.CoinThrowSpeed + Vector3.Up * 120f;
		coin.AddComponent<CoinLanding>();
	}

	sealed class CoinLanding : Component, Component.ICollisionListener
	{
		bool _reported;
		public void OnCollisionStart( Collision c )
		{
			if ( _reported ) return;
			_reported = true;
			// A footstep-class report AT THE COIN — real sound, false inference.
			SoundLedger.ReportAt( WorldPosition, LedgerSound.CoinLand, Tuning.CoinSoundRadius );
			GameObject.DestroyAsync( 4f );
		}
	}

	static void ThrowVial( DuelistController user )
	{
		var vial = new GameObject( true, "trick_vial" );
		vial.WorldPosition = user.EyePosition + user.EyeRotation.Forward * 20f;
		var rb = vial.AddComponent<Rigidbody>();
		var col = vial.AddComponent<SphereCollider>(); col.Radius = 2f;
		rb.Velocity = user.EyeRotation.Forward * 500f + Vector3.Up * 100f;
		vial.AddComponent<VialBreak>();
		SoundLedger.Report( user, LedgerSound.VialThrow, Tuning.RadiusWalk );
	}

	sealed class VialBreak : Component, Component.ICollisionListener
	{
		bool _broken;
		public void OnCollisionStart( Collision c )
		{
			if ( _broken ) return;
			_broken = true;
			SoundLedger.ReportAt( WorldPosition, LedgerSound.VialBreak, Tuning.RadiusWalk );
			// Smoke volume: brief, small, genuinely occluding (a curtain for ONE move).
			var smoke = new GameObject( true, "vial_smoke" );
			smoke.WorldPosition = WorldPosition;
			smoke.Tags.Add( "smoke" );
			var vol = smoke.AddComponent<SphereCollider>();
			vol.Radius = Tuning.VialSmokeRadius;
			vol.IsTrigger = true;
			ISceneEvent<ITrickEvents>.Post( x => x.OnSmoke( WorldPosition, Tuning.VialSmokeRadius, Tuning.VialSmokeLife ) );
			smoke.DestroyAsync( Tuning.VialSmokeLife );
			GameObject.Destroy();
		}
	}

	static void ThrowKnife( DuelistController user )
	{
		SoundLedger.Report( user, LedgerSound.KnifeThrow, Tuning.RadiusSoftStep ); // near-SILENT — that's the point
		var knife = new GameObject( true, "trick_knife" );
		knife.WorldPosition = user.EyePosition + user.EyeRotation.Forward * 24f;
		knife.WorldRotation = user.EyeRotation;
		var proj = knife.AddComponent<KnifeProjectile>();
		proj.Owner = user;
		proj.Velocity = user.EyeRotation.Forward * Tuning.KnifeSpeed;
	}

	sealed class KnifeProjectile : Component
	{
		public DuelistController Owner;
		public Vector3 Velocity;
		TimeSince _alive = 0;

		protected override void OnFixedUpdate()
		{
			if ( _alive > 3f ) { GameObject.Destroy(); return; }
			var from = WorldPosition;
			var to = from + Velocity * Time.Delta + Vector3.Down * 200f * Time.Delta * _alive; // gentle arc
			var tr = Scene.Trace.Ray( from, to ).IgnoreGameObjectHierarchy( Owner.GameObject ).Run();
			if ( tr.Hit )
			{
				var victim = tr.GameObject?.Components.GetInParentOrSelf<DuelistController>();
				if ( victim is not null && victim != Owner && Networking.IsHost )
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
