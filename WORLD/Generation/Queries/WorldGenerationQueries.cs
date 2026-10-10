// Queries this world's seeded biomes without loading chunks or spawning content.
// Planned feature/connection queries will extend this facade in the next pass.
using Godot;
using System;

public sealed class WorldGenerationQueries
{
    #region Services
    private readonly WorldConfig _config;
    private readonly WorldGenerator _surface;
    private readonly Node2D _ground;
    private readonly ChunkController _chunks;
    private readonly WorldLayerRuntime _layers;

    // =========================================================
    // Reuse existing generation services rather than constructing duplicate worlds.
    public WorldGenerationQueries(Node world, WorldLayerRuntime layers)
    {
        _config = WorldConfig.Find(world);
        _surface = world.GetNode<WorldGenerator>("Systems/WorldGenerator");
        _ground = world.GetNode<Node2D>("GroundChunks");
        _chunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _layers = layers;
        _config.ValidateLayerConnectionFrequency(_config.GetLayerCatalog());
    }
    #endregion

    #region Biomes
    // =========================================================
    // Sample logical global ground coordinates, never height-shifted screen positions.
    public BiomeDefinition BiomeAt(string layer, Vector2 worldPosition)
    {
        if (!worldPosition.IsFinite())
            throw new ArgumentException("Biome queries require finite world coordinates.");
        WorldLayerDefinition definition = _config.GetLayerCatalog().Get(layer);
        if (definition.Kind == WorldLayerKind.Surface)
            return _surface.GetBiome(IsoGrid.WorldToTile(
                _ground.ToLocal(worldPosition), _chunks.TileSize));
        CaveWorld cave = _layers?.GetUnderground(layer)
            ?? throw new InvalidOperationException($"Underground generation is unavailable for '{layer}'.");
        return cave.Generator.Biomes.GetBiome(cave.WorldToTile(worldPosition));
    }
    #endregion

    #region Connection Tuning
    // =========================================================
    // Scale the upper layer's candidate probability without generating a corridor.
    public float ConnectionChance(string upperLayer, float baseChance)
    {
        _config.GetLayerCatalog().Get(upperLayer);
        return _config.ScaleLayerConnectionChance(upperLayer, baseChance);
    }
    #endregion
}
