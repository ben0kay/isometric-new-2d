// Global surface ore placement settings; biome overrides can be added later.
using Godot;
using System;

[Tool, GlobalClass]
public partial class OreSpawnSettings : Resource
{
    [Export] public bool Enabled { get; set; } = true;
    [Export] public OreDefinition Definition { get; set; }
    [Export(PropertyHint.Range, "0,1,0.01")] public double ChancePerChunk { get; set; } = 0.15;
    [Export(PropertyHint.Range, "1,16,1")] public int PlacementAttempts { get; set; } = 6;
    [Export] public Vector2 ClearancePixels { get; set; } = new(24, 16);
    [Export] public float MaximumHeightVariation { get; set; } = 0.06f;

    public void Validate(ItemCatalog catalog)
    {
        if (Definition == null || string.IsNullOrEmpty(Definition.ResourcePath) ||
            Definition.ResourcePath.Contains("::") || !double.IsFinite(ChancePerChunk) ||
            ChancePerChunk < 0 || ChancePerChunk > 1 || PlacementAttempts < 1 || PlacementAttempts > 16 ||
            !float.IsFinite(ClearancePixels.X) || !float.IsFinite(ClearancePixels.Y) ||
            ClearancePixels.X < 0 || ClearancePixels.Y < 0 ||
            !float.IsFinite(MaximumHeightVariation) || MaximumHeightVariation < 0 ||
            !float.IsFinite(Definition.Footprint.X) || !float.IsFinite(Definition.Footprint.Y) ||
            Definition.Footprint.X <= 0 || Definition.Footprint.Y <= 0 ||
            !float.IsFinite(Definition.SizeRange.X) || !float.IsFinite(Definition.SizeRange.Y) ||
            Definition.SizeRange.X <= 0 || Definition.SizeRange.Y < Definition.SizeRange.X)
            throw new InvalidOperationException("Invalid surface ore spawn settings: " + ResourcePath);
        _ = new OreBatchPlan(Definition, catalog);
    }
}
