// Defines biome-specific frequency and spatial distribution for one object family.
// Species artwork, dimensions and selection weights remain in their existing resources.
using Godot;
using System;

public enum BiomeDistribution
{
    Uniform,
    Clumps
}

[Tool, GlobalClass]
public partial class BiomePlacementSettings : Resource
{
    #region Settings
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float FrequencyMultiplier { get; set; } = 1f;

    [Export] public BiomeDistribution Distribution { get; set; }
        = BiomeDistribution.Uniform;

    [Export(PropertyHint.Range, "1,32,1")]
    public int ClumpsPerChunk { get; set; } = 3;

    [Export(PropertyHint.Range, "0.1,32,0.1")]
    public float ClumpRadiusTiles { get; set; } = 3f;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid settings before generating object positions.
    public void Validate(string context)
    {
        if (!float.IsFinite(FrequencyMultiplier) || FrequencyMultiplier < 0f)
            throw new InvalidOperationException(
                $"{context}: FrequencyMultiplier must be finite and non-negative.");

        if (!Enum.IsDefined(typeof(BiomeDistribution), Distribution))
            throw new InvalidOperationException(
                $"{context}: invalid Distribution.");

        if (ClumpsPerChunk < 1 ||
            !float.IsFinite(ClumpRadiusTiles) || ClumpRadiusTiles <= 0f)
            throw new InvalidOperationException(
                $"{context}: clumps require a positive count and radius.");
    }
    #endregion
}