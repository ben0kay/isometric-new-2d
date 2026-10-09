// Populates streamed chunks with weighted tree and plant species from their biomes.
// A plain Node owns bookkeeping; all instances remain under WorldObjects for Y-sorting.
using Godot;
using System.Collections.Generic;

public partial class VegetationSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region State
    private readonly Dictionary<Vector2I, List<Plant>> _plants = new();
    private readonly Dictionary<Vector2I, List<Tree>> _trees = new();
    #endregion

    #region Generation
  // =========================================================
// Spawn biome-selected trees and plants through shared placement recipes.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius)
{
    if (_plants.ContainsKey(coordinate)) yield break;

    List<Plant> plants = new();
    List<Tree> trees = new();
    List<Vector2> placed = new();
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);

    _plants.Add(coordinate, plants);
    _trees.Add(coordinate, trees);

    float clearSquared = spawnClearRadius * spawnClearRadius;

    using RandomNumberGenerator treeRng = new();
    treeRng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0xA471u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Trees,
        coordinate, chunkSize, seed ^ 0xC471u))
    {
        yield return ChunkBuildStage.Trees;
        if (candidate.Biome == null) continue;

        TreeDefinition definition = BiomeSpecies.Select<TreeDefinition>(
            candidate.Biome.Vegetation.Trees, treeRng);
        if (definition == null) continue;

        float size = definition.RollSize(treeRng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
        Vector2 footprint = definition.TrunkFootprint * size;

        float clearance = Mathf.Max(
            definition.GroundClearance * size, footprint.Length() * 0.5f);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(localPoint, tileSize, clearance) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, footprint,
                obstacles, new Vector2(16, 12)) ||
            WorldPlacement.NearTree(globalPoint, definition, size, obstacles))
            continue;

        Tree tree = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = treeRng.RandiRange(
                0, CarbonTreeDrawing.VariantCount - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(treeRng)
        };

        objects.AddChild(tree);
        trees.Add(tree);
        obstacles.Add(tree);
    }

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x5A93u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Plants,
        coordinate, chunkSize, seed ^ 0x7A93u))
    {
        yield return ChunkBuildStage.Plants;
        if (candidate.Biome == null) continue;

        PlantDefinition definition = BiomeSpecies.Select<PlantDefinition>(
            candidate.Biome.Vegetation.Plants, rng);
        if (definition == null) continue;

        float size = definition.RollSize(rng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, Vector2.Zero,
                obstacles, new Vector2(32, 24)) ||
            WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size))
            continue;

        Plant plant = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = rng.RandiRange(0,
                Mathf.Max(1, definition.Visual?.ImageVariantCount > 0
                    ? definition.Visual.ImageVariantCount
                    : VegetationAtlas.VariantsPerKind) - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng)
        };

        objects.AddChild(plant);
        plants.Add(plant);
        placed.Add(localPoint);
    }
}
    #endregion

    #region Streaming
    // =========================================================
    // Release all tree and plant instances belonging to one unloaded chunk.
    public IEnumerable<ChunkBuildStage> RemoveSteps(Vector2I coordinate)
    {
        if (_plants.TryGetValue(coordinate, out List<Plant> plants))
        {
            foreach (Plant plant in plants)
            {
                if (GodotObject.IsInstanceValid(plant)) plant.QueueFree();
                yield return ChunkBuildStage.Retiring;
            }
            _plants.Remove(coordinate);
        }

        if (_trees.TryGetValue(coordinate, out List<Tree> trees))
        {
            foreach (Tree tree in trees)
            {
                if (GodotObject.IsInstanceValid(tree)) tree.QueueFree();
                yield return ChunkBuildStage.Retiring;
            }
            _trees.Remove(coordinate);
        }
    }
    #endregion
}
