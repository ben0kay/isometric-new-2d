// Generates seeded plateau areas with exactly flat centres and smooth shoulders.
// Can include a guaranteed sandbox plateau for easy inspection near spawn.
using Godot;

public sealed class PlateauFeature
{
    #region Settings
    private readonly bool _enabled, _testEnabled;
    private readonly float _spacing, _radius, _shoulder, _chance;
    private readonly Vector2 _testCentre;
    private readonly uint _seed;
    #endregion

    #region Construction
    // =========================================================
    // Copy validated biome settings into a reusable sampling feature.
    public PlateauFeature(
        BiomeDefinition biome, uint seed,
        bool testEnabled, Vector2 testCentre)
    {
        _enabled = biome.PlateausEnabled;
        _spacing = Mathf.Max(16f, biome.PlateauSpacing);
        _radius = Mathf.Clamp(biome.PlateauRadius, 1f, _spacing * 0.2f);
        _shoulder = Mathf.Clamp(
            biome.PlateauShoulder, 2f, _spacing * 0.2f);
        _chance = Mathf.Clamp(biome.PlateauChance, 0f, 1f);
        _seed = seed ^ 0x91A7u;
        _testEnabled = testEnabled;
        _testCentre = testCentre;
    }
    #endregion

    #region Sampling
    // =========================================================
    // Return full influence inside a plateau and smoothly fade across its shoulder.
    public float SampleWeight(Vector2 tile)
    {
        if (!_enabled) return 0f;
        float weight = _testEnabled ? Weight(tile, _testCentre) : 0f;
        if (weight >= 1f) return 1f;

        int cellX = Mathf.FloorToInt(tile.X / _spacing);
        int cellY = Mathf.FloorToInt(tile.Y / _spacing);

        // Nearby cells are sufficient for the bounded radius and centre offsets.
        for (int y = cellY - 1; y <= cellY + 1; y++)
        for (int x = cellX - 1; x <= cellX + 1; x++)
        {
            float chance = Unit(IsoGrid.Hash(x, y, _seed));
            if (chance >= _chance) continue;

            float offsetX = Unit(IsoGrid.Hash(x, y, _seed ^ 0xA31u));
            float offsetY = Unit(IsoGrid.Hash(x, y, _seed ^ 0xB71u));
            Vector2 centre = new(
                (x + 0.5f) * _spacing + (offsetX - 0.5f) * _spacing * 0.4f,
                (y + 0.5f) * _spacing + (offsetY - 0.5f) * _spacing * 0.4f);

            weight = Mathf.Max(weight, Weight(tile, centre));
            if (weight >= 1f) return 1f;
        }
        return weight;
    }

    // =========================================================
    // Preserve an exactly flat core and a continuous transition to rolling ground.
    private float Weight(Vector2 tile, Vector2 centre)
    {
        float distance = tile.DistanceTo(centre);
        if (distance <= _radius) return 1f;
        if (distance >= _radius + _shoulder) return 0f;
        return 1f - Mathf.SmoothStep(
            _radius, _radius + _shoulder, distance);
    }

    // =========================================================
    // Convert a coordinate hash into a deterministic value below one.
    private static float Unit(uint value)
    {
        return (value & 65535u) / 65536f;
    }
    #endregion
}