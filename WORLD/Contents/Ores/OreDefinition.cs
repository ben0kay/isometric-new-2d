// Defines an ore deposit's geometry, extraction settings, and fallback drawing.
// Inherits the existing imported-image/scene override and species variation hooks.
using Godot;

[Tool, GlobalClass]
public partial class OreDefinition : WorldObjectDefinition
{
    #region Geometry
    [ExportGroup("Deposit / Geometry")]
    [Export] public Vector2 Footprint { get; set; } = new(68, 36);
    [Export] public float Height { get; set; } = 50f;
    #endregion

    #region Extraction
    [ExportGroup("Deposit / Extraction")]
    [Export] public ItemDefinition YieldItem { get; set; }
    [Export] public int TotalUnits { get; set; } = 30;
    [Export] public int UnitsPerBatch { get; set; } = 1;
    [Export] public float WorkPerBatch { get; set; } = 18f;
    #endregion

    #region Baked Artwork
    [ExportGroup("Deposit / Baked Artwork")]
    [Export] public PackedScene FallbackDrawing { get; set; }
    [Export] public Vector2I BakeSize { get; set; } = new(192, 160);
    [Export] public Vector2 BakeAnchor { get; set; } = new(96, 112);
    [Export] public int ArtworkRevision { get; set; } = 1;
    #endregion
}