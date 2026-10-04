// Samples registered biomes, blends their terrain and exposes spawn settings.
// Comparison bands make new biome definitions easy to inspect in the sandbox.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldGenerator : Node
{
    #region Configuration
    [ExportGroup("Biomes")]
    [Export] public BiomeCatalog Catalog { get; set; }
    [Export] public bool CompareBiomes { get; set; } = true;
    [Export] public string SandboxBiomeId { get; set; } = "basalt_flats";
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
    // Register generation before the other world systems begin sampling.
    public override void _EnterTree()
    {
        AddToGroup("world_generator");
    }

    // =========================================================
    // Cache biome terrain generators and the largest required spawning budgets.
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
            if (!CompareBiomes)
                throw new InvalidOperationException(
                    $"Sandbox biome '{SandboxBiomeId}' is missing or disabled.");
            singleIndex = 0;
        }

        _sampler = new BiomeSampler(
            _biomes.Count, CompareBiomes, singleIndex,
            BiomeBandWidth, BiomeBlendWidth);
        _terrain = new TerrainGenerator[_biomes.Count];

        for (int i = 0; i < _biomes.Count; i++)
        {
            BiomeDefinition biome = _biomes[i];
            if (biome.Vegetation == null)
                throw new InvalidOperationException(
                    $"Biome '{biome.Id}' requires vegetation settings.");

            _terrain[i] = new TerrainGenerator(
                biome, _chunks.WorldSeed,
                SandboxPlateau, SandboxPlateauCentre);
            HeightRange = Mathf.Max(HeightRange, _terrain[i].HeightRange);
            MaxTrees = Mathf.Max(MaxTrees, biome.Vegetation.TreesPerChunk);
            MaxPlantPatches = Mathf.Max(
                MaxPlantPatches, biome.Vegetation.PlantPatches);
            MaxGrassPatches = Mathf.Max(
                MaxGrassPatches, biome.Vegetation.GrassPatches);
            MaxRocks = Mathf.Max(MaxRocks, biome.RocksPerChunk);
        }

        GD.Print($"[World] {_biomes.Count} enabled biome(s); comparison: {CompareBiomes}");
        SetProcess(false);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Return the definition controlling vegetation and rocks at this position.
    public BiomeDefinition GetBiome(Vector2 tile)
    {
        return _biomes[_sampler.GetIndex(tile)];
    }

    // =========================================================
    // Return terrain height using the shared border-blending calculation.
    public float GetHeight(Vector2 tile)
    {
        return SampleTerrain(tile, out _);
    }

    // =========================================================
    // Sample at most two cached generators to avoid height seams at biome borders.
    private float SampleTerrain(Vector2 tile, out float plateauWeight)
    {
        _sampler.GetBlend(tile, out int a, out int b, out float weight);
        float height = _terrain[a].SampleHeight(tile, out plateauWeight);
        if (a == b || weight <= 0f) return height;

        float other = _terrain[b].SampleHeight(tile, out float otherPlateau);
        plateauWeight = Mathf.Lerp(plateauWeight, otherPlateau, weight);
        return Mathf.Lerp(height, other, weight);
    }

    // =========================================================
    // Return shared terrain results and the dominant biome's stable ID.
    public WorldSample SampleTile(Vector2 tile)
    {
        float height = SampleTerrain(tile, out float plateauWeight);
        int x = Mathf.FloorToInt(tile.X + 0.5f);
        int y = Mathf.FloorToInt(tile.Y + 0.5f);
        return new WorldSample(
            height, !ChasmFeature.IsVoidTile(x, y),
            GetBiome(tile).Id, plateauWeight);
    }

    // =========================================================
    // Convert a logical world position into absolute terrain coordinates.
    public WorldSample SampleWorld(Vector2 globalPoint)
    {
        return SampleTile(IsoGrid.WorldToTile(
            _ground.ToLocal(globalPoint), _chunks.TileSize));
    }
    #endregion
}