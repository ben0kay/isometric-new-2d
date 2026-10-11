// Baseline contract fixtures. Update intentional content changes alongside these expectations.
using Godot;
using System;
using System.Linq;
using System.Text.Json;

public partial class ContentDiscoveryChecks : Node
{
    public override void _Ready()
    {
        var report = ContentDiscovery.Build();
        foreach (var error in report.Diagnostics.Where(x => x.Severity == "error"))
            GD.PushError(error.Path + " / " + error.Field + ": " + error.Message);
        Check(report.Valid, "Current resource validation");
        Check(report.Items.Count(x => x.Registered) == 21, "Twenty-one registered items including the workbench");
        var bark = report.Items.Single(x => x.Id == "bark" && x.Registered);
        Check(bark.SourcePaths.Count == 2, "Bark's two source species");
        Check(bark.ConsumedBy.Any(x => x.EndsWith("PlantFiberFromBarkRecipe.tres")), "Bark recipe reverse lookup");
        var barkSources = report.Sources.Where(x => bark.SourcePaths.Contains(x.Path)).ToArray();
        Check(barkSources.All(x => x.Kind == "tree" && x.Rewards.Any(r => r.ItemId == "bark" &&
            r.MinimumCount == 1 && r.MaximumCount == 3 && r.Chance == 0.75)), "Bark drop quantities/chance");
        var iron = report.Sources.Single(x => x.Kind == "ore" && x.Id == "iron_deposit");
        Check(iron.Rewards.Any(x => x.ItemId == "iron_ore" && x.Chance == 1) &&
            iron.Rewards.Any(x => x.ItemId == "rock" && x.MinimumCount == 1 && x.MaximumCount == 2 && x.Chance == 0.35), "Ore primary and bonus contexts");
        Check(report.Sources.Count(x => x.Kind == "ground") == 2 &&
            report.Sources.Where(x => x.Kind == "ground").All(x => x.Rewards.Single().MaximumCount == 1), "Ground one-unit yield");
        Check(report.Recipes.Count(x => x.Registered) == 3, "Three external recipes including the workbench");
        Check(report.Sources.Any(x => x.Biomes.Any(b => b.BiomeId == "verdigris_wilds" && b.ConfiguredForSelection)), "Wilds biome bindings");
        var second = ContentDiscovery.Build();
        Check(JsonSerializer.Serialize(report) == JsonSerializer.Serialize(second), "Repeatable report without registration growth");
        Check(ContentReportExport.Markdown(report).Contains("Bark"), "Readable report");

        var item = new ItemDefinition { Id = "fixture", MaxStack = 1 };
        var catalog = new ItemCatalog(); catalog.Items.Add(item); catalog.Initialize();
        var badProfile = new HarvestProfile();
        badProfile.Drops.Add(new HarvestDropEntry { ItemId = "fixture", Chance = double.NaN });
        ExpectInvalid(() => badProfile.Compile(catalog));
        badProfile.Drops[0].Chance = 1;
        badProfile.Drops.Add(new HarvestDropEntry { ItemId = "fixture" });
        ExpectInvalid(() => badProfile.Compile(catalog));
        var missing = new HarvestProfile(); missing.Drops.Add(new HarvestDropEntry { ItemId = "missing" });
        ExpectInvalid(() => missing.Compile(catalog));
        var cycle = new ItemCatalog(); cycle.Categories.Add(cycle);
        ExpectInvalid(cycle.Initialize); cycle.Categories.Clear();
        var parent = new ItemCatalog(); parent.Categories.Add(new ItemCatalog { IncludeBuiltInItems = true });
        ExpectInvalid(parent.Initialize);
        var duplicate = new ItemCatalog(); duplicate.Items.Add(item); duplicate.Items.Add(item);
        ExpectInvalid(duplicate.Initialize);
        GD.Print("PASS: content discovery, reverse lookup, baseline resource relationships and validation fixtures.");
        GetTree().Quit();
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception(label); }
    private static void ExpectInvalid(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Invalid fixture accepted");
    }
}
