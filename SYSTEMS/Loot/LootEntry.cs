// Defines one weighted item and its possible quantity in a loot table.
using Godot;
using System;

[Tool, GlobalClass]
public partial class LootEntry : Resource
{
    #region Configuration
    [Export] public string ItemId { get; set; } = "";
    [Export] public float Weight { get; set; } = 1f;
    [Export] public int MinimumCount { get; set; } = 1;
    [Export] public int MaximumCount { get; set; } = 1;
    #endregion

    #region Validation
    // =========================================================
    // Validate the entry against the world's central item catalog.
    public void Validate(ItemCatalog catalog)
    {
        if (string.IsNullOrWhiteSpace(ItemId) ||
            catalog.Get(ItemId) == null ||
            !float.IsFinite(Weight) || Weight <= 0f ||
            MinimumCount < 1 || MaximumCount < MinimumCount)
            throw new InvalidOperationException(
                $"Invalid loot entry '{ItemId}'. Check item ID, weight and counts.");
    }
    #endregion
}