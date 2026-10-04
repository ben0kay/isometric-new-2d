// Defines a biome's terrain and its weighted vegetation/rock recipes.
// Individual species remain reusable resources outside the biome folders.
using Godot;

[Tool, GlobalClass]
public partial class BiomeDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "basalt_flats";
    [Export] public string DisplayName { get; set; } = "Basalt Flats";
    [Export] public bool Enabled { get; set; } = true;
    [Export] public int SandboxOrder { get; set; }
    #endregion

    #region Terrain
    [ExportGroup("Terrain")]

    [ExportSubgroup("Rolling")]
    [Export] public float RollingHeight { get; set; } = 64f;
    [Export] public float RollingFeatureSize { get; set; } = 12f;

    [ExportSubgroup("Plateaus")]
    [Export] public bool PlateausEnabled { get; set; } = true;
    [Export] public float PlateauHeight { get; set; } = 96f;
    [Export] public float PlateauSpacing { get; set; } = 32f;
    [Export] public float PlateauRadius { get; set; } = 6f;
    [Export] public float PlateauShoulder { get; set; } = 6f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PlateauChance { get; set; } = 0.55f;
    #endregion

    #region Vegetation
    [ExportGroup("Vegetation")]
    [Export] public BiomeVegetation Vegetation { get; set; } = new();
    #endregion

    #region Rocks
    [ExportGroup("Rocks")]
    [Export] public int RocksPerChunk { get; set; } = 8;
    [Export] public Godot.Collections.Array<BiomeSpecies> Rocks { get; set; } = new();
    #endregion
}