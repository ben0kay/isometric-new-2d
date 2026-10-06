// Holds shared world settings exposed through the world's CONFIG node.
// Generated loot uses one global frequency multiplier across container types.
using Godot;
using System;

public partial class WorldConfig : Node
{
    #region Configuration
    [ExportGroup("LOOT")]
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float SpawnFrequencyMultiplier { get; set; } = 1f;
    #endregion

    #region Lookup
    // =========================================================
    // Find CONFIG in this world's hierarchy without relying on ready order.
    public static WorldConfig Find(Node context)
    {
        for (Node ancestor = context; ancestor != null;
             ancestor = ancestor.GetParent())
        {
            WorldConfig config =
                ancestor.GetNodeOrNull<WorldConfig>("CONFIG");

            if (config != null) return config;
        }

        throw new InvalidOperationException(
            "World requires a CONFIG node using WorldConfig.cs.");
    }
    #endregion

    #region Loot Frequency
    // =========================================================
    // Scale placement attempts with stable rounding for fractional frequencies.
    public int GetLootSpawnAttempts(int baseline, ulong seed)
    {
        float multiplier = SpawnFrequencyMultiplier;

        if (!float.IsFinite(multiplier) || multiplier < 0f)
            throw new InvalidOperationException(
                "Loot spawn frequency must be a finite, non-negative number.");

        double desired = Math.Max(0, baseline) * (double)multiplier;

        if (desired > int.MaxValue - 1)
            throw new InvalidOperationException(
                "Loot spawn frequency produces too many placement attempts.");

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