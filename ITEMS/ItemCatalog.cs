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
    #endregion

    #region State
    private readonly Dictionary<string, ItemDefinition> _items = new();
    #endregion

    #region Lookup
    // =========================================================
    // Validate and index the catalog once per world initialization.
    public void Initialize()
    {
        _items.Clear();
        foreach (ItemDefinition item in Items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id) ||
                item.MaxStack < 1 || item.WeightKg < 0f ||
                item.VolumeLitres < 0f)
                throw new InvalidOperationException("Invalid item catalog entry.");

            if (!_items.TryAdd(item.Id, item))
                throw new InvalidOperationException($"Duplicate item ID: {item.Id}");

            if (item.Icon == null) item.Icon = CreateIcon(item.Id);
        }
    }

    // =========================================================
    // Return the canonical item shared by drops and inventory.
    public ItemDefinition Get(string id)
    {
        return _items.TryGetValue(id, out ItemDefinition item) ? item : null;
    }
    #endregion

    #region Placeholder Artwork
    // =========================================================
    // Generate one small shared texture; imported icons can replace this later.
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