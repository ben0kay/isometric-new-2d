// Produces deterministic biome-controlled positions for vegetation and rocks.
// Keeps placement policy shared while individual spawners handle their own actors.
using Godot;
using System;
using System.Collections.Generic;

public enum BiomePopulationFamily
{
    Trees,
    Plants,
    Grass,
    Rocks
}

public readonly struct BiomeScatterCandidate
{
    public readonly Vector2 Tile;
    public readonly BiomeDefinition Biome;
    public readonly int Index;

    // =========================================================
    // Carry a candidate and its owning biome; null biome means rejected work.
    public BiomeScatterCandidate(
        Vector2 tile, BiomeDefinition biome, int index)
    {
        Tile = tile;
        Biome = biome;
        Index = index;
    }
}

public static class BiomeScatter
{
    #region Recipes
    // =========================================================
    // Read the placement resource belonging to the requested family.
    private static BiomePlacementSettings Settings(
        BiomeDefinition biome, BiomePopulationFamily family)
    {
        return family switch
        {
            BiomePopulationFamily.Trees => biome.Vegetation.TreesPlacement,
            BiomePopulationFamily.Plants => biome.Vegetation.PlantsPlacement,
            BiomePopulationFamily.Grass => biome.Vegetation.GrassPlacement,
            BiomePopulationFamily.Rocks => biome.RocksPlacement,
            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };
    }

    // =========================================================
    // Preserve existing counts as the family's base placement frequency.
    private static double Frequency(
        BiomeDefinition biome, BiomePopulationFamily family)
    {
        BiomeVegetation vegetation = biome.Vegetation;

        double baseline = family switch
        {
            BiomePopulationFamily.Trees =>
                Math.Max(0, vegetation.TreesPerChunk),

            BiomePopulationFamily.Plants =>
                (double)Math.Max(0, vegetation.PlantPatches) *
                Math.Max(0, vegetation.PlantsPerPatch),

            BiomePopulationFamily.Grass =>
                (double)Math.Max(0, vegetation.GrassPatches) *
                Math.Max(0, vegetation.GrassTuftsPerPatch),

            BiomePopulationFamily.Rocks =>
                Math.Max(0, biome.RocksPerChunk),

            _ => throw new ArgumentOutOfRangeException(nameof(family))
        };

        return baseline * Settings(biome, family).FrequencyMultiplier;
    }
    #endregion

    #region Generation
    // =========================================================
    // Yield budgeted placement work using each local biome's frequency and spread.
    public static IEnumerable<BiomeScatterCandidate> Generate(
        WorldGenerator generator, BiomePopulationFamily family,
        Vector2I coordinate, int chunkSize, uint seed)
    {
        double maximum = 0;

        foreach (BiomeDefinition biome in generator.Catalog.GetEnabledBiomes())
        {
            BiomePlacementSettings settings = Settings(biome, family);
            if (settings == null)
                throw new InvalidOperationException(
                    $"{biome.Id}/{family}: placement settings are missing.");

            settings.Validate($"{biome.Id}/{family}");
            maximum = Math.Max(maximum, Frequency(biome, family));
        }

        if (maximum <= 0) yield break;
        if (!double.IsFinite(maximum) || maximum > int.MaxValue - 1)
            throw new InvalidOperationException(
                $"{family}: placement frequency is too large.");

        int budget = (int)Math.Ceiling(maximum);
        float lowX = coordinate.X * chunkSize - 0.5f;
        float lowY = coordinate.Y * chunkSize - 0.5f;

        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, seed);

        for (int attempt = 0; attempt < budget; attempt++)
        {
            Vector2 tile = new(
                rng.RandfRange(lowX, lowX + chunkSize),
                rng.RandfRange(lowY, lowY + chunkSize));

            BiomeDefinition biome = generator.PickBiome(tile, rng);
            double chance = Frequency(biome, family) / budget;

            if (rng.Randf() >= chance)
            {
                yield return default;
                continue;
            }

            BiomePlacementSettings settings = Settings(biome, family);

            if (settings.Distribution == BiomeDistribution.Clumps)
            {
                tile = ClumpPoint(
                    coordinate, chunkSize, seed, settings, rng);

                // A clump must not move this biome's species into another biome.
                if (generator.GetBiome(tile).Id != biome.Id)
                {
                    yield return default;
                    continue;
                }
            }

            yield return new BiomeScatterCandidate(tile, biome, attempt);
        }
    }

    // =========================================================
    // Reuse seeded clump centres and scatter members inside their owning chunk.
    private static Vector2 ClumpPoint(
        Vector2I coordinate, int chunkSize, uint seed,
        BiomePlacementSettings settings, RandomNumberGenerator rng)
    {
        float radius = Mathf.Min(
            settings.ClumpRadiusTiles, chunkSize * 0.45f);

        float lowX = coordinate.X * chunkSize - 0.5f;
        float lowY = coordinate.Y * chunkSize - 0.5f;
        int index = rng.RandiRange(0, settings.ClumpsPerChunk - 1);

        uint clusterSeed = IsoGrid.Hash(
            coordinate.X, coordinate.Y, seed ^ 0x51C9u);

        float unitX = Unit(IsoGrid.Hash(index, 0, clusterSeed));
        float unitY = Unit(IsoGrid.Hash(index, 1, clusterSeed));

        Vector2 centre = new(
            Mathf.Lerp(lowX + radius, lowX + chunkSize - radius, unitX),
            Mathf.Lerp(lowY + radius, lowY + chunkSize - radius, unitY));

        float angle = rng.Randf() * Mathf.Tau;
        float distance = Mathf.Sqrt(rng.Randf()) * radius;
        return centre + Vector2.Right.Rotated(angle) * distance;
    }

    // =========================================================
    // Convert a deterministic hash to a value below one.
    private static float Unit(uint value)
    {
        return (value & 65535u) / 65536f;
    }
    #endregion
}