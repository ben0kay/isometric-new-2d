// Samples broad continuous temperature and moisture fields from absolute tile coordinates.
// Climate changes independently from chunks and local terrain elevation.
using Godot;

public readonly struct ClimateSample
{
    #region Values
    public readonly float Temperature;
    public readonly float Moisture;
    #endregion

    #region Construction
    // =========================================================
    // Package normalized climate values without allocating an object.
    public ClimateSample(float temperature, float moisture)
    {
        Temperature = temperature;
        Moisture = moisture;
    }
    #endregion
}

public sealed class WorldClimate
{
    #region State
    private readonly uint _seed;
    private readonly float _regionSize;
    #endregion

    #region Construction
    // =========================================================
    // Cache the seed and broad climate feature size.
    public WorldClimate(uint seed, float regionSize)
    {
        _seed = seed;
        _regionSize = Mathf.Max(16f, regionSize);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Read two independent smooth fields using the existing deterministic noise layer.
    public ClimateSample Sample(Vector2 tile)
    {
        return new ClimateSample(
            RollingLayer.Sample(tile, _seed ^ 0x1C47u, 1f, _regionSize),
            RollingLayer.Sample(tile, _seed ^ 0x7A19u, 1f, _regionSize));
    }
    #endregion
}