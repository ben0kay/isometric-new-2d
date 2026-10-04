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
    [Export(PropertyHint.Range, "0,0.2,0.01")]
    public float BorderWarpFraction { get; set; } = 0.12f;

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
    // Validate definitions and prepare cached terrain and scaled biome placement.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");
        if (Catalog == null)
            throw new InvalidOperationException("WorldGenerator requires a BiomeCatalog.");

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

            _terrain[i] = new TerrainGenerator(
                biome, _chunks.WorldSeed, SandboxPlateau, SandboxPlateauCentre);
            HeightRange = Mathf.Max(HeightRange, _terrain[i].HeightRange);
            MaxTrees = Mathf.Max(MaxTrees, biome.Vegetation.TreesPerChunk);
            MaxPlantPatches = Mathf.Max(
                MaxPlantPatches, biome.Vegetation.PlantPatches);
            MaxGrassPatches = Mathf.Max(
                MaxGrassPatches, biome.Vegetation.GrassPatches);
            MaxRocks = Mathf.Max(MaxRocks, biome.RocksPerChunk);
        }

        float scale = Mathf.Clamp(GenerationScale, 0.125f, 4f);
        _sampler = new BiomeSampler(
            _biomes, _chunks.WorldSeed, PlacementMode, singleIndex,
            RegionSizeTiles * scale, BiomeSizeTiles * scale,
            TransitionWidthTiles * scale, BorderWarpFraction,
            BiomeBandWidth * scale, BiomeBlendWidth * scale);

        // Catch an uncovered starting climate before artwork/world initialization.
        _sampler.Sample(Vector2.Zero);
        GD.Print($"[World] {_biomes.Count} biome(s); mode: {PlacementMode}; scale: {scale}");
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
    // Read terrain height through the shared multi-biome blending calculation.
    public float GetHeight(Vector2 tile)
    {
        return SampleTerrain(tile, out _, out _);
    }

    // =========================================================
    // Blend every contributing biome so multi-way borders remain continuous.
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
            height += sample * entry.Weight;
            plateauWeight += plateau * entry.Weight;
        }
        return height;
    }

    // =========================================================
    // Return shared height, walkability and dominant biome identity.
    public WorldSample SampleTile(Vector2 tile)
    {
        float height = SampleTerrain(
            tile, out float plateauWeight, out int biomeIndex);
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
}