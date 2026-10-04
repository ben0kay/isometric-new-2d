// Represents any tree species using its definition and per-instance variation.
// Only the trunk footprint collides; the canopy remains independent artwork.
using Godot;
using System.Threading.Tasks;

public partial class Tree : Obstacle
{
    #region Configuration
    [ExportGroup("Tree")]
    [Export] public TreeDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Configuration Hook
    // =========================================================
    // Configure the shared obstacle foundation from this tree species.
    protected override void ConfigureInstance()
    {
        if (Definition == null)
            throw new System.InvalidOperationException("Tree requires a TreeDefinition.");

        SizeMultiplier = Mathf.Max(0.1f, SizeMultiplier);
        Footprint = Definition.TrunkFootprint * SizeMultiplier;
        Height = Definition.VisualHeight * SizeMultiplier;
        VisualOverride = Definition.Visual;
        Kind = ObstacleKind.Rock;
    }
    #endregion

    #region Artwork
    // =========================================================
    // Attach custom artwork or the cached Carbon Tree fallback with identical size variation.
    protected override async Task AttachArtworkAsync()
    {
        await TreeAtlas.EnsureReady(this);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;

        TerrainVisual visual = TerrainVisual.Attach(
            this, TreeAtlas.GetRegion(Variant), TreeAtlas.Origin,
            Vector2.One, false, VisualOverride,
            TreeAtlas.Texture, TreeAtlas.WindMaterial);
        visual.Scale = new Vector2(
            Mirror ? -SizeMultiplier : SizeMultiplier, SizeMultiplier);
    }
    #endregion
}