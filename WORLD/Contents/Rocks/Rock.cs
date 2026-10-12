// Represents any solid rock species using its selected dimensions and variation.
// Reuses Obstacle collision, navigation compatibility and sunlight shadows.
using Godot;
using System.Threading.Tasks;

public partial class Rock : Obstacle
{
    #region Configuration
    [ExportGroup("Rock")]
    [Export] public RockDefinition Definition { get; set; }
    [Export] public RockVariantDefinition Variant { get; set; }
    [Export] public float BaseWidth { get; set; }
    [Export] public float BaseHeight { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Configuration Hook
    // =========================================================
    // Configure solid dimensions without altering the shared rock definition.
    protected override void ConfigureInstance()
    {
        if (Definition == null)
            throw new System.InvalidOperationException("Rock requires a RockDefinition.");

        Definition.ValidateVariants();
        if (Definition.Variants.Count > 0)
        {
            if (Variant == null)
            {
                using RandomNumberGenerator rng = new();
                rng.Seed = IsoGrid.Hash(Mathf.RoundToInt(GlobalPosition.X),
                    Mathf.RoundToInt(GlobalPosition.Y), 64127u);
                Variant = Definition.RollVariant(rng);
            }
            if (!Definition.Variants.Contains(Variant))
                throw new System.InvalidOperationException("Selected rock variant does not belong to its species.");
        }
        if (!float.IsFinite(SizeMultiplier) || SizeMultiplier < 0.1f)
            throw new System.InvalidOperationException("Invalid rock instance scale.");
        if (Variant != null)
        {
            Variant.Validate();
            Footprint = Variant.FootprintFor(SizeMultiplier);
            Height = Variant.HeightFor(SizeMultiplier);
            Kind = ObstacleKind.Rock;
            VisualOverride = Variant.Visual;
            ResourceHarvest.Attach(this, Definition);
            return;
        }
        SizeMultiplier = Mathf.Max(0.1f, SizeMultiplier);
        BaseWidth = Mathf.Max(16f,
            BaseWidth > 0f ? BaseWidth : Definition.WidthRange.X);
        BaseHeight = Mathf.Max(8f,
            BaseHeight > 0f ? BaseHeight : Definition.HeightRange.X);

        Footprint = new Vector2(
            BaseWidth,
            BaseWidth * Mathf.Max(0.1f, Definition.FootprintDepthRatio))
            * SizeMultiplier;
        Height = BaseHeight * SizeMultiplier;
        Kind = ObstacleKind.Rock;
        VisualOverride = Definition.Visual;
        ResourceHarvest.Attach(this, Definition);
    }
    #endregion

    #region Artwork
    // =========================================================
    // Apply base dimensions to fallback art and uniform instance variation to either visual.
    protected override async Task AttachArtworkAsync()
    {
        if (Variant != null)
        {
            TerrainVisual imported = TerrainVisual.AttachCustom(this, false, Variant.Visual)
                ?? throw new System.InvalidOperationException("Imported rock artwork is missing; primitive fallback is disabled.");
            imported.Scale = new Vector2(Mirror ? -SizeMultiplier : SizeMultiplier, SizeMultiplier);
            return;
        }
        // Retained legacy actors may still use the old shared atlas.
        await PlaceholderAtlas.EnsureReady(this);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

        int variant = RockVariant;
        if (variant < 0)
        {
            variant = (int)(IsoGrid.Hash(
                Mathf.RoundToInt(GlobalPosition.X),
                Mathf.RoundToInt(GlobalPosition.Y), 64127u)
                % (uint)RockDrawing.VariantCount);
        }

        TerrainVisual visual = TerrainVisual.Attach(
            this, PlaceholderAtlas.GetRockRegion(variant),
            new Vector2(-80, -120),
            new Vector2(BaseWidth / 96f, BaseHeight / 72f),
            false, VisualOverride);
        visual.Scale = new Vector2(
            Mirror ? -SizeMultiplier : SizeMultiplier, SizeMultiplier);
    }
    #endregion
}
