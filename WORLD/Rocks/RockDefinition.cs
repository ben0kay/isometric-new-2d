// Defines a reusable solid rock type, its size range and selection weight.
// An unassigned visual uses the existing baked rock artwork.
using Godot;

[Tool, GlobalClass]
public partial class RockDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "boulder";
    [Export] public float Weight { get; set; } = 1f;
    #endregion

    #region Shape
    [ExportGroup("Shape")]
    [Export] public Vector2 WidthRange { get; set; } = new(64, 104);
    [Export] public Vector2 HeightRange { get; set; } = new(56, 96);
    [Export] public float FootprintDepthRatio { get; set; } = 0.5f;
    #endregion

    #region Artwork
    [ExportGroup("Artwork")]
    [Export] public VisualDefinition Visual { get; set; }
    #endregion
}