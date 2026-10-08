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
        [Export] public float BaseElevation { get; set; } = 0f;

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

    #region Colour Profile
    [ExportGroup("Colour Profile")]
    [Export] public BiomeColourProfile ColourProfile { get; set; }
    #endregion

    #region Content
    [ExportGroup("Content")]
    [Export] public BiomeContent Content { get; set; }
    #endregion

    #region Legacy Population
    // Existing biome resources retain their settings until migrated to Content.
    // These accessors also keep current shared spawners using one resolved source.
    private BiomeVegetation _vegetation = new();
    private int _rocksPerChunk = 8;
    private Godot.Collections.Array<BiomeSpecies> _rocks = new();
    private BiomePlacementSettings _rocksPlacement;

    [ExportGroup("Legacy Population")]
    [ExportSubgroup("Vegetation")]
    [Export]
    public BiomeVegetation Vegetation
    {
        get => Content != null ? Content.Vegetation : _vegetation;
        set => _vegetation = value;
    }

    [ExportSubgroup("Rocks")]
    [Export]
    public int RocksPerChunk
    {
        get => Content != null ? Content.RocksPerChunk : _rocksPerChunk;
        set => _rocksPerChunk = value;
    }

    [Export]
    public Godot.Collections.Array<BiomeSpecies> Rocks
    {
        get => Content != null ? Content.Rocks : _rocks;
        set => _rocks = value;
    }

    [Export]
    public BiomePlacementSettings RocksPlacement
    {
        get => Content != null ? Content.RocksPlacement : _rocksPlacement;
        set => _rocksPlacement = value;
    }
    #endregion

    #region Feature Overrides
    [ExportGroup("Features")]
    [Export]
    public Godot.Collections.Dictionary<string, Resource> FeatureOverrides
        { get; set; } = new();
    #endregion

    #region Legacy Enemies
    private BiomeEnemies _enemies;

    [ExportGroup("Legacy Enemies")]
    [Export]
    public BiomeEnemies Enemies
    {
        get => Content != null ? Content.Enemies : _enemies;
        set => _enemies = value;
    }
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
    // Validate climate, base elevation and shared rock placement.
    public void ValidateClimate()
    {
        ValidateRange(TemperatureRange, "Temperature");
        ValidateRange(MoistureRange, "Moisture");
        ColourProfile?.Validate(Id);

        if (!float.IsFinite(BaseElevation))
            throw new InvalidOperationException(
                $"Biome '{Id}' requires a finite BaseElevation.");

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
