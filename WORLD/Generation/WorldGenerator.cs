// Reads registered biomes from the saved catalog and generates sandbox terrain.
// Regional biome selection can later reuse the same catalog without registration code.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldGenerator : Node
{
    #region Configuration
    [Export] public BiomeCatalog Catalog { get; set; }
    [Export] public string SandboxBiomeId { get; set; } = "basalt_flats";
    [Export] public bool SandboxPlateau { get; set; } = true;
    [Export] public Vector2 SandboxPlateauCentre { get; set; } = new(-6, 6);
    #endregion

    #region State
    private TerrainGenerator _terrain;
    private ChunkController _chunks;
    private Node2D _ground;
    private string _biomeId;
    public float HeightRange => _terrain?.HeightRange ?? 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register generation before other systems begin sampling.
    public override void _EnterTree()
    {
        AddToGroup("world_generator");
    }

    // =========================================================
    // Read the catalog and prepare the selected sandbox biome.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");

        if (Catalog == null)
            throw new InvalidOperationException("WorldGenerator requires a BiomeCatalog.");

        List<BiomeDefinition> biomes = Catalog.GetEnabledBiomes();
        BiomeDefinition selected = biomes[0];

        if (!string.IsNullOrWhiteSpace(SandboxBiomeId))
        {
            selected = biomes.Find(biome => biome.Id == SandboxBiomeId);
            if (selected == null)
                throw new InvalidOperationException(
                    $"Sandbox biome '{SandboxBiomeId}' is missing or disabled.");
        }

        _biomeId = selected.Id;
        _terrain = new TerrainGenerator(
            selected, _chunks.WorldSeed,
            SandboxPlateau, SandboxPlateauCentre);
        GD.Print($"[World] {biomes.Count} enabled biome(s); sandbox: {_biomeId}");
        SetProcess(false);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Read shared terrain height for mesh vertices and elevation interpolation.
    public float GetHeight(Vector2 tile)
    {
        return _terrain.SampleHeight(tile, out _);
    }

    // =========================================================
    // Return terrain classification and the selected biome ID.
    public WorldSample SampleTile(Vector2 tile)
    {
        float height = _terrain.SampleHeight(tile, out float plateauWeight);
        int x = Mathf.FloorToInt(tile.X + 0.5f);
        int y = Mathf.FloorToInt(tile.Y + 0.5f);
        return new WorldSample(
            height, !ChasmFeature.IsVoidTile(x, y), _biomeId, plateauWeight);
    }

    // =========================================================
    // Convert a logical world position into absolute tile coordinates.
    public WorldSample SampleWorld(Vector2 globalPoint)
    {
        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(globalPoint), _chunks.TileSize);
        return SampleTile(tile);
    }
    #endregion
}