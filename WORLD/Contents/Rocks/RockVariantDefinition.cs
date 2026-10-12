// Defines one rock's authored artwork and ground footprint, independent of species size.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RockVariantDefinition : Resource
{
    #region Configuration
    [Export] public VisualDefinition Visual { get; set; }
    [Export] public Vector2 GroundFootprint { get; set; } = new(128, 64);
    [Export] public float VisualHeight { get; set; } = 128f;
    [Export] public float SpawnWeight { get; set; } = 1f;
    #endregion

    // =========================================================
    // Fail explicitly instead of silently substituting primitive artwork.
    public void Validate()
    {
        if (Visual?.Image == null || Visual.VisualScene != null ||
            !string.IsNullOrWhiteSpace(Visual.ImageFolder) ||
            !float.IsFinite(GroundFootprint.X) || !float.IsFinite(GroundFootprint.Y) ||
            GroundFootprint.X <= 0 || GroundFootprint.Y <= 0 ||
            !float.IsFinite(VisualHeight) || VisualHeight <= 0 ||
            !float.IsFinite(SpawnWeight) || SpawnWeight < 0 ||
            !float.IsFinite(Visual.ArtworkScale.X) || Visual.ArtworkScale.X <= 0 ||
            Visual.ArtworkScale.X != Visual.ArtworkScale.Y ||
            !float.IsFinite(Visual.ImageAnchor.X) || !float.IsFinite(Visual.ImageAnchor.Y) ||
            !float.IsFinite(Visual.Offset.X) || !float.IsFinite(Visual.Offset.Y))
            throw new InvalidOperationException($"Invalid rock variant '{ResourcePath}': assign an image, positive footprint/height and uniform artwork scale.");
    }

    // =========================================================
    // Share exactly the same scaled dimensions between placement and the actor.
    public Vector2 FootprintFor(float size) => GroundFootprint * size;
    public float HeightFor(float size) => VisualHeight * size;
}
