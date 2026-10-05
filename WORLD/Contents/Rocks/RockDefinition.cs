// Defines a solid rock's base dimensions and footprint depth.
// Species variation and optional artwork are inherited from the shared definition.
using Godot;

[Tool, GlobalClass]
public partial class RockDefinition : WorldObjectDefinition
{
    #region Shape
    [ExportGroup("Shape")]
    [Export] public Vector2 WidthRange { get; set; } = new(64, 104);
    [Export] public Vector2 HeightRange { get; set; } = new(56, 96);
    [Export] public float FootprintDepthRatio { get; set; } = 0.5f;
    #endregion
}