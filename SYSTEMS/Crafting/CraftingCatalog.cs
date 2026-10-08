// Registers C# recipes and optional Inspector-assigned recipe resources.
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

    #region State
    private bool _built;
    #endregion

    // =========================================================
    // Add C# recipes once, validate their items and reject duplicate recipe IDs.
    public void Validate(ItemCatalog items)
    {
        if (items == null)
            throw new InvalidOperationException(
                "Crafting requires an initialized item catalog.");

        if (IncludeBuiltInRecipes && !_built)
        {
            PlaceableItems.RegisterRecipes(items, Recipes);
            _built = true;
        }

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