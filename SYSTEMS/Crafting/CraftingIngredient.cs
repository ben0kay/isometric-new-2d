// Defines one ingredient required by a crafting recipe.
// Item IDs resolve through the existing item catalog.
using Godot;

[Tool, GlobalClass]
public partial class CraftingIngredient : Resource
{
    #region Configuration
    [Export] public string ItemId { get; set; } = "";

    [Export(PropertyHint.Range, "1,999,1")]
    public int Count { get; set; } = 1;
    #endregion
}