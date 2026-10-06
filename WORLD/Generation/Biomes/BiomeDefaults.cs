// Supplies shared biome defaults without copying settings into every biome.
// Individual biomes optionally override these resource references.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeDefaults : Resource
{
    #region Placement Defaults
    [ExportGroup("Placement")]
    [Export] public BiomePlacementSettings Trees { get; set; } = new();

    [Export] public BiomePlacementSettings Plants { get; set; } = new()
    {
        Distribution = BiomeDistribution.Clumps,
        ClumpsPerChunk = 3,
        ClumpRadiusTiles = 1.8f
    };

    [Export] public BiomePlacementSettings Grass { get; set; } = new()
    {
        Distribution = BiomeDistribution.Clumps,
        ClumpsPerChunk = 6,
        ClumpRadiusTiles = 1.3f
    };

    [Export] public BiomePlacementSettings Rocks { get; set; } = new();
    #endregion

    #region Feature Defaults
    [ExportGroup("Features")]
    [Export]
    public Godot.Collections.Dictionary<string, Resource> Features { get; set; }
        = new();
    #endregion

    #region Shared Resource
    private const string DefaultPath =
        "res://CONFIG/Biomes/BiomeDefaults.tres";

    private static BiomeDefaults _shared;

    // =========================================================
    // Load one shared defaults resource for all inheriting biomes.
    public static BiomeDefaults GetShared()
    {
        if (GodotObject.IsInstanceValid(_shared)) return _shared;

        _shared = GD.Load<BiomeDefaults>(DefaultPath);
        if (_shared == null)
            throw new InvalidOperationException(
                $"Shared biome defaults are missing: {DefaultPath}");

        return _shared;
    }
    #endregion
}