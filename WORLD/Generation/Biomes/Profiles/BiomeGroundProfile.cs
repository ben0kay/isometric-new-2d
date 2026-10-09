// Defines ground coverage and surface composition.
// Colours are configured separately through BiomeDefinition.ColourProfile.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeGroundProfile : Resource
{
    #region Existing Ground
    [ExportGroup("Base Ground")]

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DustAmount { get; set; } = 0.55f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MineralAmount { get; set; } = 0.32f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float DetailStrength { get; set; } = 0.35f;
    #endregion

    #region Grass Ground
    [ExportGroup("Grass Ground")]

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float GrassCoverage { get; set; }

    [Export(PropertyHint.Range, "4,128,1")]
    public float GrassPatchScaleTiles { get; set; } = 32f;

    #endregion

    #region Mud
    [ExportGroup("Mud")]

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float MudStrength { get; set; }
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid settings before producing chunk textures.
    public void Validate()
    {
        ValidateUnit(DustAmount, nameof(DustAmount));
        ValidateUnit(MineralAmount, nameof(MineralAmount));
        ValidateUnit(DetailStrength, nameof(DetailStrength));
        ValidateUnit(GrassCoverage, nameof(GrassCoverage));
        ValidateUnit(MudStrength, nameof(MudStrength));

        if (!float.IsFinite(GrassPatchScaleTiles) ||
            GrassPatchScaleTiles < 4f)
            throw new InvalidOperationException(
                "GrassPatchScaleTiles must be finite and at least 4.");

    }

    // =========================================================
    // Require a finite normalized setting.
    private static void ValidateUnit(float value, string label)
    {
        if (!float.IsFinite(value) || value < 0f || value > 1f)
            throw new InvalidOperationException(
                $"{label} must be between zero and one.");
    }

    #endregion
}
