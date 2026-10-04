// Represents one walkable grass tuft with terrain-adjusted cached artwork.
// Supports an independent custom image or scene override.
using Godot;

public enum GrassHeight { Short, Medium, Tall }

public partial class AlienGrass : Node2D
{
    #region Configuration
    [Export] public GrassHeight HeightKind { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public VisualDefinition VisualOverride { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Attach baked or custom artwork without creating any collision.
    public override void _Ready()
    {
        if (VegetationAtlas.Texture == null)
        {
            GD.PushError("Prepare VegetationAtlas before spawning grass.");
            return;
        }

        TerrainVisual.Attach(
            this, VegetationAtlas.GetGrassRegion(HeightKind, Variant),
            VegetationAtlas.GrassOrigin,
            Vector2.One * Mathf.Clamp(SizeMultiplier, 0.5f, 1.5f),
            false, VisualOverride, VegetationAtlas.Texture,
            VegetationAtlas.GrassWindMaterial);
        SetProcess(false);
    }
    #endregion
}