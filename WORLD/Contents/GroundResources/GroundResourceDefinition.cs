// Defines a diggable ground material independently from its runtime deposits.
// Future soil or hazard types can extend this shared resource definition.
using Godot;

[Tool, GlobalClass]
public partial class GroundResourceDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string ItemId { get; set; } = "";
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 RadiusTiles { get; set; } = new(1.4f, 2.2f);
    [Export] public float ClearanceTiles { get; set; } = 0.75f;
    [Export] public float MaximumHeightVariation { get; set; } = 0.02f;
    #endregion

    #region Extraction
    [ExportGroup("Extraction")]
    [Export] public int UnitsPerDeposit { get; set; } = 16;
    [Export] public float WorkPerUnit { get; set; } = 2f;
    [Export] public int RequiredShovelStrength { get; set; } = 1;
    #endregion

    #region Appearance
    [ExportGroup("Appearance")]
    [Export] public Color SurfaceTint { get; set; } = new("#a48a62");
    #endregion
}