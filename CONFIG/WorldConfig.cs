// Holds world-wide loot frequency and terrain slope rules.
// Inherits shared visibility settings from GlobalConfig.
using Godot;
using System;

public partial class WorldConfig : GlobalConfig
{
    #region Defaults
    public const float DefaultSlopeAngle = 30f;
    public const float DefaultSlopeHeightScale = 1f;
    #endregion

    #region Loot
    [ExportGroup("LOOT")]
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float SpawnFrequencyMultiplier { get; set; } = 1f;
    #endregion

    #region Terrain Slopes
    [ExportGroup("TERRAIN SLOPES")]
    [Export] public bool TerrainSlopesEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "5,85,1")]
    public float MaxWalkableSlopeAngle { get; set; } = DefaultSlopeAngle;

    [Export(PropertyHint.Range, "0.1,8,0.1")]
    public float SlopeHeightScale { get; set; } = DefaultSlopeHeightScale;
    #endregion

    #region Lookup
    // =========================================================
    // Locate optional CONFIG without relying on scene ready order.
    public static WorldConfig TryFind(Node context)
    {
        for (Node ancestor = context; ancestor != null;
             ancestor = ancestor.GetParent())
        {
            WorldConfig config =
                ancestor.GetNodeOrNull<WorldConfig>("CONFIG");
            if (config != null) return config;
        }
        return null;
    }

    // =========================================================
    // Require CONFIG for systems using explicit world configuration.
    public static WorldConfig Find(Node context)
    {
        return TryFind(context) ?? throw new InvalidOperationException(
            "World requires a CONFIG node using WorldConfig.cs.");
    }
    #endregion

    #region Loot Frequency
    // =========================================================
    // Scale placement attempts with deterministic fractional rounding.
    public int GetLootSpawnAttempts(int baseline, ulong seed)
    {
        float multiplier = SpawnFrequencyMultiplier;

        if (!float.IsFinite(multiplier) || multiplier < 0f)
            throw new InvalidOperationException(
                "Loot spawn frequency must be finite and non-negative.");

        double desired = Math.Max(0, baseline) * (double)multiplier;

        if (desired > int.MaxValue - 1)
            throw new InvalidOperationException(
                "Loot spawn frequency produces too many attempts.");

        int count = (int)Math.Floor(desired);
        double fraction = desired - count;

        if (fraction > 0.0)
        {
            using RandomNumberGenerator rng = new();
            rng.Seed = seed;
            if (rng.Randf() < fraction) count++;
        }
        return count;
    }
    #endregion
}