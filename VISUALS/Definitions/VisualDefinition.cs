// Defines replacement artwork, placement, lighting and ground shadows.
// Optional vegetation effects remain separate from shared visual behaviour.
using Godot;

[GlobalClass]
public partial class VisualDefinition : Resource
{
    #region Artwork
    [ExportGroup("Artwork")]
    [Export] public PackedScene VisualScene { get; set; }
    [Export] public Texture2D Image { get; set; }
    #endregion

    #region Placement
    [ExportGroup("Placement")]
    [Export] public Vector2 Offset { get; set; } = Vector2.Zero;
    [Export] public Vector2 ArtworkScale { get; set; } = Vector2.One;
    [Export] public Vector2 ImageAnchor { get; set; } = new(0.5f, 1f);
    [Export] public CanvasItem.TextureFilterEnum ImageFilter { get; set; }
        = CanvasItem.TextureFilterEnum.Linear;
    #endregion

    #region Lighting
    [ExportGroup("Lighting")]
    [Export] public VisualLightingSettings Lighting { get; set; }
    #endregion

    #region Shadows
    [ExportGroup("Ground Shadows")]
    [Export] public GroundShadowSettings Shadows { get; set; }
    #endregion

    #region Vegetation
    [ExportGroup("Vegetation Effects")]
    [Export] public VegetationVisualSettings Vegetation { get; set; }
    #endregion
}