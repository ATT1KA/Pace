using Sandbox;

namespace TenPaces;

/// <summary>
/// The cylinder: six chambers, gate-loaded shell by shell — Feel Doc §IV.
/// A sequence you SPEND, not a state you enter:
///
///   Idle → GateOpening → Ejecting (per spent shell) → Inserting (per shell,
///   interruptible after each) → GateClosing → Idle
///
/// Every stage reports a distinct, countable sound to the honest ledger —
/// the reload is a public ledger written in clinks [LAW]. Whip staggers
/// interrupt mid-shell (the shell audibly drops, uninserted).
/// </summary>
public enum ReloadStage { Idle, GateOpening, Ejecting, Inserting, GateClosing }

public sealed class Cylinder : Component
{
	[Sync] public int Loaded { get; private set; } = Tuning.CylinderCapacity;
	[Sync] public int Spent { get; private set; } = 0; // spent casings still in chambers
	[Sync] public ReloadStage Stage { get; private set; } = ReloadStage.Idle;

	public bool IsReloading => Stage != ReloadStage.Idle;
	public bool IsEmpty => Loaded <= 0;

	TimeSince _stageTime;
	DuelistController Duelist => Components.GetInParentOrSelf<DuelistController>();

	public void ResetForPoint()
	{
		Loaded = Tuning.CylinderCapacity;
		Spent = 0;
		Stage = ReloadStage.Idle;
	}

	/// <summary> Fire one shell. Returns false on empty (the dead click — its own sound). </summary>
	public bool ConsumeShell()
	{
		if ( Loaded <= 0 )
		{
			SoundLedger.Report( Duelist, LedgerSound.DeadClick, Tuning.RadiusHammerClick );
			return false;
		}
		Loaded--;
		Spent++;
		return true;
	}

	// ── Reload sequence ───────────────────────────────────────────

	public void BeginReload()
	{
		if ( IsReloading || Loaded >= Tuning.CylinderCapacity ) return;
		Stage = ReloadStage.GateOpening;
		_stageTime = 0;
		SoundLedger.Report( Duelist, LedgerSound.GateOpen, Tuning.RadiusReloadClink );
	}

	/// <summary>
	/// Player releases reload / draws / fires / moves fast → the sequence ends
	/// after the CURRENT shell. Free interruption is the whole design.
	/// </summary>
	public void RequestInterrupt() => _interruptRequested = true;
	bool _interruptRequested;

	/// <summary> A whip stagger: the shell in hand drops, audibly, uninserted. </summary>
	public void ForceInterrupt()
	{
		if ( !IsReloading ) return;
		if ( Stage == ReloadStage.Inserting )
			SoundLedger.Report( Duelist, LedgerSound.ShellDropped, Tuning.RadiusReloadClink );
		Stage = ReloadStage.Idle;
		_interruptRequested = false;
	}

	protected override void OnFixedUpdate()
	{
		if ( IsProxy || !IsReloading ) return;

		// Positional honesty: loading on the move is slower & the gait is capped by the controller.
		float mult = Duelist.Body.Velocity.WithZ( 0 ).Length > 15f ? Tuning.ReloadMovingMult : 1f;

		switch ( Stage )
		{
			case ReloadStage.GateOpening:
				if ( _stageTime >= Tuning.ReloadGateOpen * mult )
					Advance( Spent > 0 ? ReloadStage.Ejecting : ReloadStage.Inserting );
				break;

			case ReloadStage.Ejecting:
				if ( _stageTime >= Tuning.ReloadEjectShell * mult )
				{
					Spent--;
					SoundLedger.Report( Duelist, LedgerSound.ShellEject, Tuning.RadiusReloadClink );
					SpawnBrass();
					Advance( Spent > 0 ? ReloadStage.Ejecting : ReloadStage.Inserting );
				}
				break;

			case ReloadStage.Inserting:
				if ( _stageTime >= Tuning.ReloadInsertShell * mult )
				{
					Loaded++;
					SoundLedger.Report( Duelist, LedgerSound.ShellInsert, Tuning.RadiusReloadClink ); // the countable clink
					FeelTelemetry.Instance?.OnShellLoaded( Duelist, Loaded );

					bool done = Loaded >= Tuning.CylinderCapacity || _interruptRequested;
					Advance( done ? ReloadStage.GateClosing : ReloadStage.Inserting );
				}
				break;

			case ReloadStage.GateClosing:
				if ( _stageTime >= Tuning.ReloadGateClose * mult )
				{
					SoundLedger.Report( Duelist, LedgerSound.GateClose, Tuning.RadiusReloadClink );
					Stage = ReloadStage.Idle;
					_interruptRequested = false;
				}
				break;
		}
	}

	void Advance( ReloadStage next ) { Stage = next; _stageTime = 0; }

	[Rpc.Broadcast]
	void SpawnBrass()
	{
		// Physical casing — Rubikon prop, tink on landing. Presentation only.
		var brass = new GameObject( true, "brass" );
		brass.WorldPosition = Duelist.EyePosition + Duelist.EyeRotation.Right * 8f + Vector3.Down * 10f;
		var rb = brass.AddComponent<Rigidbody>();
		var col = brass.AddComponent<SphereCollider>(); col.Radius = 1f;
		rb.Velocity = Duelist.EyeRotation.Right * 60f + Vector3.Up * 40f + Vector3.Random * 20f;
		brass.DestroyAsync( 8f ); // casings persist long enough to be part of the tableau
	}
}
