// Defines shared placement limits and movement effects for ground surfaces.
using Godot;
using System;

[Tool, GlobalClass]
public partial class SurfaceDefinition : Resource
{
    #region Configuration
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "surface";

    [ExportGroup("Shape — tile units")]
    [Export] public Vector2 RadiusTiles { get; set; } = new(3f, 2.4f);
    [Export] public float RotationDegrees { get; set; }
    [Export] public float ClearanceTiles { get; set; } = 0.75f;
    [Export] public float MaximumHeightVariation { get; set; } = 0.02f;

    [ExportGroup("Actor Effects")]
    [Export] public float MovementMultiplier { get; set; } = 1f;
    [Export] public float SubmersionPixels { get; set; }
    [Export] public Color SurfaceTint { get; set; } = new(0.12f, 0.3f, 0.35f);
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid definitions before placing or querying their geometry.
    public virtual void Validate()
    {
        if (!float.IsFinite(RadiusTiles.X) ||
            !float.IsFinite(RadiusTiles.Y) ||
            RadiusTiles.X <= 0f || RadiusTiles.Y <= 0f ||
            Mathf.Max(RadiusTiles.X, RadiusTiles.Y) > 64f ||
            !float.IsFinite(RotationDegrees) ||
            !float.IsFinite(ClearanceTiles) || ClearanceTiles < 0f ||
            !float.IsFinite(MaximumHeightVariation) ||
            MaximumHeightVariation < 0f ||
            !float.IsFinite(MovementMultiplier) ||
            MovementMultiplier < 0f || MovementMultiplier > 1f ||
            !float.IsFinite(SubmersionPixels) || SubmersionPixels < 0f)
            throw new InvalidOperationException(
                $"Surface '{Id}' has invalid shape or effect settings.");
    }
    #endregion
}