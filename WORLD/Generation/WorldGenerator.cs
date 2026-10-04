// Coordinates biome settings, terrain height and ground classification.
// Samples absolute coordinates so generation is independent from chunk load order.
using Godot;

public partial class WorldGenerator : Node
{
    #region Configuration
    [Export] public BiomeDefinition DefaultBiome { get; set; }
    [Export] public bool SandboxPlateau { get; set; } = true;
    [Export] public Vector2 SandboxPlateauCentre { get; set; } = new(-6, 6);
    #endregion

    #region State
    private TerrainGenerator _terrain;
    private ChunkController _chunks;
    private Node2D _ground;
    private BiomeType _biome;
    public float HeightRange => _terrain?.HeightRange ?? 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the generation service before other systems begin sampling.
    public override void _EnterTree()
    {
        AddToGroup("world_generator");
    }

    // =========================================================
    // Prepare one shared terrain generator for the current world.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");
        BiomeDefinition definition = DefaultBiome ?? new BiomeDefinition();
        _biome = definition.Type;
        _terrain = new TerrainGenerator(
            definition, _chunks.WorldSeed,
            SandboxPlateau, SandboxPlateauCentre);
        SetProcess(false);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Sample height directly for mesh vertices and elevation interpolation.
    public float GetHeight(Vector2 tile)
    {
        return _terrain.SampleHeight(tile, out _);
    }

    // =========================================================
    // Return terrain and biome classification at an absolute tile position.
    public WorldSample SampleTile(Vector2 tile)
    {
        float height = _terrain.SampleHeight(tile, out float plateauWeight);
        int x = Mathf.FloorToInt(tile.X + 0.5f);
        int y = Mathf.FloorToInt(tile.Y + 0.5f);
        return new WorldSample(
            height, !ChasmFeature.IsVoidTile(x, y), _biome, plateauWeight);
    }

    // =========================================================
    // Convert a logical world position into shared generation coordinates.
    public WorldSample SampleWorld(Vector2 globalPoint)
    {
        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(globalPoint), _chunks.TileSize);
        return SampleTile(tile);
    }
    #endregion
}