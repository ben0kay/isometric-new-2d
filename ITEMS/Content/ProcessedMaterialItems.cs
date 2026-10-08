// Defines manufactured components and salvaged industrial materials.
// Inventory settings and placeholder drawings stay together in this content file.
using Godot;
using System.Collections.Generic;

public static class ProcessedMaterialItems
{
    #region Definitions
    // =========================================================
    // Register existing industrial materials and components.
    public static void Register(List<ItemDefinition> items)
    {
        Add(items, "scrap_metal", "Scrap Metal", "SCRAP",
            20, 0.4f, 0.3f, new Color(0.65f, 0.74f, 0.78f));
        Add(items, "copper_wiring", "Copper Wiring", "WIRE",
            20, 0.1f, 0.15f, new Color(0.85f, 0.55f, 0.3f));
        Add(items, "circuit_board", "Circuit Board", "BOARD",
            10, 0.15f, 0.2f, new Color(0.4f, 0.85f, 0.65f));
    }

    // =========================================================
    // Combine a component's inventory settings with its placeholder drawing.
    private static void Add(
        List<ItemDefinition> items, string id, string name,
        string shortName, int stack, float weight, float volume, Color tint)
    {
        items.Add(ItemArtwork.Create(
            id, name, shortName, stack, weight, volume, tint, Drawing(id)));
    }
    #endregion

    #region Artwork
    // =========================================================
    // Preserve the current placeholder; add individual drawing cases here later.
    private static string Drawing(string id)
    {
        return id switch
        {
            _ =>
                "<path d='M7 26 L13 10 L29 7 L41 21 L34 38 L18 41Z' fill='#8a7261'/>" +
                "<path d='M13 10 L29 7 L41 21 L24 24Z' fill='#c0a187'/>" +
                "<path d='M12 25 L19 18 L25 26 L19 34Z' fill='#cf8c58'/>"
        };
    }
    #endregion
}