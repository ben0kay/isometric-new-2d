// Generates deterministic vegetation patches and removes them with their chunks.
// A plain Node manages ownership while individual plants share WorldObjects Y-sorting.
using Godot;
using System.Collections.Generic;

public partial class VegetationSpawner : Node
{
    #region Configuration
    public int PatchesPerChunk { get; set; } = 3;
    public int PlantsPerPatch { get; set; } = 4;
    public VisualDefinition FrondVisual { get; set; }
    public VisualDefinition ShrubVisual { get; set; }
    #endregion

    #region State
    private readonly Dictionary<Vector2I, List<Vegetation>> _plants = new();
    #endregion

    #region Generation
// =========================================================
// Generate loose patches with separated bases and consistent baked lighting.
public void Populate(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius, List<Obstacle> obstacles)
{
    if (_plants.ContainsKey(coordinate)) return;
    List<Vegetation> plants = new();
    List<Vector2> placed = new();
    _plants.Add(coordinate, plants);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0x7A93u);

    float lowX = coordinate.X * chunkSize - 0.5f;
    float lowY = coordinate.Y * chunkSize - 0.5f;
    float highX = lowX + chunkSize;
    float highY = lowY + chunkSize;

    for (int patch = 0; patch < PatchesPerChunk; patch++)
    {
        Vector2 centre = new(
            rng.RandfRange(lowX, highX),
            rng.RandfRange(lowY, highY));

        for (int i = 0; i < PlantsPerPatch; i++)
        {
            Vector2 tile = centre + new Vector2(
                rng.RandfRange(-1.8f, 1.8f),
                rng.RandfRange(-1.8f, 1.8f));
            if (tile.X < lowX || tile.X >= highX ||
                tile.Y < lowY || tile.Y >= highY) continue;

            Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
            if (globalPoint.DistanceSquaredTo(spawnPoint)
                < spawnClearRadius * spawnClearRadius) continue;
            if (!TerrainLayout.HasGroundClearance(
                localPoint, tileSize, 48f)) continue;
            if (OverlapsObstacle(globalPoint, obstacles)) continue;

            bool crowded = false;
            foreach (Vector2 existing in placed)
            {
                Vector2 difference = localPoint - existing;
                float separation =
                    difference.X * difference.X / (88f * 88f)
                    + difference.Y * difference.Y / (48f * 48f);
                if (separation >= 1f) continue;
                crowded = true;
                break;
            }
            if (crowded) continue;

            bool shrub = rng.Randf() < 0.35f;
            Vegetation plant = new()
            {
                Name = $"Plant_{coordinate.X}_{coordinate.Y}_{patch}_{i}",
                Position = objects.ToLocal(globalPoint),
                Shrub = shrub,
                Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
                SizeMultiplier = shrub
                    ? rng.RandfRange(0.65f, 0.95f)
                    : rng.RandfRange(0.6f, 1.05f),
                Mirror = false,
                VisualOverride = shrub ? ShrubVisual : FrondVisual
            };
            objects.AddChild(plant);
            plants.Add(plant);
            placed.Add(localPoint);
        }
    }
}

    // =========================================================
    // Keep plant bases outside the generated obstacle footprints.
    private static bool OverlapsObstacle(Vector2 point, List<Obstacle> obstacles)
    {
        foreach (Obstacle obstacle in obstacles)
        {
            Vector2 offset = obstacle.ToLocal(point);
            Vector2 half = obstacle.Footprint * 0.5f + new Vector2(32, 24);
            if (Mathf.Abs(offset.X) < half.X &&
                Mathf.Abs(offset.Y) < half.Y) return true;
        }
        return false;
    }

    // =========================================================
    // Release the vegetation belonging to one unloaded chunk.
    public void RemoveChunk(Vector2I coordinate)
    {
        if (!_plants.TryGetValue(coordinate, out List<Vegetation> plants)) return;
        foreach (Vegetation plant in plants)
            if (GodotObject.IsInstanceValid(plant)) plant.QueueFree();
        _plants.Remove(coordinate);
    }
    #endregion
}