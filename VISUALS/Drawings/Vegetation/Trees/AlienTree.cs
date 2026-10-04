// Represents a solid tree trunk with independent terrain-adjusted artwork.
// Inherits Obstacle so existing enemy navigation recognises its footprint.
using Godot;

public partial class AlienTree : Obstacle
{
    #region Configuration
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build trunk collision and attach custom artwork or the shared baked tree.
    public override void _Ready()
    {
        float size = Mathf.Clamp(SizeMultiplier, 0.4f, 1.5f);
        Footprint = new Vector2(44, 24) * size;
        Height = 280f * size;
        CollisionLayer = 1;
        CollisionMask = 0;

        AddChild(new CollisionShape2D
        {
            Name = "TrunkFootprint",
            Shape = new RectangleShape2D { Size = Footprint }
        });

        if (TreeAtlas.Texture == null)
        {
            GD.PushError("Prepare TreeAtlas before spawning trees.");
            return;
        }

        TerrainVisual.Attach(
            this, TreeAtlas.GetRegion(Variant), TreeAtlas.Origin,
            Vector2.One * size, false, VisualOverride,
            TreeAtlas.Texture, TreeAtlas.WindMaterial);

        WorldAtmosphere atmosphere = GetTree().GetFirstNodeInGroup(
            "world_atmosphere") as WorldAtmosphere;
        atmosphere?.CreateObstacleShadow(this);
        SetProcess(false);
    }
    #endregion
}