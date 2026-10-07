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

    // One potential basin location per square of this width in tile units.
    [Export(PropertyHint.Range, "16,256,8")]
    public float BasinCandidateSpacingTiles { get; set; } = 64f;
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