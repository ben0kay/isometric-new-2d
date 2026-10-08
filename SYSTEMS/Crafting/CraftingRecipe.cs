// Defines a crafting operation without holding player or queue state.
// Each item can keep its recipe resource beside its artwork and item definition.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class CraftingRecipe : Resource
{
    #region Configuration
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string Category { get; set; } = "General";

    [Export(PropertyHint.MultilineText)]
    public string Description { get; set; } = "";

    [ExportGroup("Output")]
    [Export] public ItemDefinition Output { get; set; }

    [Export(PropertyHint.Range, "1,999,1")]
    public int OutputCount { get; set; } = 1;

    [ExportGroup("Requirements")]
    [Export] public Godot.Collections.Array<CraftingIngredient> Ingredients
        { get; set; } = new();

    [Export(PropertyHint.Range, "0.1,600,0.1")]
    public double DurationSeconds { get; set; } = 2.0;
    #endregion

    #region Validation
    // =========================================================
    // Reject malformed recipes before they enter the player's queue.
    public void Validate(ItemCatalog items)
    {
        if (string.IsNullOrWhiteSpace(Id) ||
            Output == null ||
            string.IsNullOrWhiteSpace(Output.Id) ||
            OutputCount < 1 ||
            Output.MaxStack < 1 ||
            !double.IsFinite(DurationSeconds) ||
            DurationSeconds <= 0.0 ||
            Ingredients == null ||
            Ingredients.Count == 0)
            throw new InvalidOperationException(
                $"Invalid crafting recipe: '{Id}'.");

        if (!float.IsFinite(Output.WeightKg) || Output.WeightKg < 0f ||
            !float.IsFinite(Output.VolumeLitres) || Output.VolumeLitres < 0f)
            throw new InvalidOperationException(
                $"Invalid output storage values in recipe '{Id}'.");

        if (items.Get(Output.Id) == null)
            throw new InvalidOperationException(
                $"Unknown crafting output: '{Output.Id}'.");

        HashSet<string> seen = new();

        foreach (CraftingIngredient ingredient in Ingredients)
        {
            if (ingredient == null ||
                string.IsNullOrWhiteSpace(ingredient.ItemId) ||
                ingredient.Count < 1 ||
                items.Get(ingredient.ItemId) == null ||
                !seen.Add(ingredient.ItemId))
                throw new InvalidOperationException(
                    $"Invalid or repeated ingredient in recipe '{Id}'.");
        }
    }
    #endregion
}