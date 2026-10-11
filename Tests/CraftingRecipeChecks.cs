// Run this standalone scene with F6 after building the project.
using Godot;
using System;
using System.IO;
using System.Linq;

public partial class CraftingRecipeChecks : Node
{
    public override void _Ready()
    {
        var items = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
        items.Initialize();
        var catalog = GD.Load<CraftingCatalog>("res://SYSTEMS/Crafting/CraftingCatalog.tres");
        catalog.Validate(items);
        catalog.Validate(items);
        Check(!catalog.IncludeBuiltInRecipes && catalog.Recipes.Count == 2, "External catalog is repeatable");
        var cube = catalog.Recipes.Single(r => r.Id == "test_cube");
        Check(cube.Category == "Building" && cube.Output == items.Get("test_cube") &&
            cube.OutputCount == 1 && cube.DurationSeconds == 2.0 && cube.Ingredients.Count == 1 &&
            cube.Ingredients[0].ItemId == "scrap_metal" && cube.Ingredients[0].Count == 2,
            "Legacy cube semantics");
        var fiber = catalog.Recipes.Single(r => r.Id == "plant_fiber_from_bark");
        Check(fiber.Output == items.Get("plant_fiber") && fiber.OutputCount == 1 &&
            fiber.Ingredients.Count == 1 && fiber.Ingredients[0].ItemId == "bark" &&
            fiber.Ingredients[0].Count == 1, "Bark example");
        ExpectInvalid(() => new CraftingCatalog { IncludeBuiltInRecipes = true }.Validate(items));
        var duplicates = new CraftingCatalog();
        duplicates.Recipes.Add(cube); duplicates.Recipes.Add(cube);
        ExpectInvalid(() => duplicates.Validate(items));
        var bad = Copy(cube);
        bad.Output = null; ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.Output = new ItemDefinition { Id = "unregistered" };
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.Output = (ItemDefinition)cube.Output.Duplicate();
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.Ingredients[0].ItemId = "unregistered";
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.Ingredients.Add(new CraftingIngredient { ItemId = "scrap_metal", Count = 1 });
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.Ingredients[0].Count = 1000;
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.OutputCount = 1000;
        ExpectInvalid(() => bad.Validate(items));
        bad = Copy(cube); bad.DurationSeconds = double.NaN;
        ExpectInvalid(() => bad.Validate(items));

        // Exercise the existing restore/capture/cancel API without running a player scene.
        var crafting = new PlayerCrafting { Catalog = catalog };
        var saved = new PlayerSaveData();
        saved.Crafting.Add(new CraftSaveData { Recipe = "test_cube", Remaining = 3, Elapsed = 1.25 });
        saved.Crafting.Add(new CraftSaveData { Recipe = "plant_fiber_from_bark", Remaining = 2, Elapsed = 0 });
        crafting.RestoreSave(saved);
        Check(crafting.Jobs.Count == 2 && crafting.Jobs[0].Recipe == cube &&
            crafting.Jobs[0].Remaining == 3 && crafting.Jobs[0].Elapsed == 1.25, "Restore ID, batch and elapsed progress");
        var captured = new PlayerSaveData(); crafting.CaptureSave(captured);
        Check(captured.Crafting[0].Recipe == "test_cube" && captured.Crafting[0].Remaining == 3 &&
            captured.Crafting[0].Elapsed == 1.25, "Capture roundtrip");
        crafting.Cancel(0);
        Check(crafting.Jobs.Count == 1 && crafting.Jobs[0].Recipe == fiber, "Cancellation");
        saved.Crafting[0].Recipe = "missing_recipe";
        try { crafting.RestoreSave(saved); throw new Exception("Missing saved recipe accepted"); }
        catch (InvalidDataException) { }
        Check(crafting.Jobs.Count == 1 && crafting.Jobs[0].Recipe == fiber, "Failed restore preserves current queue");
        crafting.Free();
        GD.Print("PASS: external recipe validation and saved queue checks. Inventory/gameplay checks remain manual.");
        GetTree().Quit();
    }

    private static CraftingRecipe Copy(CraftingRecipe source) => new()
    {
        Id = source.Id, Category = source.Category, Output = source.Output,
        OutputCount = source.OutputCount, DurationSeconds = source.DurationSeconds,
        Ingredients = new Godot.Collections.Array<CraftingIngredient>
        { new CraftingIngredient { ItemId = source.Ingredients[0].ItemId, Count = source.Ingredients[0].Count } }
    };
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid recipe/catalog accepted");
    }
}
