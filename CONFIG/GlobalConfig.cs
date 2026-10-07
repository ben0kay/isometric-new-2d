// Holds shared game-wide tuning settings.
// WorldConfig inherits these settings on the existing CONFIG node.
using Godot;

public partial class GlobalConfig : Node
{
    #region Biomes
    [ExportGroup("BIOMES")]
    [Export(PropertyHint.Range, "0.25,8,0.25")]
    public float BiomeScaleMultiplier { get; set; } = 1f;
    #endregion

    #region Basins
    [ExportGroup("BASINS")]
    [Export] public bool GenerateBiomeBasins { get; set; } = true;

    [Export(PropertyHint.Range, "16,256,8")]
    public float BasinCandidateSpacingTiles { get; set; } = 64f;
    #endregion

    #region Caves
    [ExportGroup("CAVES")]
    [Export] public bool GenerateCaves { get; set; } = true;
        // Absolute terrain elevation of the main underground network.
    [Export] public float CaveFloorElevation { get; set; } = -160f;

    // Minimum logical tile distance between surface entrance mouths.
    // Large tunnel profiles may require a greater safety distance.
    [Export(PropertyHint.Range, "64,1024,8")]
    public float MinimumCaveHoleDistanceTiles { get; set; } = 96f;
    #endregion

    #region Visibility
    [ExportGroup("VISIBILITY")]
    [Export] public bool ObstructionFadingEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0,100,1")]
    public float ObstructingSpriteOpacityPercent { get; set; } = 35f;

    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public float ObstructionFadeSeconds { get; set; } = 0.2f;
    #endregion
}