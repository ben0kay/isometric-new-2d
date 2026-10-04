// Defines optional replacement artwork for a world object or character.
// An empty definition lets TerrainVisual use its existing baked fallback.
using Godot;

[GlobalClass]
public partial class VisualDefinition : Resource
{
    #region Artwork
    [Export] public PackedScene VisualScene { get; set; }
    [Export] public Texture2D Image { get; set; }
    #endregion

    #region Placement
    [Export] public Vector2 Offset { get; set; } = Vector2.Zero;
    [Export] public Vector2 ArtworkScale { get; set; } = Vector2.One;
    [Export] public Vector2 ImageAnchor { get; set; } = new(0.5f, 1f);
    [Export] public CanvasItem.TextureFilterEnum ImageFilter { get; set; }
        = CanvasItem.TextureFilterEnum.Linear;
    #endregion
}