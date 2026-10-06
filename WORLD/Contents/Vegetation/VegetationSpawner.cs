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
// Mix tree and plant recipes while keeping their placement out of basins.
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

    float lowX = coordinate.X * chunkSize - 0.5f;
    float lowY = coordinate.Y * chunkSize - 0.5f;
    float highX = lowX + chunkSize, highY = lowY + chunkSize;
    float clearSquared = spawnClearRadius * spawnClearRadius;

    using RandomNumberGenerator treeRng = new();
    treeRng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0xC471u);

    int treeBudget = Generator.MaxTrees * 4;
    for (int attempt = 0; attempt < treeBudget; attempt++)
    {
        yield return ChunkBuildStage.Trees;
        Vector2 tile = new(
            treeRng.RandfRange(lowX, highX),
            treeRng.RandfRange(lowY, highY));
        BiomeVegetation settings = Generator.PickBiome(tile, treeRng).Vegetation;
        float chance = Mathf.Clamp(
            (float)settings.TreesPerChunk / treeBudget, 0f, 1f);
        if (treeRng.Randf() >= chance) continue;

        TreeDefinition definition =
            BiomeSpecies.Select<TreeDefinition>(settings.Trees, treeRng);
        if (definition == null) continue;

        float size = definition.RollSize(treeRng);
        Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
        Vector2 footprint = definition.TrunkFootprint * size;
        float clearance = Mathf.Max(
            definition.GroundClearance * size, footprint.Length() * 0.5f);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared)
            continue;
        if (!ChasmFeature.HasGroundClearance(localPoint, tileSize, clearance))
            continue;
        if (WorldPlacement.IsBlocked(
            objects, globalPoint, footprint, obstacles, new Vector2(16, 12)))
            continue;
        if (WorldPlacement.NearTree(globalPoint, definition, size, obstacles))
            continue;

        Tree tree = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{attempt}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = treeRng.RandiRange(0, CarbonTreeDrawing.VariantCount - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(treeRng)
        };

        objects.AddChild(tree);
        trees.Add(tree);
        obstacles.Add(tree);
    }

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0x7A93u);

    int patchBudget = Generator.MaxPlantPatches;
    for (int patch = 0; patch < patchBudget; patch++)
    {
        yield return ChunkBuildStage.Plants;
        Vector2 centre = new(
            rng.RandfRange(lowX, highX),
            rng.RandfRange(lowY, highY));
        BiomeVegetation settings = Generator.PickBiome(centre, rng).Vegetation;
        float chance = Mathf.Clamp(
            (float)settings.PlantPatches / patchBudget, 0f, 1f);
        if (rng.Randf() >= chance) continue;

        for (int i = 0; i < Mathf.Max(0, settings.PlantsPerPatch); i++)
        {
            yield return ChunkBuildStage.Plants;
            Vector2 tile = centre + new Vector2(
                rng.RandfRange(-1.8f, 1.8f),
                rng.RandfRange(-1.8f, 1.8f));
            if (tile.X < lowX || tile.X >= highX ||
                tile.Y < lowY || tile.Y >= highY)
                continue;

            PlantDefinition definition =
                BiomeSpecies.Select<PlantDefinition>(settings.Plants, rng);
            if (definition == null) continue;

            float size = definition.RollSize(rng);
            Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

            if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared)
                continue;
            if (!ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size))
                continue;
            if (WorldPlacement.IsBlocked(
                objects, globalPoint, Vector2.Zero,
                obstacles, new Vector2(32, 24)))
                continue;
            if (WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size))
                continue;

            Plant plant = new()
            {
                Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{patch}_{i}",
                Position = objects.ToLocal(globalPoint),
                Definition = definition,
                Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
                SizeMultiplier = size,
                Mirror = definition.RollMirror(rng)
            };

            objects.AddChild(plant);
            plants.Add(plant);
            placed.Add(localPoint);
        }
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
