// Defines tree trunk collision, visual height and placement spacing.
// Species identity, size variation and artwork come from the shared definition.
using Godot;

[Tool, GlobalClass]
public partial class TreeDefinition : WorldObjectDefinition
{
    #region Trunk
    [ExportGroup("Trunk")]
    [Export] public Vector2 TrunkFootprint { get; set; } = new(44, 24);
    [Export] public float VisualHeight { get; set; } = 280f;
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 Spacing { get; set; } = new(220, 120);
    [Export] public float GroundClearance { get; set; } = 85f;
    #endregion
}