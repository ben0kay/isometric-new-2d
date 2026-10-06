// Supplies the default rolling and plateau terrain sampler.
// Specialized biome samplers can override height sampling without replacing world blending.
using Godot;

public class TerrainGenerator
{
    #region State
    private readonly uint _seed;
    private readonly float _rollingHeight, _featureSize, _plateauHeight;
    private readonly PlateauFeature _plateau;

    public virtual float HeightRange { get; }
    #endregion

    #region Construction
    // =========================================================
    // Snapshot the default terrain settings for this biome.
    public TerrainGenerator(
        BiomeDefinition biome, uint seed,
        bool testPlateau, Vector2 testCentre)
    {
        _seed = seed;
        _rollingHeight = Mathf.Max(0f, biome.RollingHeight);
        _featureSize = Mathf.Max(4f, biome.RollingFeatureSize);
        _plateauHeight = Mathf.Max(0f, biome.PlateauHeight);
        _plateau = new PlateauFeature(biome, seed, testPlateau, testCentre);

        HeightRange = Mathf.Max(1f,
            biome.PlateausEnabled
                ? Mathf.Max(_rollingHeight, _plateauHeight)
                : _rollingHeight);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Preserve smooth rolling ground and flat plateau cores.
    public virtual float SampleHeight(Vector2 tile, out float plateauWeight)
    {
        float rolling = RollingLayer.Sample(
            tile, _seed, _rollingHeight, _featureSize);

        plateauWeight = _plateau.SampleWeight(tile);
        return Mathf.Lerp(rolling, _plateauHeight, plateauWeight);
    }
    #endregion
}