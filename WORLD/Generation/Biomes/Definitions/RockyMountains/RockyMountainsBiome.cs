// Defines mountain terrain and its rock/path appearance.
// Existing biomes keep their current terrain and ground settings.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RockyMountainsBiome : BiomeDefinition
{
    #region Mountains
    [ExportGroup("Mountains")]

    [Export(PropertyHint.Range, "0,600,5")]
    public float MountainHeight { get; set; } = 240f;

    [Export(PropertyHint.Range, "8,128,1")]
    public float MountainSizeTiles { get; set; } = 28f;

    [Export(PropertyHint.Range, "0,200,1")]
    public float ValleyHeight { get; set; } = 24f;

    [Export(PropertyHint.Range, "1,4,0.1")]
    public float RidgeSharpness { get; set; } = 1.8f;
    #endregion

    #region Paths
    [ExportGroup("Mountain Paths")]

    [Export(PropertyHint.Range, "32,256,1")]
    public float PathSpacingTiles { get; set; } = 88f;

    [Export(PropertyHint.Range, "0.5,12,0.5")]
    public float PathHalfWidthTiles { get; set; } = 2.5f;

    [Export(PropertyHint.Range, "2,32,1")]
    public float PathShoulderTiles { get; set; } = 10f;

    [Export(PropertyHint.Range, "0,24,1")]
    public float PathWindingTiles { get; set; } = 10f;

    [Export(PropertyHint.Range, "32,256,1")]
    public float PathWavelengthTiles { get; set; } = 120f;
    #endregion

    #region Ground
    [ExportGroup("Mountain Ground")]

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float BaseRockCoverage { get; set; } = 0.25f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float SteepRockCoverage { get; set; } = 0.95f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RockSlopeStart { get; set; } = 0.08f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float RockSlopeFull { get; set; } = 0.24f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float PathSurfaceStrength { get; set; } = 0.9f;
    #endregion

    #region Terrain Factory
    // =========================================================
    // Supply a mountain sampler through the existing biome factory.
    public override TerrainGenerator CreateTerrain(
        uint seed, bool testPlateau, Vector2 testCentre)
    {
        ValidateMountains();
        return new MountainTerrainGenerator(
            this, seed, testPlateau, testCentre);
    }
    #endregion

    #region Validation
    // =========================================================
    // Reject values that would produce invalid terrain or surface masks.
    private void ValidateMountains()
    {
        RequireMinimum(MountainHeight, 0f, nameof(MountainHeight));
        RequireMinimum(MountainSizeTiles, 8f, nameof(MountainSizeTiles));
        RequireMinimum(ValleyHeight, 0f, nameof(ValleyHeight));
        RequireMinimum(RidgeSharpness, 1f, nameof(RidgeSharpness));
        RequireMinimum(PathSpacingTiles, 32f, nameof(PathSpacingTiles));
        RequireMinimum(PathHalfWidthTiles, 0.5f, nameof(PathHalfWidthTiles));
        RequireMinimum(PathShoulderTiles, 2f, nameof(PathShoulderTiles));
        RequireMinimum(PathWindingTiles, 0f, nameof(PathWindingTiles));
        RequireMinimum(PathWavelengthTiles, 32f, nameof(PathWavelengthTiles));

        RequireUnit(BaseRockCoverage, nameof(BaseRockCoverage));
        RequireUnit(SteepRockCoverage, nameof(SteepRockCoverage));
        RequireUnit(PathSurfaceStrength, nameof(PathSurfaceStrength));

        RequireMinimum(RockSlopeStart, 0f, nameof(RockSlopeStart));
        RequireMinimum(RockSlopeFull, 0f, nameof(RockSlopeFull));

        if (RockSlopeFull <= RockSlopeStart)
            throw new InvalidOperationException(
                $"{Id}: RockSlopeFull must exceed RockSlopeStart.");

        if (PathHalfWidthTiles + PathShoulderTiles >=
            PathSpacingTiles * 0.5f)
            throw new InvalidOperationException(
                $"{Id}: paths and shoulders need more spacing.");
    }

    // =========================================================
    // Require a finite value above its supported minimum.
    private void RequireMinimum(float value, float minimum, string label)
    {
        if (!float.IsFinite(value) || value < minimum)
            throw new InvalidOperationException(
                $"{Id}: {label} must be finite and at least {minimum}.");
    }

    // =========================================================
    // Require a finite normalized surface setting.
    private void RequireUnit(float value, string label)
    {
        RequireMinimum(value, 0f, label);
        if (value > 1f)
            throw new InvalidOperationException(
                $"{Id}: {label} must not exceed one.");
    }
    #endregion
}