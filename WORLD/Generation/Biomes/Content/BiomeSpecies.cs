// References a reusable species with a biome-specific selection weight.
// All families share selection and validation without hardcoded species names.
using Godot;
using System;

[Tool, GlobalClass]
public partial class BiomeSpecies : Resource
{
    #region Configuration
    [Export] public WorldObjectDefinition Definition { get; set; }
    [Export] public float Weight { get; set; } = 1f;
    #endregion

    #region Selection
    // =========================================================
    // Select a family definition using this biome's relative species weights.
    public static T Select<T>(
        Godot.Collections.Array<BiomeSpecies> entries,
        RandomNumberGenerator rng) where T : WorldObjectDefinition
    {
        float total = 0f;
        foreach (BiomeSpecies entry in entries)
            if (entry?.Definition is T && entry.Weight > 0f)
                total += entry.Weight;
        if (total <= 0f) return null;

        float roll = rng.Randf() * total;
        T last = null;
        foreach (BiomeSpecies entry in entries)
        {
            if (entry?.Definition is not T definition || entry.Weight <= 0f)
                continue;
            last = definition;
            roll -= entry.Weight;
            if (roll <= 0f) return definition;
        }
        return last;
    }
    #endregion

    #region Validation
    // =========================================================
    // Reject missing species, wrong families and invalid weights before spawning.
    public static void Validate<T>(
        Godot.Collections.Array<BiomeSpecies> entries,
        string context, bool required) where T : WorldObjectDefinition
    {
        if (entries == null)
            throw new InvalidOperationException($"{context}: species list is missing.");

        float total = 0f;
        foreach (BiomeSpecies entry in entries)
        {
            if (entry?.Definition is not T definition)
                throw new InvalidOperationException(
                    $"{context}: every entry requires a {typeof(T).Name}.");
            if (string.IsNullOrWhiteSpace(definition.Id))
                throw new InvalidOperationException(
                    $"{context}: a species definition has no ID.");
            if (!float.IsFinite(entry.Weight) || entry.Weight < 0f)
                throw new InvalidOperationException(
                    $"{context}: '{definition.Id}' requires a finite, non-negative weight.");

            total += entry.Weight;
        }

        if (!float.IsFinite(total) || (required && total <= 0f))
            throw new InvalidOperationException(
                $"{context}: assign at least one species with a positive weight.");
    }
    #endregion
}