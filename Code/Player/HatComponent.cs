using Sandbox;

namespace TenPaces;

/// <summary>
/// The hat: a real physics object riding a head socket until violence intervenes.
/// Grazes may knock it, whips almost always do, deaths always do, and the
/// deliberate hat shot (Vitals.IsHatShot) is a named zero-damage event.
/// The hat stays on the ground for the remainder of the point — part of the
/// tableau — and resets at the next point.
/// </summary>
public sealed class HatComponent : Component
{
	[Property] public Model HatModel { get; set; }
	[Property] public GameObject Socket { get; set; }

	[Sync] public bool IsWorn { get; private set; } = true;

	GameObject _looseHat;

	public void ResetForPoint()
	{
		// Host-authoritative synced truth (mirrors KnockOff's host-side write).
		IsWorn = true;
		// Presentation must be restored on EVERY client — SpawnLooseHat broadcast the
		// knock-off (renderer off + loose prop) to all clients, so the reset has to
		// broadcast too. Doing it host-only left remote clients with the worn hat hidden
		// and a leaked loose_hat on the ground every point ("animation never lies" [LAW]).
		BroadcastReset();
	}

	[Rpc.Broadcast]
	void BroadcastReset()
	{
		_looseHat?.Destroy();
		_looseHat = null;
		var r = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndChildren );
		if ( r is not null ) r.Enabled = true;
	}

	/// <summary>
	/// The deliberate hat-shot check — CROWN ONLY. The hat is a cap on the top of the
	/// head: a hit qualifies only if it's near the socket axis horizontally AND at/above
	/// the crown line. A face or front head shot lands lower and passes through to a
	/// lethal head Perfect — the hat shrinks the instant-kill surface, it is not a shield.
	/// </summary>
	public bool IsHatShot( Vector3 hitPos )
	{
		if ( !IsWorn || Socket is null ) return false;
		var socket = Socket.WorldPosition;
		float horiz = (hitPos - socket).WithZ( 0 ).Length;
		float vert = hitPos.z - socket.z;   // negative just below the socket top
		return horiz <= Tuning.HatCrownRadius && vert >= -Tuning.HatCrownDrop;
	}

	public void KnockOff( Vector3 direction )
	{
		if ( !IsWorn ) return;
		IsWorn = false;
		SpawnLooseHat( direction );
	}

	[Rpc.Broadcast]
	void SpawnLooseHat( Vector3 direction )
	{
		var r = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndChildren );
		if ( r is not null ) r.Enabled = false;

		_looseHat = new GameObject( true, "loose_hat" );
		_looseHat.WorldPosition = Socket?.WorldPosition ?? WorldPosition + Vector3.Up * 70f;
		var renderer = _looseHat.AddComponent<ModelRenderer>();
		renderer.Model = HatModel;
		var body = _looseHat.AddComponent<Rigidbody>();
		var col = _looseHat.AddComponent<SphereCollider>();
		col.Radius = 8f;
		body.Velocity = direction.Normal * 180f + Vector3.Up * 120f;
		body.AngularVelocity = Vector3.Random * 12f;

		Sound.Play( "tp.hat_off", _looseHat.WorldPosition );
	}
}
