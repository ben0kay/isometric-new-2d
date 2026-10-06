// Finds a requested biome's starting area without spawning world chunks.
// Checks biome identity, world bounds, chasms and permanent water basins.
using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

public static class DebugBiomeSpawnSearch
{
    #region Search
    // =========================================================
    // Search deterministic candidate positions under a small frame budget.
    public static async Task<Vector2?> Find(
        DebugBiomeWorld world, ChunkController chunks)
    {
        WorldGenerator generator =
            world.GetNode<WorldGenerator>("Systems/WorldGenerator");

        bool enabled = false;
        foreach (BiomeDefinition biome in generator.Catalog.GetEnabledBiomes())
            if (biome.Id == world.RequestedBiomeId) enabled = true;

        if (!enabled)
            throw new InvalidOperationException(
                $"Biome '{world.RequestedBiomeId}' is missing or disabled.");

        float span = chunks.WorldChunksPerAxis * chunks.ChunkSize;
        float minimum = -(chunks.WorldChunksPerAxis / 2)
            * chunks.ChunkSize - 0.5f;
        float maximum = minimum + span;

        // Convert the spawn-clear radius to a conservative tile-space margin.
        float gradient = Mathf.Sqrt(
            1f / (chunks.TileSize.X * chunks.TileSize.X) +
            1f / (chunks.TileSize.Y * chunks.TileSize.Y));

        int reach = Mathf.Max(2, Mathf.CeilToInt(
            chunks.SpawnClearRadius * gradient) + 1);

        float margin = reach + 1f;
        float usable = span - margin * 2f;
        if (usable <= 0f) return null;

        int side = Mathf.Clamp(
            Mathf.CeilToInt(usable /
                Mathf.Max(2f, world.SearchSpacingTiles)), 1, 256);
        int count = side * side;

        int[] order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;

        using RandomNumberGenerator rng = new();
        rng.Seed = chunks.WorldSeed ^ 0xB10Eu;

        for (int i = count - 1; i > 0; i--)
        {
            int j = rng.RandiRange(0, i);
            (order[i], order[j]) = (order[j], order[i]);
        }

        WaterBasinWorld basins = WaterBasinWorld.Find(world);
        Label status = world.GetNode<Label>("HUD/ChunkInfo");

        double budget = Math.Clamp(world.SearchBudgetMs, 0.2, 5.0);
        long started = Stopwatch.GetTimestamp();

        for (int i = 0; i < count; i++)
        {
            if (!world.IsInsideTree() || world.IsQueuedForDeletion())
                return null;

            int cell = order[i];
            Vector2 tile = new(
                minimum + margin + ((cell % side) + 0.5f) / side * usable,
                minimum + margin + ((cell / side) + 0.5f) / side * usable);

            if (generator.GetBiome(tile).Id == world.RequestedBiomeId)
            {
                bool valid = true;

                for (int y = -reach; y <= reach && valid; y++)
                for (int x = -reach; x <= reach && valid; x++)
                {
                    Vector2 point = tile + new Vector2(x, y);

                    if (!IsSafeSample(
                        generator, basins, point,
                        world.RequestedBiomeId, minimum, maximum))
                        valid = false;

                    if (ElapsedMs(started) >= budget)
                    {
                        status.Text =
                            $"Finding spawn in {world.RequestedBiomeId}...\n" +
                            $"Seed {chunks.WorldSeed} | Checked {i + 1}/{count}";

                        await world.ToSignal(
                            world.GetTree(), SceneTree.SignalName.ProcessFrame);

                        if (!world.IsInsideTree() ||
                            world.IsQueuedForDeletion())
                            return null;

                        started = Stopwatch.GetTimestamp();
                    }
                }

                if (valid)
                {
                    GD.Print(
                        $"[BiomeTest] Seed {chunks.WorldSeed}; " +
                        $"{world.RequestedBiomeId}; spawn tile {tile}");

                    return tile;
                }
            }

            if (ElapsedMs(started) >= budget)
            {
                status.Text =
                    $"Finding spawn in {world.RequestedBiomeId}...\n" +
                    $"Seed {chunks.WorldSeed} | Checked {i + 1}/{count}";

                await world.ToSignal(
                    world.GetTree(), SceneTree.SignalName.ProcessFrame);

                if (!world.IsInsideTree() || world.IsQueuedForDeletion())
                    return null;

                started = Stopwatch.GetTimestamp();
            }
        }

        return null;
    }
    #endregion

    #region Checks
    // =========================================================
    // Reject biome borders, chasms, basin footprints and world edges.
    private static bool IsSafeSample(
        WorldGenerator generator, WaterBasinWorld basins, Vector2 point,
        string biomeId, float minimum, float maximum)
    {
        if (point.X < minimum || point.Y < minimum ||
            point.X >= maximum || point.Y >= maximum)
            return false;

        if (generator.GetBiome(point).Id != biomeId)
            return false;

        if (ChasmFeature.IsVoidTile(
            Mathf.FloorToInt(point.X + 0.5f),
            Mathf.FloorToInt(point.Y + 0.5f)))
            return false;

        if (basins != null)
        {
            foreach (WaterBasinWorld.Basin basin in basins.Basins)
            {
                float clearance = basin.Extent + 1f;
                if (point.DistanceSquaredTo(basin.Centre) <
                    clearance * clearance)
                    return false;
            }
        }

        return true;
    }

    // =========================================================
    // Measure the shared search budget without allocating a stopwatch.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000.0 /
            Stopwatch.Frequency;
    }
    #endregion
}