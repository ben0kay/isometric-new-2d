// Plans one deterministic basin cell without loading terrain or artwork.
// Reservations stay inside their owning cell, preventing order-dependent overlaps.
using Godot;
using System;
using System.Collections.Generic;

public static class BiomeBasinGenerator
{
    #region Generation
    // =========================================================
    // Evaluate one seeded cell under the caller's existing work budget.
    public static IEnumerable<int> PrepareCell(
        WorldGenerator generator, ChunkController chunks,
        Vector2 spawnTile, float spacing, Vector2I cell,
        Action<WaterBasinWorld.Basin> complete)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(cell.X, cell.Y, chunks.WorldSeed ^ 0xBA51u);

        Vector2 middle = new(
            (cell.X + 0.5f) * spacing,
            (cell.Y + 0.5f) * spacing);

        BiomeDefinition biome = generator.GetBiome(middle);
        BiomeBasinProfile profile =
            biome.GetFeature<BiomeBasinProfile>("basins");

        if (profile == null || !profile.Enabled)
        {
            complete(null);
            yield break;
        }

        float roll = rng.Randf();
        if (roll >= profile.SpawnProbability)
        {
            complete(null);
            yield break;
        }

        WaterDefinition template =
            profile.Templates[rng.RandiRange(0, profile.Templates.Count - 1)];

        float size = rng.RandfRange(
            profile.SizeMultiplierRange.X, profile.SizeMultiplierRange.Y);
        float rotation = profile.RandomRotation
            ? rng.RandfRange(0f, 360f) : template.RotationDegrees;
        float fill = rng.RandfRange(
            profile.InitialFillRange.X, profile.InitialFillRange.Y);
        float phase = rng.RandfRange(0f, Mathf.Tau);

        for (int attempt = 0; attempt < profile.PlacementAttempts; attempt++)
        {
            yield return 0;

            // Leave at least 40% of the cell width around its centre.
            Vector2 centre = middle + new Vector2(
                rng.RandfRange(-0.1f, 0.1f),
                rng.RandfRange(-0.1f, 0.1f)) * spacing;

            if (generator.GetBiome(centre).Id != biome.Id) continue;

            profile.SampleElevation(
                generator.GetBaseHeight(centre),
                out float probability, out float elevationSize);

            if (roll >= profile.SpawnProbability * probability) continue;

            WaterDefinition definition = profile.CreateDefinition(
                template, size, rotation, elevationSize);
            definition.Validate();

            float extent = Mathf.Max(
                definition.RadiusTiles.X, definition.RadiusTiles.Y) * 1.1f;
            float reservation = extent + definition.ClearanceTiles;

            if (reservation > spacing * 0.4f)
                throw new InvalidOperationException(
                    $"Biome '{biome.Id}' has a basin too large for its cell. " +
                    "Increase BasinCandidateSpacingTiles or reduce basin size.");

            if (centre.DistanceSquaredTo(spawnTile) <
                (extent + 6f) * (extent + 6f))
                continue;

            int left = Mathf.FloorToInt((centre.X - reservation) * 2f);
            int right = Mathf.CeilToInt((centre.X + reservation) * 2f);
            int top = Mathf.FloorToInt((centre.Y - reservation) * 2f);
            int bottom = Mathf.CeilToInt((centre.Y + reservation) * 2f);

            float lowest = float.PositiveInfinity;
            float highest = float.NegativeInfinity;
            bool valid = true;

            for (int y = top; y <= bottom && valid; y++)
            for (int x = left; x <= right && valid; x++)
            {
                yield return 0;
                Vector2 tile = new(x * 0.5f, y * 0.5f);

                if (generator.GetBiome(tile).Id != biome.Id ||
                    ChasmFeature.IsVoidTile(
                        Mathf.FloorToInt(tile.X + 0.5f),
                        Mathf.FloorToInt(tile.Y + 0.5f)))
                {
                    valid = false;
                    continue;
                }

                float height = generator.GetBaseHeight(tile);
                lowest = Mathf.Min(lowest, height);
                highest = Mathf.Max(highest, height);
                valid = highest - lowest <= definition.MaximumHeightVariation;
            }

            if (!valid) continue;

            complete(new WaterBasinWorld.Basin
            {
                Definition = definition,
                Centre = centre,
                Phase = phase,
                RimHeight = lowest,
                Fill = fill
            });
            yield break;
        }

        complete(null);
    }
    #endregion
}