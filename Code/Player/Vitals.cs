using Sandbox;
using System;

namespace TenPaces;

public enum HitZone { Miss, Graze, Body, Perfect, Reckoning, Whip, Knife }

/// <summary>
/// The wound model — Feel Doc §VI, exactly:
///   • 100 health; body hit = 50 (two-hit kill); perfect (head/heart) = 100.
///   • Wounded (≤50): NO mechanical slow [LAW]. The game leans on nerves instead —
///     the heartbeat is PRIVATE to the wounded player, never broadcast.
///   • Grazes: outer capsule shell, zero damage, cloth-tug + crack-past + maybe the hat.
///   • Death routes exclusively through MatchDirector.OnDuelistKilled — the single
///     point-award path.
/// Host-authoritative: damage is applied only on the host; clients receive facts.
/// </summary>
public sealed class Vitals : Component
{
	[Sync] public float Health { get; private set; } = Tuning.MaxHealth;
	[Sync] public bool Alive { get; private set; } = true;

	public bool IsWounded => Alive && Health <= Tuning.BodyDamage;
	public float SpeedMultiplier => Tuning.WoundedSpeedMult; // == 1.0 [LAW]

	DuelistController Duelist => GetComponent<DuelistController>();
	SoundHandle _heartbeat;

	public void ResetForPoint()
	{
		Health = Tuning.MaxHealth;
		Alive = true;
		StopHeartbeat();
	}

	// ── Damage entry points (HOST ONLY) ───────────────────────────

	/// <summary> A bullet trace resolved against this duelist. Returns the adjudicated zone. </summary>
	public HitZone ResolveBulletHit( Vector3 hitPos, DuelistController shooter, bool wasKnife = false )
	{
		Assert.True( Networking.IsHost );
		if ( !Alive ) return HitZone.Miss;

		var zone = ClassifyZone( hitPos );

		// Crown hat shot: the hat ABSORBS HALF the shot and is knocked off — it is not
		// a no-damage event and not armor. A head Perfect through the crown becomes a
		// ~50 wound; the next head shot (hat gone) is lethal. Shrinks the instant-kill
		// surface at match start, nothing more.
		if ( Duelist.Hat is not null && Duelist.Hat.IsHatShot( hitPos ) )
		{
			Duelist.Hat.KnockOff( (hitPos - shooter.EyePosition).Normal );
			BroadcastHatShot( shooter.GameObject.Id );
			FeelTelemetry.Instance?.OnHatShot( shooter, Duelist );

			float full = zone switch
			{
				HitZone.Perfect => Tuning.PerfectDamage,
				HitZone.Body    => wasKnife ? Tuning.KnifeBodyDamage : Tuning.BodyDamage,
				_               => 0f,
			};
			if ( full > 0f )
				ApplyDamage( full * Tuning.HatAbsorbFraction, shooter, wasKnife ? HitZone.Knife : zone );
			return HitZone.Graze; // still the hat-shot event for presentation
		}

		switch ( zone )
		{
			case HitZone.Graze:
				BroadcastGraze( hitPos );
				if ( Game.Random.Float() < Tuning.HatKnockChanceGraze )
					Duelist.Hat?.KnockOff( (hitPos - shooter.EyePosition).Normal );
				return HitZone.Graze;

			case HitZone.Perfect:
				// Knife-perfect is lethal too, so the number is identical — only the zone label differs.
				ApplyDamage( Tuning.PerfectDamage, shooter, wasKnife ? HitZone.Knife : HitZone.Perfect );
				return HitZone.Perfect;

			case HitZone.Body:
				ApplyDamage( wasKnife ? Tuning.KnifeBodyDamage : Tuning.BodyDamage, shooter, wasKnife ? HitZone.Knife : HitZone.Body );
				return HitZone.Body;
		}
		return HitZone.Miss;
	}

	public void ApplyWhip( DuelistController attacker )
	{
		Assert.True( Networking.IsHost );
		if ( !Alive ) return;
		ApplyDamage( Tuning.WhipDamage, attacker, HitZone.Whip );
		if ( Alive )
		{
			Duelist.ApplyStagger();
			var push = (WorldPosition - attacker.WorldPosition).WithZ( 0 ).Normal * Tuning.WhipKnockback;
			Duelist.Body.Punch( push );
			Duelist.Hat?.KnockOff( push.Normal ); // hat physics almost always triggered
		}
	}

	public void ApplyReckoningPressure( float dmg )
	{
		Assert.True( Networking.IsHost );
		if ( !Alive ) return;
		Health = MathF.Max( 0, Health - dmg );
		if ( Health <= 0 ) Die( null, HitZone.Reckoning );
		else SyncWoundFx();
	}

