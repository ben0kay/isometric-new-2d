// Selects large climate-compatible biome areas with gently warped boundaries.
// Supports natural placement, comparison bands and single-biome testing.
using Godot;
using System;
using System.Collections.Generic;

public enum BiomePlacementMode { Natural, Comparison, Single }

public sealed class BiomeSampler
{
    #region Site Data
    private readonly struct Site
    {
        public readonly Vector2 Centre;
        public readonly int BiomeIndex;

        // =========================================================
        // Store a deterministic biome centre and its selected definition index.
        public Site(Vector2 centre, int biomeIndex)
        {
            Centre = centre;
            BiomeIndex = biomeIndex;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 4096;
    private readonly Dictionary<Vector2I, Site> _sites = new();
    private readonly IReadOnlyList<BiomeDefinition> _biomes;
    private readonly uint _seed;
    private readonly BiomePlacementMode _mode;
    private readonly int _singleIndex;
    private readonly float _biomeSize, _transition, _warp;
    private readonly float _bandWidth, _bandBlend;

    public WorldClimate Climate { get; }
    #endregion

    #region Construction
    // =========================================================
    // Cache placement distances and prepare the broad climate sampler.
    public BiomeSampler(
        IReadOnlyList<BiomeDefinition> biomes, uint seed,
        BiomePlacementMode mode, int singleIndex,
        float regionSize, float biomeSize, float transition,
        float warpFraction, float bandWidth, float bandBlend)
    {
        _biomes = biomes;
        _seed = seed;
        _mode = mode;
        _singleIndex = singleIndex;
        _biomeSize = Mathf.Max(8f, biomeSize);
        _transition = Mathf.Clamp(transition, 0f, _biomeSize * 0.2f);
        _warp = Mathf.Clamp(warpFraction, 0f, 0.2f) * _biomeSize;
        _bandWidth = Mathf.Max(1f, bandWidth);
        _bandBlend = Mathf.Clamp(bandBlend, 0f, _bandWidth * 0.45f);
        Climate = new WorldClimate(
            seed, Mathf.Max(regionSize, _biomeSize * 2f));
    }
    #endregion

    #region Sampling
    // =========================================================
    // Return normalized biome influences for the selected placement mode.
    public BiomeBlend Sample(Vector2 tile)
    {
        if (_mode == BiomePlacementMode.Single || _biomes.Count == 1)
        {
            BiomeBlend single = new();
            single.Add(_singleIndex, 1f);
            single.Normalize();
            return single;
        }

        return _mode == BiomePlacementMode.Comparison
            ? SampleComparison(tile) : SampleNatural(tile);
    }

    // =========================================================
    // Blend nearby biome centres while preserving large single-biome interiors.
    private BiomeBlend SampleNatural(Vector2 tile)
    {
        Vector2 point = tile;
        if (_warp > 0f)
        {
            float featureSize = _biomeSize * 0.75f;
            point += new Vector2(
                RollingLayer.Sample(tile, _seed ^ 0x519u, 1f, featureSize) - 0.5f,
                RollingLayer.Sample(tile, _seed ^ 0xA27u, 1f, featureSize) - 0.5f)
                * (_warp * 2f);
        }

        int cellX = Mathf.FloorToInt(point.X / _biomeSize);
        int cellY = Mathf.FloorToInt(point.Y / _biomeSize);
        Span<Site> sites = stackalloc Site[9];
        Span<float> distances = stackalloc float[9];
        float nearest = float.MaxValue;
        int nearestSlot = 0, slot = 0;

        for (int y = cellY - 1; y <= cellY + 1; y++)
        for (int x = cellX - 1; x <= cellX + 1; x++)
        {
            Site site = GetSite(new Vector2I(x, y));
            float distance = point.DistanceTo(site.Centre);
            sites[slot] = site;
            distances[slot] = distance;
            if (distance < nearest)
            {
                nearest = distance;
                nearestSlot = slot;
            }
            slot++;
        }

        BiomeBlend blend = new();
        if (_transition <= 0f)
        {
            blend.Add(sites[nearestSlot].BiomeIndex, 1f);
        }
        else
        {
            for (int i = 0; i < 9; i++)
            {
                float weight = 1f - Mathf.SmoothStep(
                    0f, _transition, distances[i] - nearest);
                blend.Add(sites[i].BiomeIndex, weight);
            }
        }

        blend.Normalize();
        return blend;
    }

    // =========================================================
    // Retain repeating comparison bands with continuous terrain transitions.
    private BiomeBlend SampleComparison(Vector2 tile)
    {
        float position = tile.X - tile.Y;
        int band = Mathf.FloorToInt(
            (position + _bandWidth * 0.5f) / _bandWidth);
        float offset = position - band * _bandWidth;
        int a = Wrap(band), b = a;
        float weight = 0f, half = _bandWidth * 0.5f;

        if (_bandBlend > 0f && Mathf.Abs(offset) > half - _bandBlend)
        {
            b = Wrap(band + (offset >= 0f ? 1 : -1));
            weight = Mathf.SmoothStep(
                half - _bandBlend, half + _bandBlend, Mathf.Abs(offset));
        }

        BiomeBlend blend = new();
        blend.Add(a, 1f - weight);
        blend.Add(b, weight);
        blend.Normalize();
        return blend;
    }
    #endregion

    #region Site Generation
    // =========================================================
    // Cache coordinate-derived sites while keeping memory use bounded.
    private Site GetSite(Vector2I cell)
    {
        if (_sites.TryGetValue(cell, out Site site)) return site;

        Vector2 centre = new(
            (cell.X + 0.5f
                + (Unit(IsoGrid.Hash(cell.X, cell.Y, _seed ^ 0x331u)) - 0.5f) * 0.3f)
                * _biomeSize,
            (cell.Y + 0.5f
                + (Unit(IsoGrid.Hash(cell.X, cell.Y, _seed ^ 0x771u)) - 0.5f) * 0.3f)
                * _biomeSize);

        int index = SelectBiome(cell, Climate.Sample(centre));
        site = new Site(centre, index);
        if (_sites.Count >= CacheLimit) _sites.Clear();
        _sites.Add(cell, site);
        return site;
    }

    // =========================================================
    // Select only biomes eligible for this centre's temperature and moisture.
    private int SelectBiome(Vector2I cell, ClimateSample climate)
    {
        float total = 0f;
        for (int i = 0; i < _biomes.Count; i++)
            total += _biomes[i].GetClimateWeight(climate);

        if (total <= 0f)
            throw new InvalidOperationException(
                $"No biome fits climate at biome cell {cell}: "
                + $"temperature {climate.Temperature:F2}, moisture {climate.Moisture:F2}. "
                + "Add a compatible transitional biome or expand the climate ranges.");

        float roll = Unit(IsoGrid.Hash(
            cell.X, cell.Y, _seed ^ 0xC91u)) * total;
        int last = 0;
        for (int i = 0; i < _biomes.Count; i++)
        {
            float weight = _biomes[i].GetClimateWeight(climate);
            if (weight <= 0f) continue;
            last = i;
            roll -= weight;
            if (roll <= 0f) return i;
        }
        return last;
    }

    // =========================================================
    // Convert a coordinate hash into a stable value below one.
    private static float Unit(uint value)
    {
        return (value & 65535u) / 65536f;
    }

    // =========================================================
    // Keep comparison indices valid on either side of the world origin.
    private int Wrap(int index)
    {
        return (index % _biomes.Count + _biomes.Count) % _biomes.Count;
    }
    #endregion
}