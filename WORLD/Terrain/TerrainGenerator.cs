// Combines continuous terrain layers with features that reshape the surface.
// Keeps terrain generation independent from rendering and scene-tree ownership.
using Godot;

public sealed class TerrainGenerator
{
    #region State
    private readonly uint _seed;
    private readonly float _rollingHeight, _featureSize, _plateauHeight;
    private readonly PlateauFeature _plateau;
    public float HeightRange { get; }
    #endregion

    #region Construction
    // =========================================================
    // Prepare terrain sampling from one biome definition.
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
    // Apply plateau flattening after rolling terrain so its core stays flat.
    public float SampleHeight(Vector2 tile, out float plateauWeight)
    {
        float rolling = RollingLayer.Sample(
            tile, _seed, _rollingHeight, _featureSize);
        plateauWeight = _plateau.SampleWeight(tile);
        return Mathf.Lerp(rolling, _plateauHeight, plateauWeight);
    }
    #endregion
}