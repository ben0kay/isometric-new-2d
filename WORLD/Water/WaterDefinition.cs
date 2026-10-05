// Adds water-specific rendering settings to the shared surface definition.
using Godot;
using System;

[Tool, GlobalClass]
public partial class WaterDefinition : SurfaceDefinition
{
    #region Water Rendering
    [ExportGroup("Water Rendering")]
    [Export] public Shader WaterShader { get; set; }
    [Export] public float WaveSpeed { get; set; } = 0.6f;
    [Export] public float WaveStrength { get; set; } = 0.12f;
    [Export] public float Opacity { get; set; } = 0.85f;
    #endregion

    #region Validation
    // =========================================================
    // Require valid water rendering settings in addition to shared surface limits.
    public override void Validate()
    {
        base.Validate();
        if (WaterShader == null ||
            !float.IsFinite(WaveSpeed) ||
            !float.IsFinite(WaveStrength) ||
            WaveStrength < 0f || WaveStrength > 1f ||
            !float.IsFinite(Opacity) || Opacity < 0f || Opacity > 1f)
            throw new InvalidOperationException(
                $"Water '{Id}' requires a shader and valid rendering settings.");
    }
    #endregion
}