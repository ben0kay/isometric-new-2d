// Stores automatically discovered biome resource references.
// Validates stable IDs and returns enabled definitions in deterministic order.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class BiomeCatalog : Resource
{
    #region Definitions
    [Export] public Godot.Collections.Array<BiomeDefinition> Biomes { get; set; }
        = new();
    #endregion

    #region Queries
    // =========================================================
    // Validate all entries and return enabled biomes sorted by their stable IDs.
    public List<BiomeDefinition> GetEnabledBiomes()
    {
        List<BiomeDefinition> enabled = new();
        HashSet<string> ids = new(StringComparer.Ordinal);

        foreach (BiomeDefinition biome in Biomes)
        {
            if (biome == null)
                throw new InvalidOperationException("Biome catalog has a missing entry.");

            string id = biome.Id;
            if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
                throw new InvalidOperationException(
                    $"Biome '{biome.ResourcePath}' requires an ID without surrounding spaces.");
            if (!ids.Add(id))
                throw new InvalidOperationException($"Duplicate biome ID: '{id}'.");

            if (biome.Enabled) enabled.Add(biome);
        }

        if (enabled.Count == 0)
            throw new InvalidOperationException("Enable at least one biome.");

        enabled.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        return enabled;
    }
    #endregion
}