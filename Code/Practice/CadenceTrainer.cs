using Sandbox;
using System;

namespace TenPaces;

/// <summary>
/// THE CADENCE TRAINER — the game's wordless tutorial and its daily ritual.
///
/// A row of bottles in the practice range. No text, no prompts. The player
/// shoots; mashing shatters nothing; riding the click shatters everything.
/// The whole true-beat system is taught by glass in under a minute — the
/// First Miss Test and the Metronome Test, engineered as one object.
///
/// The elite layer: STREAK. Consecutive on-beat hits build a streak counter
/// (just-frames count double), and the range keeps your best — the daily
/// warm-up leaderboard is the game's equivalent of CS aim maps, first-party.
/// The streak chime rises in pitch as the streak grows: mastery you can hear.
/// </summary>
public sealed class CadenceTrainer : Component, IGunEvents
{
	[Property] public GameObject BottleRowRoot { get; set; }
	[Property] public int BottleCount { get; set; } = 12;
	[Property] public float BottleSpacing { get; set; } = 26f;

	[Sync] public int Streak { get; private set; }
	[Sync] public int BestStreak { get; private set; }

	GameObject[] _bottles;
	TimeSince _sinceRespawn = 0;

	protected override void OnStart() => SpawnRow();

	void SpawnRow()
	{
		_bottles = new GameObject[BottleCount];
		var root = BottleRowRoot?.WorldPosition ?? WorldPosition;
		var right = (BottleRowRoot?.WorldRotation ?? WorldRotation).Right;
		for ( int i = 0; i < BottleCount; i++ )
		{
			var b = new GameObject( true, $"bottle_{i}" );
			b.WorldPosition = root + right * (i - BottleCount / 2f) * BottleSpacing + Vector3.Up * 40f;
			b.Tags.Add( "bottle" );
			var col = b.AddComponent<BoxCollider>();
			col.Scale = new Vector3( 6, 6, 16 );
			var renderer = b.AddComponent<ModelRenderer>();
			renderer.Model = Model.Load( "models/dev/box.vmdl" ); // greybox glass
			_bottles[i] = b;
		}
	}

	protected override void OnFixedUpdate()
	{
		// Row respawns quietly when depleted
		if ( _bottles is null ) return;
		bool anyAlive = false;
		foreach ( var b in _bottles ) if ( b.IsValid() ) { anyAlive = true; break; }
		if ( !anyAlive && _sinceRespawn > 2f )
		{
			SpawnRow();
			_sinceRespawn = 0;
		}
	}

	// Listen to the same gun events everything else hears.
	public void OnMuzzle( DuelistController shooter, Vector3 origin, Vector3 dir, bool justFrame )
	{
		var tr = Scene.Trace.Ray( origin, origin + dir * 5000f )
			.IgnoreGameObjectHierarchy( shooter.GameObject ).Run();

		if ( tr.Hit && tr.GameObject is not null && tr.GameObject.Tags.Has( "bottle" ) )
		{
			tr.GameObject.Destroy();
			Sound.Play( "tp.bottle_shatter", tr.HitPosition );

			// On-beat discipline is what the streak measures. A blooming shot
			// that got lucky still hit — but earliness resets the streak, so
			// the leaderboard measures COMPOSURE, not luck.
			bool wasDisciplined = shooter.Gun.CurrentConeDegrees( 0f ) <= Tuning.ConePlantedHip * 1.5f;
			if ( wasDisciplined )
			{
				Streak += justFrame ? 2 : 1;
				BestStreak = Math.Max( BestStreak, Streak );
				// rising chime: mastery you can hear (pitch hook via streak count)
				Sound.Play( "tp.streak_chime", tr.HitPosition );
				FeelTelemetry.Instance?.OnTrainerStreak( shooter, Streak, justFrame );
			}
			else Streak = 0;
		}
		else if ( tr.Hit )
		{
			Streak = 0; // a miss is the player's miss — and the dirt kick already said so
		}
	}
}
