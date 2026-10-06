// Defines basin geometry and the water that fills it.
using Godot;
using System;

[Tool, GlobalClass]
public partial class WaterDefinition : SurfaceDefinition
{
    #region Basin
    [ExportGroup("Basin")]
    [Export] public float BasinDepth { get; set; } = 24f;
    [Export] public float ShoreWidthTiles { get; set; } = 0.9f;
    [Export] public float WaterSurfaceDrop { get; set; } = 6f;
    #endregion

    #region Water Rendering
    [ExportGroup("Water Rendering")]
    [Export] public Shader WaterShader { get; set; }
    [Export] public float WaveSpeed { get; set; } = 0.6f;
    [Export] public float WaveStrength { get; set; } = 0.12f;
    [Export] public float Opacity { get; set; } = 0.85f;
    #endregion

    #region Validation
    // =========================================================
    // Validate the basin dimensions and its water presentation.
    public override void Validate()
    {
        base.Validate();
        float smallestRadius = Mathf.Min(RadiusTiles.X, RadiusTiles.Y);

        if (!float.IsFinite(BasinDepth) || BasinDepth <= 0f ||
            !float.IsFinite(ShoreWidthTiles) || ShoreWidthTiles <= 0f ||
            ShoreWidthTiles >= smallestRadius * 0.8f ||
            !float.IsFinite(WaterSurfaceDrop) ||
            WaterSurfaceDrop < MaximumHeightVariation ||
            WaterSurfaceDrop >= BasinDepth ||
            WaterShader == null ||
            !float.IsFinite(WaveSpeed) ||
            !float.IsFinite(WaveStrength) ||
            WaveStrength < 0f || WaveStrength > 1f ||
            !float.IsFinite(Opacity) || Opacity < 0f || Opacity > 1f)
            throw new InvalidOperationException(
                $"Water '{Id}' has invalid basin or rendering settings.");
    }
    #endregion
}