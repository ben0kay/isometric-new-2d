// Checks capability gating, public API rejection and compatibility without creating a gameplay world.
using Godot;
using System;
using System.IO;
using System.Linq;

public partial class StationCraftingChecks : Node
{
    // =========================================================
    // Run with F6 after compiling; manual interaction/transaction/save checks remain necessary.
    public override void _Ready()
    {
        var items = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres"); items.Initialize();
        var catalog = GD.Load<CraftingCatalog>("res://SYSTEMS/Crafting/CraftingCatalog.tres"); catalog.Validate(items);
        var bracket = catalog.Recipes.Single(x => x.Id == "structural_bracket");
        var cube = catalog.Recipes.Single(x => x.Id == "test_cube");
        var definition = GD.Load<StationDefinition>("res://SYSTEMS/Crafting/Stations/Definitions/FieldWorkbenchStation.tres");
        definition.Validate();
        Check(!bracket.IsHandCraftable && definition.Provides(bracket) && !definition.Provides(cube), "Station versus hand requirements");
        Check(catalog.Recipes.Count(x => x.IsHandCraftable) == 3, "Existing hand recipes remain available");
        Check(bracket.Output == items.Get("structural_bracket") && bracket.Ingredients.Single().ItemId == "scrap_metal" &&
            bracket.Ingredients.Single().Count == 2 && bracket.OutputCount == 1 && bracket.DurationSeconds == 6, "Bracket recipe");
        ExpectInvalid(() => CraftingCapabilities.Validate(new[] { "typo" }, "test"));
        ExpectInvalid(() => CraftingCapabilities.Validate(new[] { "assembly_basic", "assembly_basic" }, "test"));
        ExpectInvalid(() => CraftingCapabilities.Validate(new[] { "ore_reduction" }, "test"));
        Check(!new StationDefinition().Provides(bracket), "Unqualified station rejection");
        var crafting = new PlayerCrafting { Catalog = catalog };
        var inventory = new PlayerInventory();
        Check(!crafting.TryQueue(bracket, 1), "Direct hand queue rejects station recipe before player initialization");
        Check(!inventory.TryCraft(bracket, out _), "Direct hand exchange rejects station recipe");
        Check(!inventory.TryCraftAtStation(bracket, null, out _), "Direct station exchange requires station");
        var saved = new PlayerSaveData();
        saved.Crafting.Add(new CraftSaveData { Recipe = "test_cube", Remaining = 2, Elapsed = 1.25 });
        crafting.RestoreSave(saved);
        var captured = new PlayerSaveData(); crafting.CaptureSave(captured);
        Check(captured.Crafting.Single().Elapsed == 1.25 && captured.Crafting.Single().Remaining == 2, "Old hand progress roundtrip");
        saved.Crafting[0].Recipe = "structural_bracket";
        try { crafting.RestoreSave(saved); throw new Exception("Station recipe restored as hand job"); }
        catch (InvalidDataException) { }
        Check(crafting.Jobs.Single().Recipe == cube, "Rejected restore preserves old queue");
        Check(!StationRecipeCompatibility.Allows("res://unrelated.tscn", "bad"), "Migration is path-restricted");
        Check(!StationRecipeCompatibility.Allows("res://WORLDABLES/Objects/Workstations/FieldWorkbench/FieldWorkbench.tscn", "bad"), "Migration is fingerprint-restricted");
        inventory.Free(); crafting.Free();
        GD.Print("PASS: workstation capability contracts, hand API gates, old queue restore and restricted migration checks.");
        GetTree().Quit();
    }

    // =========================================================
    // Reject invalid definitions rather than silently accepting future or mistyped capabilities.
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid capability accepted.");
    }
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }
}
