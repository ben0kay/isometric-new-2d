// Represents any walkable plant species using its settings and chosen instance variation.
// Imported visuals and baked fallback artwork share the same instance scale.
using Godot;

public partial class Plant : Node2D
{
    #region Configuration
    [ExportGroup("Plant")]
    [Export] public PlantDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach terrain-adjusted artwork above the species' shared contact shadow.
    public override async void _Ready()
    {
        try
        {
            if (Definition == null)
                throw new System.InvalidOperationException(
                    "Plant requires a PlantDefinition.");

            await VegetationAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            TerrainVisual visual = TerrainVisual.Attach(
                this,
                VegetationAtlas.GetRegion(
                    Definition.BakedKind == PlantArtwork.Shrub, Variant),
                VegetationAtlas.Origin, Vector2.One, false,
                Definition.Visual, VegetationAtlas.Texture,
                VegetationAtlas.WindMaterial);

            Sprite2D shadow = new()
            {
                Name = "ContactShadow",
                Texture = VegetationShadow.GetTexture(),
                Position = new Vector2(5, 3),
                Scale = Definition.ContactShadowScale,
                TextureFilter = TextureFilterEnum.Linear
            };
            visual.AddChild(shadow);
            visual.MoveChild(shadow, 0);

            float size = Mathf.Max(0.1f, SizeMultiplier);
            visual.Scale = new Vector2(Mirror ? -size : size, size);
            SetProcess(false);
        }
        catch (System.Exception error)
        {
            GD.PushError($"Plant '{Name}' artwork failed: {error}");
        }
    }
    #endregion
}