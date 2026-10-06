// Defines reusable liquid appearance, wading resistance and exposure damage.
// Individual bodies reference this resource without modifying its shared settings.
using Godot;
using System;

[Tool, GlobalClass]
public partial class LiquidDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "water";
    #endregion

    #region Appearance
    [ExportGroup("Appearance")]
    [Export] public Color SurfaceColour { get; set; } =
        new(0.12f, 0.3f, 0.35f, 1f);
    #endregion

    #region Movement
    [ExportGroup("Movement")]
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float WadingSpeedMultiplier { get; set; } = 0.65f;

    [Export] public float FullResistanceDepthPixels { get; set; } = 8f;
    #endregion

    #region Exposure
    [ExportGroup("Exposure")]
    [Export] public float DamagePerSecond { get; set; }
    [Export] public DamageType ExposureDamageType { get; set; } =
        DamageType.Corrosive;
    [Export] public float MinimumDamageDepthPixels { get; set; } = 0.5f;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid liquid behaviour before a basin is registered.
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            !float.IsFinite(WadingSpeedMultiplier) ||
            WadingSpeedMultiplier < 0f || WadingSpeedMultiplier > 1f ||
            !float.IsFinite(FullResistanceDepthPixels) ||
            FullResistanceDepthPixels <= 0f ||
            !float.IsFinite(DamagePerSecond) || DamagePerSecond < 0f ||
            !float.IsFinite(MinimumDamageDepthPixels) ||
            MinimumDamageDepthPixels < 0f ||
            !Enum.IsDefined(typeof(DamageType), ExposureDamageType))
            throw new InvalidOperationException(
                $"Liquid '{Id}' has invalid movement or exposure settings.");
    }
    #endregion
}