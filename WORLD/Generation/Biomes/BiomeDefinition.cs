// Defines a biome's identity, terrain and population recipes.
// Optional overrides inherit shared defaults; terrain creation can be specialized.
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

    [Export] public Godot.Collections.Array<BiomeSpecies> Rocks { get; set; }
        = new();

    [Export] public BiomePlacementSettings RocksPlacement { get; set; }
    #endregion

    #region Feature Overrides
    [ExportGroup("Features")]
    [Export]
    public Godot.Collections.Dictionary<string, Resource> FeatureOverrides
        { get; set; } = new();
    #endregion

    #region Enemies
    [ExportGroup("Enemies")]
    [Export] public BiomeEnemies Enemies { get; set; }
    #endregion

    #region Shared Settings
    // =========================================================
    // Resolve a biome override or the shared placement profile.
    public BiomePlacementSettings GetPlacement(BiomePopulationFamily family)
    {
        BiomeDefaults defaults = BiomeDefaults.GetShared();

        BiomePlacementSettings settings = family switch
        {
            BiomePopulationFamily.Trees =>
                Vegetation.TreesPlacement ?? defaults.Trees,
            BiomePopulationFamily.Plants =>
                Vegetation.PlantsPlacement ?? defaults.Plants,
            BiomePopulationFamily.Grass =>
                Vegetation.GrassPlacement ?? defaults.Grass,
            BiomePopulationFamily.Rocks =>
                RocksPlacement ?? defaults.Rocks,
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

        return settings ?? throw new InvalidOperationException(
            $"{Id}/{family}: shared placement profile is missing.");
    }

// =========================================================
// Resolve the biome's own feature reference or a shared default.
public T GetFeature<T>(string id) where T : Resource
{
    Resource profile = null;

    if (FeatureOverrides != null &&
        FeatureOverrides.TryGetValue(id, out Resource local) &&
        local != null)
    {
        profile = local;
    }
    else
    {
        BiomeDefaults defaults = BiomeDefaults.GetShared();
        if (defaults.Features != null)
            defaults.Features.TryGetValue(id, out profile);
    }

    if (profile == null) return null;
    if (profile is T typed) return typed;

    throw new InvalidOperationException(
        $"Biome '{Id}': feature '{id}' requires {typeof(T).Name}.");
}

    #endregion

    #region Terrain Factory
    // =========================================================
    // Use shared terrain unless a specialized biome definition overrides this.
    public virtual TerrainGenerator CreateTerrain(
        uint seed, bool testPlateau, Vector2 testCentre)
    {
        return new TerrainGenerator(this, seed, testPlateau, testCentre);
    }
    #endregion

    #region Climate Queries
    // =========================================================
    // Return selection weight only when climate fits this biome.
    public float GetClimateWeight(ClimateSample climate)
    {
        if (climate.Temperature < TemperatureRange.X ||
            climate.Temperature > TemperatureRange.Y ||
            climate.Moisture < MoistureRange.X ||
            climate.Moisture > MoistureRange.Y)
            return 0f;

        return SelectionWeight;
    }

    // =========================================================
    // Validate climate and the resolved rock placement profile.
    public void ValidateClimate()
    {
        ValidateRange(TemperatureRange, "Temperature");
        ValidateRange(MoistureRange, "Moisture");

        if (!float.IsFinite(SelectionWeight) || SelectionWeight < 0f)
            throw new InvalidOperationException(
                $"Biome '{Id}' requires a finite, non-negative SelectionWeight.");

        GetPlacement(BiomePopulationFamily.Rocks).Validate($"{Id}/Rocks");
    }

    // =========================================================
    // Require an ordered normalized climate interval.
    private void ValidateRange(Vector2 range, string label)
    {
        if (!float.IsFinite(range.X) || !float.IsFinite(range.Y) ||
            range.X < 0f || range.Y > 1f || range.X > range.Y)
            throw new InvalidOperationException(
                $"Biome '{Id}': {label}Range must satisfy " +
                "0 <= minimum <= maximum <= 1.");
    }
    #endregion
}