// Inspector-editable values for the shared event-driven camera shake.
// Keeps environmental, combat and weapon systems ignorant of camera rendering.
using Godot;
using System;

[Tool, GlobalClass]
public partial class CameraShakeSettings : Resource
{
    #region General
    [ExportGroup("General")]
    [Export] public bool Enabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,2,0.05")]
    public float StrengthMultiplier { get; set; } = 1f;
    [Export(PropertyHint.Range, "0.05,1,0.05")]
    public float MaximumStrength { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,30,0.5")]
    public float MaximumOffsetPixels { get; set; } = 6f;
    #endregion

    #region Motion
    [ExportGroup("Motion")]
    [Export(PropertyHint.Range, "1,40,0.5")]
    public float ShakeFrequencyHz { get; set; } = 15f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float FadeInSeconds { get; set; } = 0.03f;
    [Export(PropertyHint.Range, "0.05,1,0.05")]
    public float FadeOutFraction { get; set; } = 0.45f;
    #endregion

    #region Combining And Distance
    [ExportGroup("Combining And Distance")]
    [Export(PropertyHint.Range, "1,32,1")]
    public int MaximumConcurrentRequests { get; set; } = 12;
    [Export(PropertyHint.Range, "0.25,4,0.05")]
    public float DistanceFalloffPower { get; set; } = 1.4f;
    #endregion

    #region Validation
    // =========================================================
    // Invalid resources disable only this visual effect, never gameplay.
    public void Validate()
    {
        if (!float.IsFinite(StrengthMultiplier) || StrengthMultiplier < 0f ||
            !float.IsFinite(MaximumStrength) || MaximumStrength <= 0f ||
            !float.IsFinite(MaximumOffsetPixels) || MaximumOffsetPixels < 0f ||
            !float.IsFinite(ShakeFrequencyHz) || ShakeFrequencyHz <= 0f ||
            !float.IsFinite(FadeInSeconds) || FadeInSeconds < 0f ||
            !float.IsFinite(FadeOutFraction) || FadeOutFraction <= 0f ||
            FadeOutFraction > 1f ||
            !float.IsFinite(DistanceFalloffPower) || DistanceFalloffPower <= 0f ||
            MaximumConcurrentRequests < 1 || MaximumConcurrentRequests > 128)
            throw new InvalidOperationException("Invalid CameraShakeSettings.");
    }
    #endregion
}
