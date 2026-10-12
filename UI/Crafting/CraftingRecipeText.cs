// Shared ingredient presentation for inventory and workstation crafting windows.
using System.Text;

public static class CraftingRecipeText
{
    // =========================================================
    // Read backpack ownership without reserving or spending ingredients.
    public static string Requirements(CraftingRecipe recipe, int quantity, PlayerInventory inventory, ItemCatalog items)
    {
        StringBuilder text = new("INGREDIENTS — OWNED / REQUIRED\n");
        foreach (CraftingIngredient ingredient in recipe.Ingredients)
            text.Append($"\n{items.Get(ingredient.ItemId).DisplayName}: {inventory.GetBagItemCount(ingredient.ItemId)} / {(long)ingredient.Count * quantity}");
        return text.ToString();
    }
}
