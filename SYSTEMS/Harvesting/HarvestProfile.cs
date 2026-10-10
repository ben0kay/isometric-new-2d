// Holds independent reward entries and compiles them against the world's item catalog.
// An empty profile intentionally yields nothing; a missing profile is a configuration error.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class HarvestProfile : Resource
{
    #region Configuration
    [ExportGroup("Drops")]
    [Export] public Godot.Collections.Array<HarvestDropEntry> Drops
        { get; set; } = new();
    #endregion

    #region Validation
    // =========================================================
    // Validate all entries, even disabled rewards, before preparing any source's payout.
    public HarvestDropPlan Compile(ItemCatalog catalog)
    {
        string label = string.IsNullOrEmpty(ResourcePath) ? "HarvestProfile" : ResourcePath;
        if (catalog == null || Drops == null || Drops.Count > 64)
            throw new InvalidOperationException($"{label}: requires a catalog and at most 64 drops.");

        HashSet<string> ids = new(StringComparer.Ordinal);
        List<HarvestDropPlan.Entry> entries = new();
        foreach (HarvestDropEntry drop in Drops)
        {
            if (drop == null || string.IsNullOrWhiteSpace(drop.ItemId) ||
                drop.MinimumCount < 1 || drop.MaximumCount < drop.MinimumCount ||
                drop.MaximumCount > 100000 || !double.IsFinite(drop.Chance) ||
                drop.Chance < 0.0 || drop.Chance > 1.0 || !ids.Add(drop.ItemId))
                throw new InvalidOperationException($"{label}: invalid or duplicate harvest entry '{drop?.ItemId}'.");

            ItemDefinition item = catalog.Get(drop.ItemId);
            if (item == null)
                throw new InvalidOperationException($"{label}: unknown catalog item '{drop.ItemId}'.");
            entries.Add(new HarvestDropPlan.Entry(
                item, drop.MinimumCount, drop.MaximumCount, drop.Chance));
        }
        return new HarvestDropPlan(entries.ToArray());
    }
    #endregion
}
