// Samples rolling Wilds terrain with seeded, locally elevated flat clearings.
// Keeps exact flat interiors and smooth transitions without changing ground shaders.
using Godot;
using System.Collections.Generic;

public class VerdigrisTerrainGenerator : TerrainGenerator
{
    #region Clearing Data
    private readonly struct Clearing
    {
        public readonly bool Enabled;
        public readonly Vector2 Centre;
        public readonly float Height;

        // =========================================================
        // Store one deterministic clearing's local elevation.
        public Clearing(bool enabled, Vector2 centre, float height)
        {
            Enabled = enabled;
            Centre = centre;
            Height = height;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 4096;
    private readonly Dictionary<Vector2I, Clearing> _clearings = new();
    private readonly Queue<Vector2I> _cacheOrder = new();

    private readonly uint _seed;
    private readonly float _rollingHeight, _rollingSize;
    private readonly float _coverage, _radius, _blendWidth, _spacing;
    #endregion

    #region Construction
    // =========================================================
    // Snapshot biome settings so each generation run remains consistent.
    public VerdigrisTerrainGenerator(VerdigrisWildsBiome biome, uint seed)
        : base(biome, seed, false, Vector2.Zero)
    {
        _seed = seed;
        _rollingHeight = biome.RollingHeight;
        _rollingSize = biome.RollingFeatureSize;
        _coverage = biome.FlatPatchCoverage;
        _radius = biome.FlatPatchSizeTiles;
        _blendWidth = biome.FlatPatchBlendWidthTiles;
        _spacing = (_radius + _blendWidth) * 2.5f;
    }
    #endregion

    #region Sampling
    // =========================================================
    // Blend gentle hills toward a constant elevation inside each clearing.
    public override float SampleHeight(Vector2 tile, out float plateauWeight)
    {
        // These clearings are not raised plateau features.
        plateauWeight = 0f;

        float rolling = RollingHeightAt(tile);
        if (_coverage <= 0f) return rolling;

        Vector2I cell = new(
            Mathf.FloorToInt(tile.X / _spacing),
            Mathf.FloorToInt(tile.Y / _spacing));

        Clearing clearing = GetClearing(cell);
        if (!clearing.Enabled) return rolling;

        float distance = tile.DistanceTo(clearing.Centre);
        if (distance <= _radius) return clearing.Height;
        if (distance >= _radius + _blendWidth) return rolling;

        float t = (distance - _radius) / _blendWidth;
        float smooth = t * t * (3f - 2f * t);
        return Mathf.Lerp(clearing.Height, rolling, smooth);
    }

    // =========================================================
    // Reuse the existing rolling terrain noise and height settings.
    private float RollingHeightAt(Vector2 tile)
    {
        return RollingLayer.Sample(
            tile, _seed, _rollingHeight, _rollingSize);
    }
    #endregion

    #region Clearing Cache
    // =========================================================
    // Derive each clearing from its coordinates, independently of chunk order.
    private Clearing GetClearing(Vector2I cell)
    {
        if (_clearings.TryGetValue(cell, out Clearing clearing))
            return clearing;

        bool enabled =
            Unit(IsoGrid.Hash(cell.X, cell.Y, _seed ^ 0xF1A7u)) < _coverage;

        // Keep the complete flat core and blend shoulder within this cell.
        float jitter = _spacing * 0.5f - (_radius + _blendWidth);

        Vector2 centre = new(
            (cell.X + 0.5f) * _spacing +
                (Unit(IsoGrid.Hash(
                    cell.X, cell.Y, _seed ^ 0x71C3u)) * 2f - 1f) * jitter,
            (cell.Y + 0.5f) * _spacing +
                (Unit(IsoGrid.Hash(
                    cell.X, cell.Y, _seed ^ 0x93B5u)) * 2f - 1f) * jitter);

        clearing = new Clearing(
            enabled, centre, enabled ? RollingHeightAt(centre) : 0f);

        while (_clearings.Count >= CacheLimit)
            _clearings.Remove(_cacheOrder.Dequeue());

        _clearings.Add(cell, clearing);
        _cacheOrder.Enqueue(cell);
        return clearing;
    }

    // =========================================================
    // Convert a coordinate hash into a stable value below one.
    private static float Unit(uint value)
    {
        return (value & 65535u) / 65536f;
    }
    #endregion
}