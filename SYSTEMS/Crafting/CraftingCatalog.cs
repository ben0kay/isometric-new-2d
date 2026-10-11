// Registers explicit external recipes and rejects duplicate recipe identities.
// Crafting execution and player queue state remain in the existing shared systems.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class CraftingCatalog : Resource
{
    #region Configuration
    [Export] public bool IncludeBuiltInRecipes { get; set; }

    [Export] public Godot.Collections.Array<CraftingRecipe> Recipes
        { get; set; } = new();
    #endregion

    // =========================================================
    // Validate resource content without appending or duplicating recipes.
    public void Validate(ItemCatalog items)
    {
        if (items == null)
            throw new InvalidOperationException(
                "Crafting requires an initialized item catalog.");

        if (IncludeBuiltInRecipes)
            throw new InvalidOperationException(
                "Built-in recipe registration is retired. Register external recipes and disable IncludeBuiltInRecipes.");

        if (Recipes == null)
            throw new InvalidOperationException("Crafting catalog has no recipe array.");

        HashSet<string> seen = new();

        foreach (CraftingRecipe recipe in Recipes)
        {
            if (recipe == null)
                throw new InvalidOperationException(
                    "Crafting catalog contains an empty recipe.");

            recipe.Validate(items);

            if (!seen.Add(recipe.Id))
                throw new InvalidOperationException(
                    $"Duplicate crafting recipe: '{recipe.Id}'.");
        }
    }
}
