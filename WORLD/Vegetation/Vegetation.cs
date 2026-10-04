// Represents one walkable vegetation instance with terrain-adjusted artwork.
// Uses a custom visual when assigned, otherwise the shared baked vegetation.
using Godot;

public partial class Vegetation : Node2D
{
    #region Configuration
    [Export] public bool Shrub { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    [Export] public VisualDefinition VisualOverride { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach prepared artwork once without adding processing or collision.
    public override void _Ready()
    {
        if (VegetationAtlas.Texture == null)
        {
            GD.PushError("Prepare VegetationAtlas before spawning vegetation.");
            return;
        }

        float size = Mathf.Max(0.1f, SizeMultiplier);
        TerrainVisual visual = TerrainVisual.Attach(
            this, VegetationAtlas.GetRegion(Shrub, Variant),
            VegetationAtlas.Origin, Vector2.One, false,
            VisualOverride, VegetationAtlas.Texture,
            VegetationAtlas.WindMaterial);

        visual.Scale = new Vector2(Mirror ? -size : size, size);
        SetProcess(false);
    }
    #endregion
}