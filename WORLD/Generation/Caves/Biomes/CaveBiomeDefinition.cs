// Defines underground geography while reusing shared biome identity and content.
// Cave terrain creation is independent of surface terrain generation.
using Godot;
using System;

[Tool, GlobalClass]
public partial class CaveBiomeDefinition : BiomeDefinition
{
    #region Cave Shape
    [ExportGroup("Cave / Chambers")]
    [Export] public Vector2 ChamberRadiusRange { get; set; } = new(4, 8);
    [Export] public float WallIrregularityTiles { get; set; } = 0.8f;

    [ExportGroup("Cave / Passages")]
    [Export] public float TunnelWidthTiles { get; set; } = 4.5f;
    [Export] public float WindingTiles { get; set; } = 2f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ExtraConnectionChance { get; set; } = 0.25f;

    [ExportGroup("Cave / Floor")]
    [Export] public float FloorAmplitude { get; set; }
    [Export] public float FloorFeatureSizeTiles { get; set; } = 32f;
    #endregion

    #region Terrain Factory
    // =========================================================
    // Supply the default cave shape unless a biome specializes its generator.
    public virtual CaveTerrainGenerator CreateCaveTerrain(uint seed)
    {
        return new CaveTerrainGenerator(this, seed);
    }
    #endregion

    #region Validation
    // =========================================================
    // Validate underground settings without requiring surface climate or placement.
    public void ValidateCave()
    {
        if (!float.IsFinite(SelectionWeight) || SelectionWeight <= 0f ||
            !float.IsFinite(ChamberRadiusRange.X) ||
            !float.IsFinite(ChamberRadiusRange.Y) ||
            ChamberRadiusRange.X < 3f ||
            ChamberRadiusRange.Y < ChamberRadiusRange.X ||
            !float.IsFinite(WallIrregularityTiles) ||
            WallIrregularityTiles < 0f ||
            WallIrregularityTiles > ChamberRadiusRange.X * 0.4f ||
            !float.IsFinite(TunnelWidthTiles) ||
            TunnelWidthTiles < 4f ||
            !float.IsFinite(WindingTiles) || WindingTiles < 0f ||
            !float.IsFinite(ExtraConnectionChance) ||
            ExtraConnectionChance < 0f || ExtraConnectionChance > 1f ||
            !float.IsFinite(FloorAmplitude) || FloorAmplitude < 0f ||
            !float.IsFinite(FloorFeatureSizeTiles) ||
            FloorFeatureSizeTiles < 8f)
        {
            throw new InvalidOperationException(
                $"Cave biome '{Id}' has invalid generation settings.");
        }

        if (Content == null || Content.Vegetation == null)
            throw new InvalidOperationException(
                $"Cave biome '{Id}' requires a content resource and vegetation settings.");
    }
    #endregion
}