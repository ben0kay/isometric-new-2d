// Generates solid rock instances from weighted species references in each biome.
// Shares footprint placement checks with vegetation and records chunk ownership.
using Godot;
using System.Collections.Generic;

public partial class RockSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region Generation
// =========================================================
// Mix local biome rock recipes while preserving solid footprint placement.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius, List<Obstacle> owned)
{
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0xB041u);

    float lowX = coordinate.X * chunkSize - 0.5f;
    float lowY = coordinate.Y * chunkSize - 0.5f;
    float clearSquared = spawnClearRadius * spawnClearRadius;
    int budget = Generator.MaxRocks * 4;

    for (int attempt = 0; attempt < budget; attempt++)
    {
        yield return ChunkBuildStage.Rocks;
        Vector2 tile = new(
            rng.RandfRange(lowX, lowX + chunkSize),
            rng.RandfRange(lowY, lowY + chunkSize));
        BiomeDefinition biome = Generator.PickBiome(tile, rng);
        float chance = Mathf.Clamp(
            (float)biome.RocksPerChunk / budget, 0f, 1f);
        if (rng.Randf() >= chance) continue;

        RockDefinition definition =
            BiomeSpecies.Select<RockDefinition>(biome.Rocks, rng);
        if (definition == null) continue;

        float minWidth = Mathf.Max(16f, definition.WidthRange.X);
        float minHeight = Mathf.Max(8f, definition.HeightRange.X);
        float width = rng.RandfRange(
            minWidth, Mathf.Max(minWidth, definition.WidthRange.Y));
        float height = rng.RandfRange(
            minHeight, Mathf.Max(minHeight, definition.HeightRange.Y));
        float size = definition.RollSize(rng);
        Vector2 footprint = new Vector2(
            width, width * Mathf.Max(0.1f, definition.FootprintDepthRatio)) * size;

        Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared) continue;
        if (!ChasmFeature.HasGroundClearance(
            localPoint, tileSize, footprint.Length() * 0.5f + 8f)) continue;
        if (WorldPlacement.IsBlocked(
            globalPoint, footprint, obstacles, new Vector2(12, 8))) continue;

        Rock rock = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{attempt}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            BaseWidth = width,
            BaseHeight = height,
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng),
            RockVariant = rng.RandiRange(0, RockDrawing.VariantCount - 1)
        };
        objects.AddChild(rock);
        owned.Add(rock);
        obstacles.Add(rock);
    }
}
    #endregion

    #region Test Prop Placement
    // =========================================================
    // Preserve the existing crate placement API using the shared footprint test.
    public static bool CanPlace(
        Node2D objects, Vector2 point, Vector2 footprint)
    {
        return !WorldPlacement.IsBlocked(
            point, footprint, WorldPlacement.CollectObstacles(objects),
            new Vector2(12, 8));
    }
    #endregion
}
