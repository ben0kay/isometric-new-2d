// Holds shared game-wide tuning settings.
// WorldConfig inherits these settings on the existing CONFIG node.
using Godot;

public partial class GlobalConfig : Node
{
    #region Visibility
    [ExportGroup("VISIBILITY")]
    [Export] public bool ObstructionFadingEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0,100,1")]
    public float ObstructingSpriteOpacityPercent { get; set; } = 35f;

    [Export(PropertyHint.Range, "0.05,2,0.05")]
    public float ObstructionFadeSeconds { get; set; } = 0.2f;
    #endregion
}