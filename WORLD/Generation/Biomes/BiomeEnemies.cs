// Defines a biome's small enemy population and eligible enemy types.
// Selection uses definition weights without hardcoded robot or creature names.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeEnemies : Resource
{
    #region Configuration
    [ExportGroup("Population")]
    [Export(PropertyHint.Range, "0,8,1")]
    public int MinimumPerChunk { get; set; }
    [Export(PropertyHint.Range, "0,8,1")]
    public int MaximumPerChunk { get; set; } = 2;

    [ExportGroup("Species")]
    [Export] public Godot.Collections.Array<EnemyDefinition> Definitions { get; set; } = new();
    #endregion

    #region Selection
    // =========================================================
    // Choose one eligible definition using its relative spawn weight.
    public EnemyDefinition Pick(RandomNumberGenerator rng)
    {
        double total = 0.0;
        foreach (EnemyDefinition definition in Definitions)
            if (definition != null && float.IsFinite(definition.SpawnWeight) &&
                definition.SpawnWeight > 0f)
                total += definition.SpawnWeight;

        if (total <= 0.0) return null;
        double roll = rng.Randf() * total;
        EnemyDefinition last = null;

        foreach (EnemyDefinition definition in Definitions)
        {
            if (definition == null || !float.IsFinite(definition.SpawnWeight) ||
                definition.SpawnWeight <= 0f) continue;
            last = definition;
            roll -= definition.SpawnWeight;
            if (roll <= 0.0) return definition;
        }
        return last;
    }

    // =========================================================
    // Read a bounded population count for one seeded chunk.
    public int GetCount(RandomNumberGenerator rng)
    {
        int minimum = Math.Clamp(MinimumPerChunk, 0, 8);
        int maximum = Math.Clamp(MaximumPerChunk, minimum, 8);
        return rng.RandiRange(minimum, maximum);
    }
    #endregion
}