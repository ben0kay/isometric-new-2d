// Stores common species identity, instance variation and replacement artwork.
// Family definitions inherit these settings instead of repeating them.
using Godot;

[Tool, GlobalClass]
public partial class WorldObjectDefinition : Resource
{
	#region Identity
	[ExportGroup("Identity")]
	[Export] public string Id { get; set; } = "";
	#endregion

	#region Variation
	[ExportGroup("Variation")]
	[Export] public Vector2 SizeRange { get; set; } = Vector2.One;
	[Export(PropertyHint.Range, "0,1,0.01")]
	public float MirrorChance { get; set; }
	#endregion

	#region Artwork
	[ExportGroup("Artwork")]
	[Export] public VisualDefinition Visual { get; set; }
	#endregion

		#region Harvesting
	[ExportGroup("Harvesting")]
	[Export] public string HarvestItemId { get; set; } = "";
	[Export] public int HarvestUnits { get; set; } = 1;
	[Export] public float HarvestWork { get; set; } = 18f;
	[Export] public int RequiredMiningStrength { get; set; } = 1;
	#endregion

	#region Instance Variation
	// =========================================================
	// Choose one instance size without changing the shared species resource.
	public float RollSize(RandomNumberGenerator rng)
	{
		float minimum = Mathf.Max(0.1f, SizeRange.X);
		return rng.RandfRange(minimum, Mathf.Max(minimum, SizeRange.Y));
	}

	// =========================================================
	// Choose whether this instance mirrors its artwork horizontally.
	public bool RollMirror(RandomNumberGenerator rng)
	{
		return rng.Randf() < Mathf.Clamp(MirrorChance, 0f, 1f);
	}
	#endregion
}
