// Samples seeded mountain ridges with winding, flat valley corridors.
// Terrain and path surfaces share the same absolute-coordinate corridor mask.
using Godot;

public class MountainTerrainGenerator : TerrainGenerator
{
    #region State
    private readonly uint _seed;
    private readonly float _height, _size, _valley, _sharpness;
    private readonly float _spacing, _halfWidth, _shoulder;
    private readonly float _winding, _wavelength;
    private readonly Vector2 _phase;

    public override float HeightRange => _valley + _height;
    #endregion

    #region Construction
    // =========================================================
    // Snapshot the biome settings once instead of reading exports per sample.
    public MountainTerrainGenerator(
        RockyMountainsBiome biome, uint seed,
        bool testPlateau, Vector2 testCentre)
        : base(biome, seed, testPlateau, testCentre)
    {
        _seed = seed ^ 0xA731u;
        _height = biome.MountainHeight;
        _size = biome.MountainSizeTiles;
        _valley = biome.ValleyHeight;
        _sharpness = biome.RidgeSharpness;
        _spacing = biome.PathSpacingTiles;
        _halfWidth = biome.PathHalfWidthTiles;
        _shoulder = biome.PathShoulderTiles;
        _winding = biome.PathWindingTiles;
        _wavelength = biome.PathWavelengthTiles;

        _phase = new Vector2(
            (IsoGrid.Hash(17, 31, seed) & 65535u) / 65535f,
            (IsoGrid.Hash(43, 11, seed) & 65535u) / 65535f
        ) * _spacing;
    }
    #endregion

    #region Terrain Sampling
    // =========================================================
    // Raise ridges while keeping corridor cores at a constant valley height.
    public override float SampleHeight(
        Vector2 tile, out float plateauWeight)
    {
        plateauWeight = 0f;

        float field = RollingLayer.Sample(tile, _seed, 1f, _size);
        float ridge = 1f - Mathf.Abs(field * 2f - 1f);
        ridge = Mathf.Pow(Mathf.Clamp(ridge, 0f, 1f), _sharpness);

        float distance = GetPathDistance(tile);
        float mountainWeight = Smooth(
            _halfWidth, _halfWidth + _shoulder, distance);

        return _valley + ridge * _height * mountainWeight;
    }
    #endregion

    #region Path Sampling
    // =========================================================
    // Restrict the visible path surface to the flattened corridor core.
    public float SamplePathSurface(Vector2 tile)
    {
        return 1f - Smooth(
            _halfWidth * 0.65f, _halfWidth, GetPathDistance(tile));
    }

    // =========================================================
    // Build two intersecting families of gently winding valley corridors.
    private float GetPathDistance(Vector2 tile)
    {
        Vector2 p = tile + _phase;
        float frequency = Mathf.Tau / _wavelength;

        float first = p.X +
            Mathf.Sin(p.Y * frequency) * _winding;
        float second = p.Y +
            Mathf.Sin(p.X * frequency + 1.7f) * _winding;

        return Mathf.Min(
            DistanceToCorridor(first),
            DistanceToCorridor(second));
    }

    // =========================================================
    // Measure distance to a repeating corridor without chunk-local seams.
    private float DistanceToCorridor(float coordinate)
    {
        float wrapped = coordinate -
            Mathf.Floor((coordinate + _spacing * 0.5f) / _spacing)
            * _spacing;

        return Mathf.Abs(wrapped);
    }

    // =========================================================
    // Blend smoothly between flat corridor cores and surrounding ridges.
    private static float Smooth(float from, float to, float value)
    {
        float t = Mathf.Clamp((value - from) / (to - from), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
    #endregion
}