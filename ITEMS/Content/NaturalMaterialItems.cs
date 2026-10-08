// Defines gathered natural materials and their primitive inventory artwork.
// Stable item IDs are shared by harvesting, mining, wildlife loot and recipes.
using Godot;
using System.Collections.Generic;

public static class NaturalMaterialItems
{
    #region Definitions
    // =========================================================
    // Register materials gathered from terrain, vegetation and wildlife.
    public static void Register(List<ItemDefinition> items)
    {
        Add(items, "rock", "Rock", "ROCK",
            20, 0.5f, 0.25f, new Color(0.75f, 0.82f, 0.85f));
        Add(items, "carbon", "Carbon", "CARBON",
            20, 0.25f, 0.3f, new Color(0.65f, 0.75f, 0.8f));
        Add(items, "plant_fiber", "Plant Fiber", "FIBER",
            40, 0.05f, 0.15f, new Color(0.7f, 0.85f, 0.5f));
        Add(items, "iron_ore", "Iron Ore", "IRON",
            20, 0.6f, 0.35f, new Color(0.85f, 0.68f, 0.48f));
        Add(items, "alien_resin", "Alien Resin", "RESIN",
            30, 0.1f, 0.1f, new Color(0.95f, 0.65f, 0.25f));
        Add(items, "biomass", "Biomass", "BIOMASS",
            30, 0.2f, 0.3f, new Color(0.45f, 0.85f, 0.4f));
        Add(items, "ice", "Ice", "ICE",
            20, 0.5f, 0.55f, new Color(0.55f, 0.9f, 1f));
        Add(items, "sand", "Sand", "SAND",
            30, 0.5f, 0.3f, new Color(0.85f, 0.72f, 0.48f));
        Add(items, "clay", "Clay", "CLAY",
            30, 0.5f, 0.3f, new Color(0.75f, 0.46f, 0.32f));
        Add(items, "tallow", "Tallow", "TALLOW",
            30, 0.15f, 0.18f, new Color(0.84f, 0.85f, 0.68f));
    }

    // =========================================================
    // Combine a material's inventory settings with its placeholder drawing.
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
    // Preserve existing natural material drawings and fallback appearance.
    private static string Drawing(string id)
    {
        return id switch
        {
            "rock" =>
                "<path d='M6 24 L11 11 L24 6 L38 16 L40 32 L27 41 L12 36Z' fill='#74838b'/>" +
                "<path d='M11 11 L24 6 L38 16 L25 22Z' fill='#b1bdbd'/>" +
                "<path d='M25 22 L38 16 L40 32 L27 41Z' fill='#485963'/>",

            "carbon" =>
                "<path d='M7 26 L13 10 L29 7 L41 21 L34 38 L18 41Z' fill='#26333d'/>" +
                "<path d='M13 10 L29 7 L41 21 L24 24Z' fill='#52636e'/>" +
                "<path d='M24 24 L41 21 L34 38 L18 41Z' fill='#15232e'/>",

            "plant_fiber" =>
                "<path d='M13 39 Q5 18 15 6 M21 40 Q30 21 21 5 M29 39 Q40 19 35 9' " +
                "fill='none' stroke='#a8bf77' stroke-width='5'/>" +
                "<path d='M11 29 L33 30 M12 33 L32 34' stroke='#d5ae72' stroke-width='3'/>",

            "alien_resin" =>
                "<path d='M24 5 C21 15 9 22 9 31 C9 45 39 45 39 31 " +
                "C39 22 28 15 24 5Z' fill='#d99632' stroke='#ffcc64' stroke-width='2'/>" +
                "<path d='M18 24 Q12 33 19 36' fill='none' stroke='#ffe4a0' stroke-width='3'/>",

            "biomass" =>
                "<path d='M8 30 Q4 19 16 17 Q14 6 27 9 Q37 7 39 20 " +
                "Q47 29 36 36 Q30 45 20 38 Q8 42 8 30Z' " +
                "fill='#4b8f49' stroke='#91c969' stroke-width='2'/>" +
                "<path d='M13 28 Q24 15 35 26 M19 34 Q28 25 34 33' " +
                "fill='none' stroke='#b4dd7c' stroke-width='2'/>",

            "ice" =>
                "<path d='M10 15 L28 5 L41 17 L36 37 L17 43 L6 30Z' " +
                "fill='#79cce8' stroke='#d0faff' stroke-width='2'/>" +
                "<path d='M10 15 L24 23 L41 17 M24 23 L17 43 M28 5 L24 23' " +
                "fill='none' stroke='#e6ffff' stroke-width='2'/>" +
                "<path d='M11 19 L19 24 L12 30Z' fill='#c7f5ff'/>",

            "sand" =>
                "<path d='M4 37 Q12 30 17 20 Q21 12 26 22 Q33 30 44 37Z' " +
                "fill='#c7aa73' stroke='#ecd3a0' stroke-width='2'/>" +
                "<circle cx='19' cy='31' r='1.5' fill='#8c7043'/>" +
                "<circle cx='29' cy='34' r='1.5' fill='#8c7043'/>",

            "clay" =>
                "<path d='M6 31 L12 17 L28 11 L41 23 L35 39 L17 41Z' fill='#9c6248'/>" +
                "<path d='M12 17 L28 11 L41 23 L24 27Z' fill='#c68a64'/>" +
                "<path d='M24 27 L41 23 L35 39 L17 41Z' fill='#754431'/>",

            _ =>
                "<path d='M7 26 L13 10 L29 7 L41 21 L34 38 L18 41Z' fill='#8a7261'/>" +
                "<path d='M13 10 L29 7 L41 21 L24 24Z' fill='#c0a187'/>" +
                "<path d='M12 25 L19 18 L25 26 L19 34Z' fill='#cf8c58'/>"
        };
    }
    #endregion
}