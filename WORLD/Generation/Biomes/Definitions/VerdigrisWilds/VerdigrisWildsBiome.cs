// Defines Verdigris Wilds terrain: gentle rolling hills and flat clearings.
// Supplies its own sampler through the existing biome terrain factory.
using Godot;
using System;

[Tool, GlobalClass]
public partial class VerdigrisWildsBiome : BiomeDefinition
{
    #region Clearings
    [ExportGroup("FLAT CLEARINGS")]

    // Chance that a terrain placement cell contains a clearing.
    [Export(PropertyHint.Range, "0,1,0.05")]
    public float FlatPatchCoverage { get; set; } = 0.8f;

    // Radius of the completely flat interior, measured in tiles.
    [Export(PropertyHint.Range, "4,96,1")]
    public float FlatPatchSizeTiles { get; set; } = 36f;

    // Additional width used to blend the clearing into surrounding hills.
    [Export(PropertyHint.Range, "2,48,1")]
    public float FlatPatchBlendWidthTiles { get; set; } = 12f;
    #endregion

    #region Terrain Factory
    // =========================================================
    // Create the Wilds sampler without changing shared world generation.
    public override TerrainGenerator CreateTerrain(
        uint seed, bool testPlateau, Vector2 testCentre)
    {
        ValidateTerrain();
        return new VerdigrisTerrainGenerator(this, seed);
    }
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid settings before terrain generation begins.
    private void ValidateTerrain()
    {
        if (!float.IsFinite(RollingHeight) || RollingHeight < 0f ||
            !float.IsFinite(RollingFeatureSize) || RollingFeatureSize < 4f ||
            !float.IsFinite(FlatPatchCoverage) ||
            FlatPatchCoverage < 0f || FlatPatchCoverage > 1f ||
            !float.IsFinite(FlatPatchSizeTiles) || FlatPatchSizeTiles < 4f ||
            !float.IsFinite(FlatPatchBlendWidthTiles) ||
            FlatPatchBlendWidthTiles < 2f)
            throw new InvalidOperationException(
                $"{Id}: invalid rolling terrain or flat clearing settings.");
    }
    #endregion
}