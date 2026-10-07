// Searches a configurable area for a requested biome without finite world bounds.
// Metadata preparation and safety checks share the existing search frame budget.
using Godot;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

public static class DebugBiomeSpawnSearch
{
    #region Search
    // =========================================================
    // Search seeded positions within a debug search radius, not a world boundary.
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

        float radius = WorldConfig.Find(world).DebugBiomeSearchRadiusTiles;
        if (!float.IsFinite(radius) || radius < 128f)
            throw new InvalidOperationException(
                "DebugBiomeSearchRadiusTiles must be at least 128.");

        int side = Mathf.Clamp(Mathf.CeilToInt(
            radius * 2f / Mathf.Max(2f, world.SearchSpacingTiles)), 1, 256);
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

        float gradient = Mathf.Sqrt(
            1f / (chunks.TileSize.X * chunks.TileSize.X) +
            1f / (chunks.TileSize.Y * chunks.TileSize.Y));
        int reach = Mathf.Max(2,
            Mathf.CeilToInt(chunks.SpawnClearRadius * gradient) + 1);

        WaterBasinWorld basins = WaterBasinWorld.Find(world);
        InfiniteWorldGeneration generation =
            InfiniteWorldGeneration.Find(world);
        Label status = world.GetNode<Label>("HUD/ChunkInfo");

        double budget = Math.Clamp(world.SearchBudgetMs, 0.2, 5.0);
        long started = Stopwatch.GetTimestamp();

        // =========================================================
        // Yield the shared search budget and stop if the scene has departed.
        async Task<bool> ContinueSearch(int checkedCount)
        {
            if (!world.IsInsideTree() || world.IsQueuedForDeletion())
                return false;
            if (ElapsedMs(started) < budget) return true;

            status.Text =
                $"Finding spawn in {world.RequestedBiomeId}...\n" +
                $"Seed {chunks.WorldSeed} | Checked {checkedCount}/{count}";

            await world.ToSignal(
                world.GetTree(), SceneTree.SignalName.ProcessFrame);
            started = Stopwatch.GetTimestamp();

            return world.IsInsideTree() && !world.IsQueuedForDeletion();
        }

        for (int i = 0; i < count; i++)
        {
            if (!await ContinueSearch(i + 1)) return null;

            int cell = order[i];
            Vector2 tile = new(
                -radius + ((cell % side) + 0.5f) / side * radius * 2f,
                -radius + ((cell / side) + 0.5f) / side * radius * 2f);

            if (generator.GetBiome(tile).Id != world.RequestedBiomeId)
                continue;

                            using IDisposable candidateProtection = generation?.PinArea(
                new Rect2(
                    tile - Vector2.One * reach,
                    Vector2.One * (reach * 2f)));

            if (generation != null)
                foreach (int step in generation.PrepareArea(new Rect2(
                    tile - Vector2.One * reach,
                    Vector2.One * (reach * 2f))))
                    if (!await ContinueSearch(i + 1)) return null;

            bool valid = true;
            for (int y = -reach; y <= reach && valid; y++)
            for (int x = -reach; x <= reach && valid; x++)
            {
                valid = IsSafeSample(
                    generator, basins, tile + new Vector2(x, y),
                    world.RequestedBiomeId);
                if (!await ContinueSearch(i + 1)) return null;
            }

            if (valid) return tile;
        }

        return null;
    }
    #endregion

    #region Checks
    // =========================================================
    // Reject biome borders, chasms, slopes, basin reservations and cave mouths.
    private static bool IsSafeSample(
        WorldGenerator generator, WaterBasinWorld basins,
        Vector2 tile, string biomeId)
    {
        if (generator.GetBiome(tile).Id != biomeId ||
            ChasmFeature.IsVoidTile(
                Mathf.FloorToInt(tile.X + 0.5f),
                Mathf.FloorToInt(tile.Y + 0.5f)) ||
            (basins != null && basins.Overlaps(tile, 1f)))
            return false;

        Node2D ground = generator.GetNode<Node2D>("../../GroundChunks");
        ChunkController chunks =
            generator.GetNode<ChunkController>("../ChunkController");
        Vector2 point = ground.ToGlobal(
            IsoGrid.TileToWorld(tile, chunks.TileSize));

        return TerrainSlopeWorld.Ensure(generator).HasClearance(point, 12f) &&
            !CaveWorld.IsHoleReserved(
                generator, point, Vector2.One * 24f, Vector2.Zero);
    }

    // =========================================================
    // Measure elapsed work without allocating stopwatch instances.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion
}