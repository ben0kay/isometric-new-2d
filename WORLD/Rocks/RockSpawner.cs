// Generates solid rocks from the local biome's weighted rock definitions.
// A plain helper Node places artwork under the existing shared Y-sort root.
using Godot;
using System.Collections.Generic;

public partial class RockSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region Generation
    // =========================================================
    // Place locally selected rock types using a bounded spawning budget.
    public void Populate(
        Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
        Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
        float spawnClearRadius, List<Obstacle> owned)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed ^ 0xB041u);

        float lowX = coordinate.X * chunkSize - 0.5f;
        float lowY = coordinate.Y * chunkSize - 0.5f;
        float clearSquared = spawnClearRadius * spawnClearRadius;
        int budget = Generator.MaxRocks * 4;

        for (int attempt = 0; attempt < budget; attempt++)
        {
            Vector2 tile = new(
                rng.RandfRange(lowX, lowX + chunkSize),
                rng.RandfRange(lowY, lowY + chunkSize));
            BiomeDefinition biome = Generator.GetBiome(tile);
            float chance = Mathf.Clamp(
                (float)biome.RocksPerChunk / budget, 0f, 1f);
            if (rng.Randf() >= chance) continue;

            RockDefinition definition = Select(biome, rng);
            if (definition == null) continue;

            float minWidth = Mathf.Max(16f, definition.WidthRange.X);
            float maxWidth = Mathf.Max(minWidth, definition.WidthRange.Y);
            float minHeight = Mathf.Max(8f, definition.HeightRange.X);
            float maxHeight = Mathf.Max(minHeight, definition.HeightRange.Y);
            float width = rng.RandfRange(minWidth, maxWidth);
            float height = rng.RandfRange(minHeight, maxHeight);
            Vector2 footprint = new(
                width, width * Mathf.Max(0.1f, definition.FootprintDepthRatio));

            Vector2 localPoint = IsoGrid.TileToWorld(tile, tileSize);
            Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
            if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared) continue;
            if (!ChasmFeature.HasGroundClearance(
                localPoint, tileSize, footprint.Length() * 0.5f + 8f)) continue;
            if (!CanPlace(objects, globalPoint, footprint)) continue;

            Obstacle rock = new()
            {
                Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{attempt}",
                Position = objects.ToLocal(globalPoint),
                Kind = Obstacle.ObstacleKind.Rock,
                Footprint = footprint,
                Height = height,
                RockVariant = rng.RandiRange(0, RockDrawing.VariantCount - 1),
                VisualOverride = definition.Visual
            };
            objects.AddChild(rock);
            owned.Add(rock);
        }
    }
    #endregion

    #region Placement Helpers
    // =========================================================
    // Choose a rock resource using its non-negative relative weight.
    private static RockDefinition Select(
        BiomeDefinition biome, RandomNumberGenerator rng)
    {
        float total = 0f;
        foreach (RockDefinition rock in biome.Rocks)
            if (rock != null) total += Mathf.Max(0f, rock.Weight);
        if (total <= 0f) return null;

        float roll = rng.Randf() * total;
        RockDefinition last = null;
        foreach (RockDefinition rock in biome.Rocks)
        {
            if (rock == null || rock.Weight <= 0f) continue;
            last = rock;
            roll -= rock.Weight;
            if (roll <= 0f) return rock;
        }
        return last;
    }

    // =========================================================
    // Keep solid footprints apart, including obstacles in neighbouring chunks.
    public static bool CanPlace(
        Node2D objects, Vector2 point, Vector2 footprint)
    {
        foreach (Node node in objects.GetChildren())
        {
            if (node is not Obstacle obstacle || obstacle.IsQueuedForDeletion())
                continue;

            Vector2 difference = point - obstacle.GlobalPosition;
            Vector2 separation = (footprint + obstacle.Footprint) * 0.5f
                + new Vector2(12, 8);
            if (Mathf.Abs(difference.X) < separation.X &&
                Mathf.Abs(difference.Y) < separation.Y) return false;
        }
        return true;
    }
    #endregion
}