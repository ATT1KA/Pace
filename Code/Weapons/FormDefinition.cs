using Sandbox;

namespace TenPaces;

public enum FormId { Duelist, Deadeye, Fanning }
public enum TrickId { Coin, Vial, Knife }

/// <summary>
/// A Form is a school of gunfighting — a handling profile over the universal
/// revolver. Data-driven as a GameResource so live tuning (and the annual
/// Form release) never touches code. The three launch Forms ship as .form
/// assets built from the static baselines below; the baselines double as
/// defaults so the game runs asset-less in greybox.
///
/// Design law expressed in data:
///   • Every Form uses the same revolver, same cylinder, same damage model.
///   • Forms shift WHERE the point is won, never WHO wins (no hard counters).
///   • Each Form's draw has a unique audio signature — identifiable by ear.
/// </summary>
[GameResource( "Ten Paces Form", "form", "A school of gunfighting", Icon = "sports_mma" )]
public sealed class FormDefinition : GameResource
{
	[Property] public FormId Id { get; set; }
	[Property] public string DisplayName { get; set; } = "Duelist";
	[Property, TextArea] public string Creed { get; set; } = "";

	[Property, Group( "The Draw" )] public float DrawTime { get; set; } = 0.45f;
	[Property, Group( "The Draw" )] public string DrawSound { get; set; } = "tp.draw_duelist";

	[Property, Group( "Cadence" )] public float TrueBeatInterval { get; set; } = 0.50f;
	[Property, Group( "Cadence" )] public bool CanFan { get; set; } = false;
	[Property, Group( "Cadence" )] public float FanInterval { get; set; } = 0.12f;
	[Property, Group( "Cadence" )] public int FanMaxShells { get; set; } = 3;
	[Property, Group( "Cadence" )] public float FanConeDegrees { get; set; } = 9f;

	[Property, Group( "Accuracy" )] public float ConeScale { get; set; } = 1.0f;    // multiplies the matrix
	[Property, Group( "Accuracy" )] public float EffectiveRange { get; set; } = 1600f; // falloff knee (units)
	[Property, Group( "Accuracy" )] public float BeyondRangeConeAdd { get; set; } = 2.0f;

	// ── Launch baselines (asset-less defaults) ────────────────────

	public static FormDefinition Baseline( FormId id ) => id switch
	{
		FormId.Deadeye => new FormDefinition
		{
			Id = FormId.Deadeye, DisplayName = "Deadeye",
			Creed = "One shot is a sentence. Speak rarely; never misspeak.",
			DrawTime = 0.70f, DrawSound = "tp.draw_deadeye",
			TrueBeatInterval = 0.65f, CanFan = false,
			ConeScale = 0.75f, EffectiveRange = 2400f, BeyondRangeConeAdd = 0.8f,
		},
		FormId.Fanning => new FormDefinition
		{
			Id = FormId.Fanning, DisplayName = "Fanning",
			Creed = "Distance is the enemy. Close it, and end the argument at once.",
			DrawTime = 0.60f, DrawSound = "tp.draw_fanning",
			TrueBeatInterval = 0.55f, CanFan = true,
			FanInterval = 0.12f, FanMaxShells = 3, FanConeDegrees = 9f,
			ConeScale = 1.25f, EffectiveRange = 700f, BeyondRangeConeAdd = 5.0f,
		},
		_ => new FormDefinition
		{
			Id = FormId.Duelist, DisplayName = "Duelist",
			Creed = "Win the moment between moments. Your habits are my ammunition.",
			DrawTime = 0.45f, DrawSound = "tp.draw_duelist",
			TrueBeatInterval = 0.50f, CanFan = false,
			ConeScale = 1.0f, EffectiveRange = 1400f, BeyondRangeConeAdd = 2.0f,
		},
	};
}
