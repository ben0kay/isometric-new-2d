// Represents any solid rock species using its selected dimensions and variation.
// Reuses Obstacle collision, navigation compatibility and sunlight shadows.
using Godot;
using System.Threading.Tasks;

public partial class Rock : Obstacle
{
    #region Configuration
    [ExportGroup("Rock")]
    [Export] public RockDefinition Definition { get; set; }
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
    }
    #endregion

    #region Artwork
    // =========================================================
    // Apply base dimensions to fallback art and uniform instance variation to either visual.
    protected override async Task AttachArtworkAsync()
    {
        await PlaceholderAtlas.EnsureReady(this);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;

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