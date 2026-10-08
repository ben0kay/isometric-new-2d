// Registers recipe resources without containing their individual recipe data.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class CraftingCatalog : Resource
{
    #region Configuration
    [Export] public Godot.Collections.Array<CraftingRecipe> Recipes
        { get; set; } = new();
    #endregion

    // =========================================================
    // Validate registered recipes and reject duplicate recipe IDs.
    public void Validate(ItemCatalog items)
    {
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