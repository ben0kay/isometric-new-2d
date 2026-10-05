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
// Mix biome grass densities and species across smooth transition areas.
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
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0x6A55u);
    float lowX = coordinate.X * chunkSize - 0.5f;
    float lowY = coordinate.Y * chunkSize - 0.5f;
    float highX = lowX + chunkSize, highY = lowY + chunkSize;

    int patchBudget = Generator.MaxGrassPatches;
    for (int patch = 0; patch < patchBudget; patch++)
    {
        yield return ChunkBuildStage.Grass;
        Vector2 centre = new(
            rng.RandfRange(lowX, highX), rng.RandfRange(lowY, highY));
        BiomeVegetation settings = Generator.PickBiome(centre, rng).Vegetation;
        float chance = Mathf.Clamp(
            (float)settings.GrassPatches / patchBudget, 0f, 1f);
        if (rng.Randf() >= chance) continue;

        for (int tuft = 0; tuft < Mathf.Max(0, settings.GrassTuftsPerPatch); tuft++)
        {
            yield return ChunkBuildStage.Grass;
            Vector2 tile = centre + new Vector2(
                rng.RandfRange(-1.3f, 1.3f), rng.RandfRange(-1.3f, 1.3f));
            if (tile.X < lowX || tile.X >= highX ||
                tile.Y < lowY || tile.Y >= highY) continue;

            GrassDefinition definition =
                BiomeSpecies.Select<GrassDefinition>(settings.Grass, rng);
            if (definition == null) continue;

            float size = definition.RollSize(rng);
            Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
            if (globalPoint.DistanceSquaredTo(spawnPoint) < 72f * 72f) continue;
            if (!ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size)) continue;
            if (WorldPlacement.IsBlocked(
                globalPoint, Vector2.Zero, obstacles, new Vector2(20, 12))) continue;
            if (WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size)) continue;

            Grass grass = new()
            {
                Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{patch}_{tuft}",
                Position = objects.ToLocal(globalPoint),
                Definition = definition,
                Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
                SizeMultiplier = size,
                Mirror = definition.RollMirror(rng)
            };
            objects.AddChild(grass);
            tufts.Add(grass);
            placed.Add(localPoint);
        }
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
