// Defines one grass species and its baked fallback height.
// Imported artwork can replace the fallback without changing its placement.
using Godot;

[Tool, GlobalClass]
public partial class GrassDefinition : WorldObjectDefinition
{
    #region Baked Fallback
    [ExportGroup("Baked Fallback")]
    [Export] public GrassHeight BakedHeight { get; set; }
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 Spacing { get; set; } = new(25, 14);
    [Export] public float GroundClearance { get; set; } = 28f;
    #endregion
}