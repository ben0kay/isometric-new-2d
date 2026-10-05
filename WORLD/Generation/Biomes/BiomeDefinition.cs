// Defines biome terrain, climate eligibility and weighted species recipes.
// Tags describe the biome; continuous climate ranges determine natural placement.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "basalt_flats";
    [Export] public string DisplayName { get; set; } = "Basalt Flats";
    [Export] public bool Enabled { get; set; } = true;
    [Export] public int SandboxOrder { get; set; }
    [Export] public string[] Tags { get; set; } = Array.Empty<string>();
    #endregion

    #region Climate
    [ExportGroup("Climate")]
    [Export] public Vector2 TemperatureRange { get; set; } = new(0, 1);
    [Export] public Vector2 MoistureRange { get; set; } = new(0, 1);
    [Export] public float SelectionWeight { get; set; } = 1f;
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

    #region Enemies
[ExportGroup("Enemies")]
[Export] public BiomeEnemies Enemies { get; set; }
#endregion

    #region Climate Queries
    // =========================================================
    // Return the natural selection weight only when the climate fits this biome.
    public float GetClimateWeight(ClimateSample climate)
    {
        if (climate.Temperature < TemperatureRange.X ||
            climate.Temperature > TemperatureRange.Y ||
            climate.Moisture < MoistureRange.X ||
            climate.Moisture > MoistureRange.Y) return 0f;
        return SelectionWeight;
    }

    // =========================================================
    // Reject invalid normalized ranges and selection weights before world generation.
    public void ValidateClimate()
    {
        ValidateRange(TemperatureRange, "Temperature");
        ValidateRange(MoistureRange, "Moisture");
        if (!float.IsFinite(SelectionWeight) || SelectionWeight < 0f)
            throw new InvalidOperationException(
                $"Biome '{Id}' requires a finite, non-negative SelectionWeight.");
    }

    // =========================================================
    // Require an ordered climate interval within the normalized zero-to-one range.
    private void ValidateRange(Vector2 range, string label)
    {
        if (!float.IsFinite(range.X) || !float.IsFinite(range.Y) ||
            range.X < 0f || range.Y > 1f || range.X > range.Y)
            throw new InvalidOperationException(
                $"Biome '{Id}': {label}Range must satisfy 0 <= minimum <= maximum <= 1.");
    }
    #endregion
}