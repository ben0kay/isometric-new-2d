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
// Attach terrain-adjusted artwork above a shared static contact shadow.
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

    Sprite2D shadow = new()
    {
        Name = "ContactShadow",
        Texture = VegetationShadow.GetTexture(),
        Position = new Vector2(5, 3),
        Scale = Shrub ? new Vector2(1.05f, 0.85f) : new Vector2(0.8f, 0.7f),
        TextureFilter = TextureFilterEnum.Linear
    };
    visual.AddChild(shadow);
    visual.MoveChild(shadow, 0);

    visual.Scale = new Vector2(Mirror ? -size : size, size);
    SetProcess(false);
}
    #endregion
}