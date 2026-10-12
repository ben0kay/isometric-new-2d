// Tests the actual Pass A item, recipe, placement scene and independent artwork.
using Godot;
using System;
using System.Linq;

public partial class FieldWorkbenchChecks : Node
{
    // =========================================================
    // Run this scene with F6 after importing SVGs and building the project.
    public override void _Ready()
    {
        var items = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
        items.Initialize();
        var recipes = GD.Load<CraftingCatalog>("res://SYSTEMS/Crafting/CraftingCatalog.tres");
        recipes.Validate(items); recipes.Validate(items);
        var item = items.Get("field_workbench");
        Require(item != null && item.Placeable != null && item.Icon != null,
            "Workbench must be a registered placeable with an icon.");
        Require(item.MaxStack == 1 && item.WeightKg == 5f && item.VolumeLitres == 6f,
            "Workbench storage values.");
        var recipe = recipes.Recipes.Single(r => r.Id == "field_workbench");
        Require(recipe.Output == item && recipe.OutputCount == 1 && recipe.DurationSeconds == 20,
            "Canonical workbench recipe output and duration.");
        var expected = new System.Collections.Generic.Dictionary<string, int>
        { ["scrap_metal"] = 6, ["rock"] = 4, ["plant_fiber"] = 5, ["alien_resin"] = 2 };
        Require(recipe.Ingredients.Count == expected.Count && recipe.Ingredients.All(i =>
            expected.TryGetValue(i.ItemId, out int count) && count == i.Count && items.Get(i.ItemId) != null),
            "Actual registered ingredients.");
        item.Placeable.Validate();
        Require(item.Placeable.Cells == Vector2I.One && item.Placeable.CoverHeight == 106,
            "One-cell placement and cover height.");
        var placed = item.Placeable.WorldScene.Instantiate();
        try
        {
            Require(placed is PlacedObject && placed.GetNodeOrNull<Health>("Systems/Health")?.MaxHealth == 150,
                "PlacedObject-compatible world scene with Systems/Health.");
            Require(placed.GetNodeOrNull<Node2D>("Visual") == null,
                "Artwork is supplied by PlacedObject, not duplicated in the world scene.");
        }
        finally { placed.Free(); }
        var artwork = item.Placeable.ArtworkScene.Instantiate<Node2D>();
        try
        {
            Require(artwork.GetNodeOrNull<Sprite2D>("Sprite")?.Texture != null,
                "Replaceable artwork scene has an imported texture.");
        }
        finally { artwork.Free(); }
        var resinSources = new[] {
            "res://WORLD/Contents/Vegetation/Plants/FrondHarvest.tres",
            "res://WORLD/Contents/Vegetation/Plants/AlienShrubHarvest.tres"
        };
        foreach (string path in resinSources)
        {
            var profile = GD.Load<HarvestProfile>(path);
            profile.Compile(items);
            Require(profile.Drops.Any(d => d.ItemId == "alien_resin" && d.MinimumCount >= 1 && d.Chance == 1),
                "Current resin gathering route.");
        }
        GD.Print("PASS: Field Workbench item/recipe/scene/artwork and current resin route. Gameplay and campaign restore checks remain manual.");
        GetTree().Quit();
    }

    // =========================================================
    // Stop the scene with a clear failed contract rather than a silent false result.
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
