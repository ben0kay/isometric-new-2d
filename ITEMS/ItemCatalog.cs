// Resolves centrally configured item entries and creates shared fallback icons.
// Each item may override its icon with an Inspector-assigned texture.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class ItemCatalog : Resource
{
#region Configuration
[Export] public Godot.Collections.Array<ItemDefinition> Items
    { get; set; } = new();

[Export] public Godot.Collections.Array<ItemCatalog> Categories
    { get; set; } = new();
#endregion

    #region State
    private readonly Dictionary<string, ItemDefinition> _items = new();
    #endregion

#region Lookup
// =========================================================
// Flatten the master and category catalogs into one runtime lookup.
public void Initialize()
{
    _items.Clear();
    RegisterCatalog(this, new HashSet<ItemCatalog>());
}

// =========================================================
// Register nested entries while rejecting cycles and duplicate item IDs.
private void RegisterCatalog(
    ItemCatalog catalog, HashSet<ItemCatalog> visiting)
{
    if (catalog == null || !visiting.Add(catalog))
        throw new InvalidOperationException(
            "Item catalogs contain a missing category or circular reference.");

    foreach (ItemDefinition item in catalog.Items)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id) ||
            item.MaxStack < 1 ||
            !float.IsFinite(item.WeightKg) || item.WeightKg < 0f ||
            !float.IsFinite(item.VolumeLitres) || item.VolumeLitres < 0f)
            throw new InvalidOperationException("Invalid item catalog entry.");

        item.Consumable?.Validate();

        if (item.Consumable != null && item.Attack != null)
            throw new InvalidOperationException(
                $"Item '{item.Id}' cannot attack and consume on the same input.");

        if (!_items.TryAdd(item.Id, item))
            throw new InvalidOperationException(
                $"Duplicate item ID: {item.Id}");

        if (item.Icon == null)
            item.Icon = item.Id == "alien_berry"
                ? AlienBerryDrawing.GetTexture()
                : CreateIcon(item.Id);
    }

    foreach (ItemCatalog category in catalog.Categories)
        RegisterCatalog(category, visiting);

    visiting.Remove(catalog);
}

// =========================================================
// Return an item directly from the initialized master lookup.
public ItemDefinition Get(string id)
{
    return _items.TryGetValue(id, out ItemDefinition item) ? item : null;
}
#endregion

    #region Placeholder Artwork
// =========================================================
// Generate shared material icons; assigned textures can replace them later.
private static Texture2D CreateIcon(string id)
{
    string shapes = id switch
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

    using Image image = new();
    Error error = image.LoadSvgFromString(
        "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'>" +
        shapes + "</svg>");
    if (error != Error.Ok)
        throw new InvalidOperationException($"Item icon failed: {id}, {error}");
    return ImageTexture.CreateFromImage(image);
}
    #endregion
}