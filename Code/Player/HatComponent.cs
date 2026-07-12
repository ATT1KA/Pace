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
		_looseHat?.Destroy();
		_looseHat = null;
		IsWorn = true;
		var r = Components.Get<ModelRenderer>( FindMode.EverythingInSelfAndChildren );
		if ( r is not null ) r.Enabled = true;
	}

	/// <summary> World-space hat sphere for the deliberate hat-shot check. </summary>
	public bool IsHatShot( Vector3 hitPos )
	{
		if ( !IsWorn || Socket is null ) return false;
		return hitPos.Distance( Socket.WorldPosition ) <= Tuning.HatShotRadius;
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
