// Generates seeded loot from weighted entries in the central item catalog.
// Tables contain definitions; generated quantities belong to runtime storage.
using Godot;
using System;

[Tool, GlobalClass]
public partial class LootTable : Resource
{
    #region Configuration
    [Export] public Godot.Collections.Array<LootEntry> Entries
        { get; set; } = new();
    [Export] public int MinimumRolls { get; set; } = 3;
    [Export] public int MaximumRolls { get; set; } = 5;
    #endregion

    #region Generation
    // =========================================================
    // Generate complete contents once without silently discarding overflow.
    public InventoryStorage Generate(
        ItemCatalog catalog, StorageDefinition storage, ulong seed)
    {
        storage.Validate();
        if (Entries == null || Entries.Count == 0 ||
            MinimumRolls < 1 || MaximumRolls < MinimumRolls ||
            MaximumRolls > 128)
            throw new InvalidOperationException("Invalid loot table roll settings.");

        double total = 0;
        foreach (LootEntry entry in Entries)
        {
            if (entry == null)
                throw new InvalidOperationException("Loot entries cannot be empty.");
            entry.Validate(catalog);
            total += entry.Weight;
        }

        InventoryStorage contents = new(storage.SlotCount);
        using RandomNumberGenerator rng = new() { Seed = seed };
        int rolls = rng.RandiRange(MinimumRolls, MaximumRolls);

        for (int roll = 0; roll < rolls; roll++)
        {
            double choice = rng.Randf() * total;
            LootEntry selected = Entries[Entries.Count - 1];

            foreach (LootEntry entry in Entries)
            {
                choice -= entry.Weight;
                if (choice > 0) continue;
                selected = entry;
                break;
            }

            ItemDefinition item = catalog.Get(selected.ItemId);
            int count = rng.RandiRange(
                selected.MinimumCount, selected.MaximumCount);

            if (!contents.TryAdd(item, count))
                throw new InvalidOperationException(
                    "Loot exceeds container slots. Increase slots or reduce rolls/counts.");
        }

        contents.GetTotals(out float weight, out float volume);
        if (weight > storage.MaximumWeightKg || volume > storage.CapacityLitres)
            throw new InvalidOperationException(
                "Generated loot exceeds container weight or volume capacity.");

        return contents;
    }
    #endregion
}