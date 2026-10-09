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
// Spawn biome-selected rocks using the same placement recipes as vegetation.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius, List<Obstacle> owned)
{
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x9041u);

    float clearSquared = spawnClearRadius * spawnClearRadius;

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Rocks,
        coordinate, chunkSize, seed ^ 0xB041u))
    {
        yield return ChunkBuildStage.Rocks;
        if (candidate.Biome == null) continue;

        RockDefinition definition = BiomeSpecies.Select<RockDefinition>(
            candidate.Biome.Rocks, rng);
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

        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, footprint.Length() * 0.5f + 8f) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, footprint,
                obstacles, new Vector2(12, 8)))
            continue;

        Rock rock = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
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
// Reject basin reservations and solid obstacles for test prop placement.
public static bool CanPlace(
    Node2D objects, Vector2 point, Vector2 footprint)
{
    return !WorldPlacement.IsBlocked(
        objects, point, footprint,
        WorldPlacement.CollectObstacles(objects),
        new Vector2(12, 8));
}
    #endregion
}
