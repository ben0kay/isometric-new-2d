// Registers explicit item and category resources into one cached lookup.
// Item content lives in .tres; runtime stack quantities remain in inventory.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class ItemCatalog : Resource
{
    #region Configuration
    [Export] public bool IncludeBuiltInItems { get; set; }

    [Export] public Godot.Collections.Array<ItemDefinition> Items
        { get; set; } = new();

    [Export] public Godot.Collections.Array<ItemCatalog> Categories
        { get; set; } = new();
    #endregion

    #region State
    private readonly Dictionary<string, ItemDefinition> _items = new();
    #endregion

    #region Registration
    // =========================================================
    // Rebuild explicit resource registration; reject retired built-in catalogs.
    public void Initialize()
    {
        if (IncludeBuiltInItems)
            throw new InvalidOperationException(
                "Built-in item registration is retired. Register external items and disable IncludeBuiltInItems.");

        _items.Clear();
        RegisterCatalog(this, new HashSet<ItemCatalog>());
    }

    // =========================================================
    // Register configured resources while rejecting cycles and duplicate entries.
    private void RegisterCatalog(
        ItemCatalog catalog, HashSet<ItemCatalog> visiting)
    {
        if (catalog == null || !visiting.Add(catalog))
            throw new InvalidOperationException(
                "Item catalogs contain a missing category or circular reference.");

        foreach (ItemDefinition item in catalog.Items)
            RegisterItem(item);

        foreach (ItemCatalog category in catalog.Categories)
            RegisterCatalog(category, visiting);

        visiting.Remove(catalog);
    }

    // =========================================================
    // Validate shared item settings and prepare an icon only when missing.
    private void RegisterItem(ItemDefinition item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id) ||
            item.MaxStack < 1 ||
            !float.IsFinite(item.WeightKg) || item.WeightKg < 0f ||
            !float.IsFinite(item.VolumeLitres) || item.VolumeLitres < 0f)
            throw new InvalidOperationException("Invalid item catalog entry.");

        item.Consumable?.Validate();
        item.Placeable?.Validate();

        int useModes = (item.Attack != null ? 1 : 0) +
            (item.Consumable != null ? 1 : 0) +
            (item.Placeable != null ? 1 : 0);

        if (useModes > 1)
            throw new InvalidOperationException(
                $"Item '{item.Id}' has conflicting primary-use capabilities.");

        if (!_items.TryAdd(item.Id, item))
            throw new InvalidOperationException(
                $"Duplicate item ID: {item.Id}");

        if (item.Icon != null) return;

        item.Icon ??= ItemArtwork.Fallback();
    }
    #endregion

    #region Lookup
    // =========================================================
    // Resolve an item directly from the initialized master lookup.
    public ItemDefinition Get(string id)
    {
        return id != null && _items.TryGetValue(id, out ItemDefinition item)
            ? item : null;
    }
    #endregion
}
