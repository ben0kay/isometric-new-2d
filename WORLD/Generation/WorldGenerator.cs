// Coordinates climate-aware biome placement, blended terrain and population queries.
// Generation uses absolute tile coordinates independently from streamed chunk ownership.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldGenerator : Node
{
    #region Configuration
    [ExportGroup("Biomes")]
    [Export] public BiomeCatalog Catalog { get; set; }
    [Export] public BiomePlacementMode PlacementMode { get; set; }
        = BiomePlacementMode.Natural;
    [Export] public string SandboxBiomeId { get; set; } = "basalt_flats";

    [ExportGroup("Generation Scale")]
    [Export(PropertyHint.Range, "0.125,4,0.125")]
    public float GenerationScale { get; set; } = 1f;
    [Export] public float RegionSizeTiles { get; set; } = 256f;
    [Export] public float BiomeSizeTiles { get; set; } = 96f;
    [Export] public float TransitionWidthTiles { get; set; } = 8f;
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float BorderWarpFraction { get; set; } = 0.55f;

    [ExportGroup("Comparison")]
    [Export] public float BiomeBandWidth { get; set; } = 24f;
    [Export] public float BiomeBlendWidth { get; set; } = 4f;

    [ExportGroup("Test Plateau")]
    [Export] public bool SandboxPlateau { get; set; }
    [Export] public Vector2 SandboxPlateauCentre { get; set; } = new(-6, 6);
    #endregion

    #region State
    private List<BiomeDefinition> _biomes;
    private TerrainGenerator[] _terrain;
    private BiomeSampler _sampler;
    private ChunkController _chunks;
    private Node2D _ground;
    private WaterBasinWorld _waterBasins;

    public float HeightRange { get; private set; } = 1f;
    public int MaxTrees { get; private set; }
    public int MaxPlantPatches { get; private set; }
    public int MaxGrassPatches { get; private set; }
    public int MaxRocks { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register generation before other world systems begin sampling.
    public override void _EnterTree()
    {
        AddToGroup("world_generator");
    }

// =========================================================
// Build scaled biome placement, then prepare permanent basin geometry.
public override void _Ready()
{
    _chunks = GetNode<ChunkController>("../ChunkController");
    _ground = GetNode<Node2D>("../../GroundChunks");

    if (Catalog == null)
        throw new InvalidOperationException(
            "WorldGenerator requires a BiomeCatalog.");

    BiomeDefaults.GetShared();

    _biomes = Catalog.GetEnabledBiomes();
    _biomes.Sort((a, b) =>
    {
        int order = a.SandboxOrder.CompareTo(b.SandboxOrder);
        return order != 0 ? order : string.CompareOrdinal(a.Id, b.Id);
    });

    int singleIndex = _biomes.FindIndex(b => b.Id == SandboxBiomeId);
    if (singleIndex < 0)
    {
        if (PlacementMode == BiomePlacementMode.Single)
            throw new InvalidOperationException(
                $"Sandbox biome '{SandboxBiomeId}' is missing or disabled.");
        singleIndex = 0;
    }

    _terrain = new TerrainGenerator[_biomes.Count];
    HeightRange = 1f;
    MaxTrees = MaxPlantPatches = MaxGrassPatches = MaxRocks = 0;

    for (int i = 0; i < _biomes.Count; i++)
    {
        BiomeDefinition biome = _biomes[i];
        biome.ValidateClimate();

        if (biome.Vegetation == null)
            throw new InvalidOperationException(
                $"Biome '{biome.Id}' requires vegetation settings.");

        biome.Vegetation.Validate(biome.Id);
        BiomeSpecies.Validate<RockDefinition>(
            biome.Rocks, $"{biome.Id}/Rocks", biome.RocksPerChunk > 0);

        _terrain[i] = biome.CreateTerrain(
            _chunks.WorldSeed, SandboxPlateau, SandboxPlateauCentre);

        if (_terrain[i] == null)
            throw new InvalidOperationException(
                $"Biome '{biome.Id}' returned no terrain sampler.");

                HeightRange = Mathf.Max(
            HeightRange,
            Mathf.Abs(biome.BaseElevation) + _terrain[i].HeightRange);
        MaxTrees = Mathf.Max(MaxTrees, biome.Vegetation.TreesPerChunk);
        MaxPlantPatches = Mathf.Max(
            MaxPlantPatches, biome.Vegetation.PlantPatches);
        MaxGrassPatches = Mathf.Max(
            MaxGrassPatches, biome.Vegetation.GrassPatches);
        MaxRocks = Mathf.Max(MaxRocks, biome.RocksPerChunk);
    }

    WorldConfig config = WorldConfig.Find(this);
    float multiplier = config.BiomeScaleMultiplier;

    if (!float.IsFinite(multiplier) || multiplier <= 0f)
        throw new InvalidOperationException(
            "BiomeScaleMultiplier must be finite and positive.");

    float scale = Mathf.Clamp(GenerationScale, 0.125f, 4f) * multiplier;

    _sampler = new BiomeSampler(
        _biomes, _chunks.WorldSeed, PlacementMode, singleIndex,
        RegionSizeTiles * scale, BiomeSizeTiles * scale,
        TransitionWidthTiles * scale, BorderWarpFraction,
        BiomeBandWidth * scale, BiomeBlendWidth * scale);

    _waterBasins = new WaterBasinWorld { Name = "WaterBasins" };
    AddChild(_waterBasins);
    _waterBasins.Initialize(this, _chunks, _ground);
    HeightRange = Mathf.Max(HeightRange, _waterBasins.MaximumDepth);

    _sampler.Sample(Vector2.Zero);
    GD.Print(
        $"[World] {_biomes.Count} biome(s); " +
        $"mode: {PlacementMode}; biome scale: {scale}");

    SetProcess(false);
}
    #endregion

    #region Biome Queries
    // =========================================================
    // Read the dominant biome for HUD labels and classification.
    public BiomeDefinition GetBiome(Vector2 tile)
    {
        return _biomes[_sampler.Sample(tile).DominantIndex];
    }

    // =========================================================
    // Mix biome population recipes according to the local transition weights.
    public BiomeDefinition PickBiome(Vector2 tile, RandomNumberGenerator rng)
    {
        return _biomes[_sampler.Sample(tile).Pick(rng)];
    }

    // =========================================================
    // Expose normalized temperature and moisture for debugging and future mechanics.
    public ClimateSample SampleClimate(Vector2 tile)
    {
        return _sampler.Climate.Sample(tile);
    }
    #endregion

    #region Terrain Queries
// =========================================================
// Read terrain including permanent water-basin geometry.
public float GetHeight(Vector2 tile)
{
    float height = GetBaseHeight(tile);
    return _waterBasins?.ApplyHeight(tile, height) ?? height;
}

// =========================================================
// Read original terrain for basin eligibility without including carved basins.
public float GetBaseHeight(Vector2 tile)
{
    return SampleTerrain(tile, out _, out _);
}

    // =========================================================
    // Blend biome base elevations and local terrain shapes together.
    private float SampleTerrain(
        Vector2 tile, out float plateauWeight, out int biomeIndex)
    {
        BiomeBlend blend = _sampler.Sample(tile);
        biomeIndex = blend.DominantIndex;
        plateauWeight = 0f;
        float height = 0f;

        for (int i = 0; i < blend.Count; i++)
        {
            BiomeInfluence entry = blend.Get(i);
            float sample = _terrain[entry.Index].SampleHeight(
                tile, out float plateau);

            height +=
                (_biomes[entry.Index].BaseElevation + sample) * entry.Weight;
            plateauWeight += plateau * entry.Weight;
        }

        return height;
    }

// =========================================================
// Keep generation queries consistent with rendered basin terrain.
public WorldSample SampleTile(Vector2 tile)
{
    float height = SampleTerrain(
        tile, out float plateauWeight, out int biomeIndex);
    height = _waterBasins?.ApplyHeight(tile, height) ?? height;

    int x = Mathf.FloorToInt(tile.X + 0.5f);
    int y = Mathf.FloorToInt(tile.Y + 0.5f);
    return new WorldSample(
        height, !ChasmFeature.IsVoidTile(x, y),
        _biomes[biomeIndex].Id, plateauWeight);
}

    // =========================================================
    // Convert a logical world position into absolute generation coordinates.
    public WorldSample SampleWorld(Vector2 globalPoint)
    {
        return SampleTile(IsoGrid.WorldToTile(
            _ground.ToLocal(globalPoint), _chunks.TileSize));
    }

    #endregion

        #region Ground Queries
    // =========================================================
    // Blend ground recipes using the existing smooth biome influences.
    public void SampleGround(
        Vector2 tile, out Color settings,
        out Color shade, out Color main)
    {
        BiomeBlend blend = _sampler.Sample(tile);
        settings = new Color(0, 0, 0, 0);

        Color shadeSum = new(0, 0, 0, 0);
        Color mainSum = new(0, 0, 0, 0);
        float grassWeight = 0f;
        float patchScale = 0f;
        float mudStrength = 0f;

        for (int i = 0; i < blend.Count; i++)
        {
            BiomeInfluence entry = blend.Get(i);
            BiomeGroundProfile profile =
                _biomes[entry.Index].GetFeature<BiomeGroundProfile>("ground");

            if (profile == null)
                throw new InvalidOperationException(
                    $"Biome '{_biomes[entry.Index].Id}' has no ground profile.");

            profile.Validate();

            settings += new Color(
                profile.DustAmount, profile.MineralAmount,
                profile.DetailStrength, profile.GrassCoverage) * entry.Weight;

            float contribution = profile.GrassCoverage * entry.Weight;
            grassWeight += contribution;
            BiomeColourProfile colours = _biomes[entry.Index].ColourProfile;
            bool tinted = colours != null && colours.TintGroundGrass;
            shadeSum += (tinted ? colours.GroundGrassShade
                : BiomeColourProfile.DefaultGroundGrassShade) * contribution;
            mainSum += (tinted ? colours.GroundGrassMain
                : BiomeColourProfile.DefaultGroundGrassMain) * contribution;
            patchScale += profile.GrassPatchScaleTiles * contribution;

            mudStrength += profile.MudStrength * entry.Weight;
        }

        // Ignore colours from biomes that have no grass ground coverage.
        if (grassWeight > 0.0001f)
        {
            shade = shadeSum / grassWeight;
            main = mainSum / grassWeight;
            shade.A = patchScale / grassWeight;
        }
        else
        {
            shade = new Color(0, 0, 0, 32f);
            main = new Color(0, 0, 0, 0);
        }

        // Texture alpha channels carry settings, not transparency.
        main.A = mudStrength;
    }

    // =========================================================
    // Blend optional tuft colours at the actual grass spawn position.
    public Color SampleGrassTint(Vector2 tile, out float tintStrength)
    {
        BiomeBlend blend = _sampler.Sample(tile);
        Color sum = new(0, 0, 0, 0);
        tintStrength = 0f;

        for (int i = 0; i < blend.Count; i++)
        {
            BiomeInfluence entry = blend.Get(i);
            BiomeColourProfile colours = _biomes[entry.Index].ColourProfile;
            if (colours == null || !colours.TintGrass) continue;

            float contribution = entry.Weight * colours.GrassTintStrength;
            sum += colours.GrassTint * contribution;
            tintStrength += contribution;
        }

        if (tintStrength <= 0.0001f) return Colors.White;

        Color result = sum / tintStrength;
        result.A = 1f;
        return result;
    }

    // =========================================================
// Cache mountain rock and path coverage using the existing biome blend.
public Color SampleMountainGround(Vector2 tile)
{
    BiomeBlend blend = _sampler.Sample(tile);
    float rock = 0f, path = 0f;
    float slope = -1f;

    for (int i = 0; i < blend.Count; i++)
    {
        BiomeInfluence entry = blend.Get(i);

        if (_biomes[entry.Index] is not RockyMountainsBiome biome ||
            _terrain[entry.Index] is not MountainTerrainGenerator terrain)
            continue;

        if (slope < 0f)
        {
            float alongX = GetHeight(tile + new Vector2(0.5f, 0f)) -
                GetHeight(tile - new Vector2(0.5f, 0f));
            float alongY = GetHeight(tile + new Vector2(0f, 0.5f)) -
                GetHeight(tile - new Vector2(0f, 0.5f));

            Vector2 gradient = new(
                (alongX - alongY) / Mathf.Max(1f, _chunks.TileSize.X),
                (alongX + alongY) / Mathf.Max(1f, _chunks.TileSize.Y));

            slope = gradient.Length();
        }

        float steep = Mathf.Clamp(
            (slope - biome.RockSlopeStart) /
            (biome.RockSlopeFull - biome.RockSlopeStart), 0f, 1f);
        steep = steep * steep * (3f - 2f * steep);

        float localPath = terrain.SamplePathSurface(tile);

        float localRock = Mathf.Lerp(
            biome.BaseRockCoverage, biome.SteepRockCoverage, steep);

        rock += localRock * (1f - localPath) * entry.Weight;
        path += localPath * biome.PathSurfaceStrength * entry.Weight;
    }

    // Red stores rock coverage; green stores path coverage.
    return new Color(rock, path, 0f, 1f);
}
    #endregion

        // =========================================================
    // Expose whether the returned terrain height includes completed basin data.
    public bool TryGetHeight(Vector2 tile, out float height)
    {
        float original = GetBaseHeight(tile);
        height = original;

        return _waterBasins == null ||
            _waterBasins.TryApplyHeight(tile, original, out height);
    }
}
