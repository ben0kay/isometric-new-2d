// Defines vegetation movement and obstruction fading.
// Lighting is shared with every other world visual through VisualDefinition.
using Godot;

[Tool, GlobalClass]
public partial class VegetationVisualSettings : Resource
{
    #region Wind
    [ExportGroup("Wind")]
    [Export] public bool WindEnabled { get; set; } = true;
    [Export] public float WindStrength { get; set; } = 4f;
    [Export] public float WindSpeed { get; set; } = 1.4f;
    #endregion

    #region Brushing
    [ExportGroup("Player Brushing")]
    [Export] public bool BrushingEnabled { get; set; } = true;
    [Export] public float BrushStrength { get; set; } = 9f;
    #endregion

    #region Visibility
    [ExportGroup("Visibility")]
    [Export] public bool FadeBehindPlayer { get; set; } = true;
    #endregion
}