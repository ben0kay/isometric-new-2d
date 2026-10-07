// Places seeded basin candidates using the local biome's resource profile.
// Runs before terrain caching and object placement.
using Godot;
using System;
using System.Collections.Generic;

public static class BiomeBasinGenerator
{
    #region Generation
    // =========================================================
    // Prepare basin records across the current finite world.
    public static void Generate(
        WorldGenerator generator, ChunkController chunks, Node2D ground,
        Func<WaterDefinition, Vector2, float, float,
            WaterBasinWorld.Basin> register)
    {
        WorldConfig config = WorldConfig.Find(generator);
        if (!config.GenerateBiomeBasins) return;

        float spacing = config.BasinCandidateSpacingTiles;
        if (!float.IsFinite(spacing) || spacing < 16f)
            throw new InvalidOperationException(
                "BasinCandidateSpacingTiles must be finite and at least 16.");

        float minimum = -(chunks.WorldChunksPerAxis / 2) *
            chunks.ChunkSize - 0.5f;
        float maximum = minimum +
            chunks.WorldChunksPerAxis * chunks.ChunkSize;

        int first = Mathf.FloorToInt(minimum / spacing);
        int last = Mathf.CeilToInt(maximum / spacing) - 1;

        Player player = generator.GetNode<Player>(
            "../../WorldObjects/Player");
        Vector2 spawn = IsoGrid.WorldToTile(
            ground.ToLocal(player.GlobalPosition), chunks.TileSize);

        HashSet<BiomeBasinProfile> validated = new();
        Dictionary<string, int> selected = new();
        Dictionary<string, int> accepted = new();

        using RandomNumberGenerator rng = new();

        for (int y = first; y <= last; y++)
        for (int x = first; x <= last; x++)
        {
            float left = Mathf.Max(minimum, x * spacing);
            float right = Mathf.Min(maximum, (x + 1) * spacing);
            float top = Mathf.Max(minimum, y * spacing);
            float bottom = Mathf.Min(maximum, (y + 1) * spacing);
            if (right <= left || bottom <= top) continue;

            rng.Seed = IsoGrid.Hash(x, y, chunks.WorldSeed ^ 0xBA51u);
            Vector2 anchor = new(
                rng.RandfRange(left, right),
                rng.RandfRange(top, bottom));

            BiomeDefinition biome = generator.GetBiome(anchor);
            BiomeBasinProfile profile =
                biome.GetFeature<BiomeBasinProfile>("basins");

            if (profile == null || !profile.Enabled) continue;
            if (validated.Add(profile)) profile.Validate(biome.Id);
            if (rng.Randf() >= profile.SpawnProbability) continue;

            selected.TryGetValue(biome.Id, out int count);
            selected[biome.Id] = count + 1;

            int index = rng.RandiRange(0, profile.Templates.Count - 1);
            WaterDefinition template = profile.Templates[index];
            float size = rng.RandfRange(
                profile.SizeMultiplierRange.X, profile.SizeMultiplierRange.Y);
            float rotation = profile.RandomRotation
                ? rng.RandfRange(0f, 360f) : template.RotationDegrees;

            WaterDefinition definition =
                profile.CreateDefinition(template, size, rotation);
            definition.Validate();

            float fill = rng.RandfRange(
                profile.InitialFillRange.X, profile.InitialFillRange.Y);
            float phase = rng.RandfRange(0f, Mathf.Tau);
            float extent = Mathf.Max(
                definition.RadiusTiles.X, definition.RadiusTiles.Y) * 1.1f;
            float reservation = extent + definition.ClearanceTiles;

            for (int attempt = 0; attempt < profile.PlacementAttempts; attempt++)
            {
                Vector2 centre = attempt == 0 ? anchor : new Vector2(
                    rng.RandfRange(left, right),
                    rng.RandfRange(top, bottom));

                if (centre.X - reservation < minimum ||
                    centre.Y - reservation < minimum ||
                    centre.X + reservation > maximum ||
                    centre.Y + reservation > maximum ||
                    centre.DistanceSquaredTo(spawn) <
                        (extent + 6f) * (extent + 6f))
                    continue;

                if (!FitsBiome(generator, centre, reservation, biome.Id))
                    continue;

                WaterBasinWorld.Basin basin =
                    register(definition, centre, phase, fill);
                if (basin == null) continue;

                accepted.TryGetValue(biome.Id, out int total);
                accepted[biome.Id] = total + 1;
                break;
            }
        }

        foreach (var pair in selected)
        {
            accepted.TryGetValue(pair.Key, out int total);
            GD.Print(
                $"[Basins] {pair.Key}: {pair.Value} selected cells, " +
                $"{total} accepted basins.");
        }

        if (selected.Count == 0)
            GD.Print("[Basins] No candidate cells passed biome probability rolls.");
    }
    #endregion

    #region Eligibility
    // =========================================================
    // Require the reserved footprint to remain inside its owning biome.
    private static bool FitsBiome(
        WorldGenerator generator, Vector2 centre, float radius, string biomeId)
    {
        int left = Mathf.FloorToInt((centre.X - radius) * 2f);
        int right = Mathf.CeilToInt((centre.X + radius) * 2f);
        int top = Mathf.FloorToInt((centre.Y - radius) * 2f);
        int bottom = Mathf.CeilToInt((centre.Y + radius) * 2f);

        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
            if (generator.GetBiome(new Vector2(x * 0.5f, y * 0.5f)).Id
                != biomeId)
                return false;

        return true;
    }
    #endregion
}