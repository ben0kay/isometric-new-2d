// Generates loose grass patches and removes them with their owning chunks.
// Uses an independent seed so grass does not change tree or shrub generation.
using Godot;
using System.Collections.Generic;

public partial class GrassSpawner : Node
{
#region Configuration
public WorldGenerator Generator { get; set; }
#endregion
    #region State
    private readonly Dictionary<Vector2I, List<AlienGrass>> _grass = new();
    #endregion

    #region Generation
    
    // =========================================================
// Generate each biome's grass patches while retaining safe footprint spacing.
public void Populate(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint)
{
    if (_grass.ContainsKey(coordinate)) return;
    List<AlienGrass> tufts = new();
    List<Vector2> placed = new();
    List<Obstacle> obstacles = new();
    _grass.Add(coordinate, tufts);

    foreach (Node node in objects.GetChildren())
        if (node is Obstacle obstacle && !obstacle.IsQueuedForDeletion())
            obstacles.Add(obstacle);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0x6A55u);
    float lowX = coordinate.X * chunkSize - 0.5f;
    float lowY = coordinate.Y * chunkSize - 0.5f;
    float highX = lowX + chunkSize, highY = lowY + chunkSize;

    int patchBudget = Generator.MaxGrassPatches;
    for (int patch = 0; patch < patchBudget; patch++)
    {
        Vector2 centre = new(
            rng.RandfRange(lowX, highX), rng.RandfRange(lowY, highY));
        BiomeDefinition biome = Generator.GetBiome(centre);
        BiomeVegetation settings = biome.Vegetation;
        float chance = Mathf.Clamp(
            (float)settings.GrassPatches / patchBudget, 0f, 1f);
        if (rng.Randf() >= chance) continue;

        float tallChance = Mathf.Clamp(settings.TallGrassChance, 0f, 1f);
        float mediumChance = Mathf.Clamp(
            settings.MediumGrassChance, 0f, 1f - tallChance);

        for (int tuft = 0; tuft < Mathf.Max(0, settings.GrassTuftsPerPatch); tuft++)
        {
            Vector2 tile = centre + new Vector2(
                rng.RandfRange(-1.3f, 1.3f), rng.RandfRange(-1.3f, 1.3f));
            if (tile.X < lowX || tile.X >= highX ||
                tile.Y < lowY || tile.Y >= highY) continue;
            if (Generator.GetBiome(tile) != biome) continue;

            Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
            if (globalPoint.DistanceSquaredTo(spawnPoint) < 72f * 72f) continue;
            if (!ChasmFeature.HasGroundClearance(localPoint, tileSize, 28f)) continue;
            if (Blocked(globalPoint, obstacles)) continue;

            bool crowded = false;
            foreach (Vector2 existing in placed)
            {
                Vector2 d = localPoint - existing;
                if (d.X * d.X / (25f * 25f) + d.Y * d.Y / (14f * 14f) >= 1f)
                    continue;
                crowded = true;
                break;
            }
            if (crowded) continue;

            float roll = rng.Randf();
            GrassHeight height = roll < tallChance ? GrassHeight.Tall
                : roll < tallChance + mediumChance ? GrassHeight.Medium
                : GrassHeight.Short;
            VisualDefinition visual = height == GrassHeight.Tall
                ? settings.TallGrassVisual
                : height == GrassHeight.Medium
                    ? settings.MediumGrassVisual : settings.ShortGrassVisual;

            AlienGrass grass = new()
            {
                Name = $"Grass_{coordinate.X}_{coordinate.Y}_{patch}_{tuft}",
                Position = objects.ToLocal(globalPoint),
                HeightKind = height,
                Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
                SizeMultiplier = rng.RandfRange(0.8f, 1.15f),
                VisualOverride = visual
            };
            objects.AddChild(grass);
            tufts.Add(grass);
            placed.Add(localPoint);
        }
    }
}

    // =========================================================
    // Keep tuft bases clear of rock, crate and tree trunk footprints.
    private static bool Blocked(Vector2 point, List<Obstacle> obstacles)
    {
        foreach (Obstacle obstacle in obstacles)
        {
            Vector2 offset = obstacle.ToLocal(point);
            Vector2 half = obstacle.Footprint * 0.5f + new Vector2(20, 12);
            if (Mathf.Abs(offset.X) < half.X && Mathf.Abs(offset.Y) < half.Y)
                return true;
        }
        return false;
    }

    // =========================================================
    // Release grass belonging to one unloaded chunk.
    public void RemoveChunk(Vector2I coordinate)
    {
        if (!_grass.TryGetValue(coordinate, out List<AlienGrass> tufts)) return;
        foreach (AlienGrass grass in tufts)
            if (GodotObject.IsInstanceValid(grass)) grass.QueueFree();
        _grass.Remove(coordinate);
    }
    #endregion
}