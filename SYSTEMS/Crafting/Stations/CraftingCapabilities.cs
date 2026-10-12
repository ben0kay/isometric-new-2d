// Central capability identities; reserved future capabilities fail clearly until implemented.
using System;
using System.Collections.Generic;

public static class CraftingCapabilities
{
    public const string BasicAssembly = "assembly_basic";
    private static readonly HashSet<string> Reserved = new(StringComparer.Ordinal)
        { "repair_basic", "salvage_processing", "ore_reduction", "metalworking", "electronics", "advanced_fabrication" };

    // =========================================================
    // Validate identifiers without silently accepting spelling mistakes or duplicate requirements.
    public static void Validate(string[] capabilities, string context, bool required = false)
    {
        if (capabilities == null || capabilities.Length > 16 || (required && capabilities.Length == 0))
            throw new InvalidOperationException(context + ": assign a valid capability array.");
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (string id in capabilities)
        {
            if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                throw new InvalidOperationException(context + ": missing or repeated capability.");
            if (Reserved.Contains(id))
                throw new InvalidOperationException(context + ": capability is reserved but not implemented: " + id);
            if (id != BasicAssembly)
                throw new InvalidOperationException(context + ": unknown capability: " + id);
        }
    }
}
