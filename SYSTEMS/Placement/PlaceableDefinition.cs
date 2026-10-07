// Defines a placeable item's world scene, preview artwork, and grid footprint.
// The item owns inventory data; the instantiated world object owns its health.
using Godot;
using System;

[Tool, GlobalClass]
public partial class PlaceableDefinition : Resource
{
    #region Object
    [ExportGroup("Object")]
    [Export] public PackedScene WorldScene { get; set; }
    [Export] public PackedScene ArtworkScene { get; set; }
    [Export] public Vector2 ArtworkScale { get; set; } = Vector2.One;
    #endregion

    #region Footprint
    [ExportGroup("Footprint")]
    [Export] public Vector2I Cells { get; set; } = Vector2I.One;

    [Export(PropertyHint.Range, "0,32,0.5")]
    public float MaximumHeightDifference { get; set; } = 8f;

    [Export(PropertyHint.Range, "1,256,1")]
    public float CoverHeight { get; set; } = 96f;
    #endregion

    #region Validation
    // =========================================================
    // Reject missing scenes and unreasonable footprint settings.
    public void Validate()
    {
        if (WorldScene == null || ArtworkScene == null ||
            Cells.X < 1 || Cells.Y < 1 || Cells.X > 8 || Cells.Y > 8 ||
            !float.IsFinite(MaximumHeightDifference) ||
            MaximumHeightDifference < 0f ||
            !float.IsFinite(CoverHeight) || CoverHeight <= 0f ||
            !float.IsFinite(ArtworkScale.X) || ArtworkScale.X <= 0f ||
            !float.IsFinite(ArtworkScale.Y) || ArtworkScale.Y <= 0f)
            throw new InvalidOperationException(
                "Placeable requires scenes, a 1–8 cell footprint, and valid dimensions.");
    }
    #endregion
}