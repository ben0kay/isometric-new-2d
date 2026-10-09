// Provides reusable organic chambers, curved passages and floor height sampling.
// Specialized cave biomes override only their distinctive shape behaviour.
using Godot;

public class CaveTerrainGenerator
{
    #region State
    public CaveBiomeDefinition Definition { get; }
    private readonly uint _seed;
    private readonly FastNoiseLite _noise;
    #endregion

    #region Construction
    // =========================================================
    // Create one reusable noise sampler for this biome.
    public CaveTerrainGenerator(CaveBiomeDefinition definition, uint seed)
    {
        Definition = definition;
        _seed = seed;
        _noise = new FastNoiseLite
        {
            Seed = unchecked((int)seed),
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.12f
        };
    }
    #endregion

    #region Shape Sampling
    // =========================================================
    // Return a signed chamber boundary: negative values are inside the chamber.
    public virtual float ChamberDistance(
        Vector2 point, Vector2 centre, int x, int y)
    {
        Vector2 range = Definition.ChamberRadiusRange;
        Vector2 radius = new(
            Mathf.Lerp(range.X, range.Y, Random(x, y, 3)),
            Mathf.Lerp(range.X, range.Y, Random(x, y, 4)));

        Vector2 delta = point - centre;
        float distance = ((delta / radius).Length() - 1f) *
            Mathf.Min(radius.X, radius.Y);

        // Avoid noise work for samples clearly outside the boundary.
        if (distance > Definition.WallIrregularityTiles)
            return distance;

        return distance - _noise.GetNoise2D(point.X, point.Y) *
            Definition.WallIrregularityTiles;
    }

    // =========================================================
    // Bend a passage while keeping both room connections fixed.
    public virtual float PassageOffset(float t, uint edgeSeed)
    {
        float phase = (edgeSeed & 65535u) / 65535f * Mathf.Tau;
        return Mathf.Sin(Mathf.Pi * t) *
            Mathf.Sin(Mathf.Tau * t + phase) * Definition.WindingTiles;
    }

    // =========================================================
    // Supply optional gentle floor variation independently of surface height.
    public virtual float FloorHeight(Vector2 tile)
    {
        if (Definition.FloorAmplitude <= 0f) return 0f;

        float scale = 1f /
            (Definition.FloorFeatureSizeTiles * 0.12f);

        return _noise.GetNoise2D(tile.X * scale, tile.Y * scale) *
            Definition.FloorAmplitude;
    }
    #endregion

    #region Seeded Variation
    // =========================================================
    // Choose repeatable chamber dimensions without allocating random generators.
    protected float Random(int x, int y, uint salt)
    {
        uint hash = IsoGrid.Hash(x, y, _seed ^ salt);
        return (hash & 0xFFFFFFu) / 16777216f;
    }
    #endregion
}