	void ApplyDamage( float dmg, DuelistController source, HitZone zone )
	{
		Health = MathF.Max( 0, Health - dmg );
		BroadcastHit( zone, source?.GameObject.Id ?? Guid.Empty );
		if ( Health <= 0 ) Die( source, zone );
		else SyncWoundFx();
	}

	void Die( DuelistController killer, HitZone zone )
	{
		Alive = false;
		Duelist.Hat?.KnockOff( Vector3.Random.WithZ( 0.4f ).Normal );
		BroadcastDeath( (int)zone, killer?.GameObject.Id ?? Guid.Empty );
		MatchDirector.Instance?.OnDuelistKilled( Duelist, killer, zone );
	}

	void SyncWoundFx()
	{
		if ( IsWounded ) BroadcastWounded();
	}

	// ── Zone classification ───────────────────────────────────────

	HitZone ClassifyZone( Vector3 hitPos )
	{
		float z = hitPos.z - WorldPosition.z;

		// Perfect zone: head (top of capsule) or heart (small chest disc).
		bool head = z > Tuning.ZoneHeadMinZ;
		// The heart is a disc on the FRONT of the chest — offset forward along the facing
		// (flattened to horizontal). A back shot to the same height falls through to Body,
		// not an instant kill. (The forward term was previously a *0f placeholder.)
		var heartCenter = WorldPosition
			+ Duelist.EyeRotation.Forward.WithZ( 0 ).Normal * Tuning.ZoneHeartForward
			+ Vector3.Up * Tuning.ZoneHeartHeight;
		bool heart = z is > Tuning.ZoneHeartMinZ and < Tuning.ZoneHeartMaxZ
			&& (hitPos - heartCenter).WithZ( 0 ).Length < Tuning.ZoneHeartRadius;
		if ( head || heart ) return HitZone.Perfect;

		// Inside the body capsule?
		var toHit = (hitPos - WorldPosition).WithZ( 0 ).Length;
		if ( toHit <= Duelist.Body.Radius + Tuning.ZoneBodyMargin && z >= 0 && z <= Tuning.ZoneBodyMaxZ )
			return HitZone.Body;

		// Graze shell — outer capsule
		if ( toHit <= Duelist.Body.Radius * Tuning.GrazeCapsuleScale && z >= Tuning.ZoneGrazeMinZ && z <= Tuning.ZoneGrazeMaxZ )
			return HitZone.Graze;

		return HitZone.Miss;
	}

	// ── Client-side presentation of facts ─────────────────────────

	[Rpc.Broadcast]
	void BroadcastHit( HitZone zone, Guid shooterId )
	{
		ISceneEvent<IVitalsEvents>.Post( x => x.OnDuelistHit( Duelist, zone ) );
		if ( Duelist.IsLocalControlled )
		{
			// directional flinch, sharp in / slow out (camera layer reads this)
			Duelist.Gun?.ApplyFlinch( Tuning.FlinchDegrees );
		}
	}

	[Rpc.Broadcast]
	void BroadcastGraze( Vector3 at )
	{
		// crack-past for the target, cloth-tug visual hook
		ISceneEvent<IVitalsEvents>.Post( x => x.OnGraze( Duelist, at ) );
	}

	[Rpc.Broadcast]
	void BroadcastWounded()
	{
		// The heartbeat is PRIVATE [LAW]: only the wounded player's client starts it.
		if ( Duelist.IsLocalControlled ) StartHeartbeat();
		ISceneEvent<IVitalsEvents>.Post( x => x.OnWounded( Duelist ) );
	}

	[Rpc.Broadcast]
	void BroadcastDeath( int zone, Guid killerId )
	{
		StopHeartbeat();
		ISceneEvent<IVitalsEvents>.Post( x => x.OnDuelistDied( Duelist, (HitZone)zone ) );
		// Ragdoll + dust + directional impulse handled by presentation layer / prefab.
	}

	[Rpc.Broadcast]
	void BroadcastHatShot( Guid shooterId )
	{
		ISceneEvent<IVitalsEvents>.Post( x => x.OnHatShot( Duelist ) );
		Umpire.Instance?.Say( "The hat.", UmpireTone.Grave ); // the sting; VO later
	}

	void StartHeartbeat()
	{
		_heartbeat?.Stop();
		_heartbeat = Sound.Play( "tp.heartbeat" ); // looping .sound asset, 2D, private
		if ( _heartbeat is not null ) _heartbeat.Volume = Tuning.HeartbeatVolume;
	}

	void StopHeartbeat()
	{
		_heartbeat?.Stop();
		_heartbeat = null;
	}
}

public interface IVitalsEvents : ISceneEvent<IVitalsEvents>
{
	void OnDuelistHit( DuelistController d, HitZone zone ) { }
	void OnGraze( DuelistController d, Vector3 at ) { }
	void OnWounded( DuelistController d ) { }
	void OnDuelistDied( DuelistController d, HitZone zone ) { }
	void OnHatShot( DuelistController d ) { }
}
