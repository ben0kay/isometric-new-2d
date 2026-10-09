// Populates streamed chunks with weighted grass species from their local biomes.
// Reuses shared placement checks and removes instances with their owning chunk.
using Godot;
using System.Collections.Generic;

public partial class GrassSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region State
    private readonly Dictionary<Vector2I, List<Grass>> _grass = new();
    #endregion

    #region Generation
// =========================================================
// Spawn biome-selected grass with optional locally blended biome colouring.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint)
{
    if (_grass.ContainsKey(coordinate)) yield break;

    List<Grass> tufts = new();
    List<Vector2> placed = new();
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
    _grass.Add(coordinate, tufts);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x4A55u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Grass,
        coordinate, chunkSize, seed ^ 0x6A55u))
    {
        yield return ChunkBuildStage.Grass;
        if (candidate.Biome == null) continue;

        GrassDefinition definition = BiomeSpecies.Select<GrassDefinition>(
            candidate.Biome.Vegetation.Grass, rng);
        if (definition == null) continue;

        float size = definition.RollSize(rng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < 72f * 72f ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, Vector2.Zero,
                obstacles, new Vector2(20, 12)) ||
            WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size))
            continue;

        Color tint = Generator.SampleGrassTint(
            candidate.Tile, out float tintStrength);

        Grass grass = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng),
            BiomeTint = tint,
            BiomeTintStrength = tintStrength
        };

        objects.AddChild(grass);
        tufts.Add(grass);
        placed.Add(localPoint);
    }
}
    #endregion

    #region Streaming
    // =========================================================
    // Release grass instances belonging to one unloaded chunk.
    public IEnumerable<ChunkBuildStage> RemoveSteps(Vector2I coordinate)
    {
        if (!_grass.TryGetValue(coordinate, out List<Grass> tufts)) yield break;
        foreach (Grass grass in tufts)
        {
            if (GodotObject.IsInstanceValid(grass)) grass.QueueFree();
            yield return ChunkBuildStage.Retiring;
        }
        _grass.Remove(coordinate);
    }
    #endregion
}
