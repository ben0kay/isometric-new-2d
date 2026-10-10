// Run this scene independently after building C#. No campaign data is written.
using Godot;
using System;
using System.Linq;

public partial class OreBatchChecks : Node
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Reject(Action action)
    {
        try { action(); } catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid ore settings to be rejected.");
    }
    public override void _Ready()
    {
        try
        {
            ItemCatalog catalog = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
            catalog.Initialize();
            OreDefinition live = GD.Load<OreDefinition>("res://WORLD/Contents/Ores/Iron/IronDeposit.tres");
            new OreBatchPlan(live, catalog);
            GroundResourceCatalog ground = GD.Load<GroundResourceCatalog>(
                "res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres");
            ground.Validate(catalog);
            OreDefinition definition = new()
            {
                YieldItemId = "iron_ore", TotalUnits = 7, UnitsPerBatch = 3,
                BonusDrops = new HarvestProfile()
            };
            definition.BonusDrops.Drops.Add(new HarvestDropEntry
            {
                ItemId = "rock", MinimumCount = 1, MaximumCount = 2, Chance = 1
            });
            OreBatchPlan guaranteed = new(definition, catalog);
            foreach (int remaining in new[] { 7, 4, 1 })
            {
                var rewards = guaranteed.Prepare(64, "surface/ore/0/0/1", live.ResourcePath,
                    7, remaining, 3);
                Check(rewards.Count == 2 && rewards[0].Count == Math.Min(3, remaining),
                    "Primary final partial batch was changed.");
                Check(rewards[1].Count >= 1 && rewards[1].Count <= 2,
                    "Bonus range invalid.");
            }
            definition.BonusDrops.Drops[0].Chance = 0.35;
            OreBatchPlan optional = new(definition, catalog);
            int extras = 0;
            for (uint seed = 0; seed < 512; seed++)
            {
                var first = optional.Prepare(seed, "surface/ore/0/0/1", live.ResourcePath, 7, 4, 3);
                var retry = optional.Prepare(seed, "surface/ore/0/0/1", live.ResourcePath, 7, 4, 3);
                Check(first.SequenceEqual(retry), "Blocked/reloaded batch rerolled.");
                if (first.Count == 2) extras++;
            }
            Check(extras > 0 && extras < 512, "Optional chance did not vary.");
            definition.BonusDrops.Drops[0].Chance = 0;
            Check(new OreBatchPlan(definition, catalog).Prepare(64, "ore", live.ResourcePath,
                7, 4, 3).Count == 1, "0% bonus paid out.");
            definition.BonusDrops = null;
            Check(new OreBatchPlan(definition, catalog).Prepare(64, "ore", live.ResourcePath,
                7, 1, 3)[0].Count == 1, "Null bonus changed primary yield.");
            definition.YieldItemId = "missing-item";
            Reject(() => new OreBatchPlan(definition, catalog));
            definition.YieldItemId = "iron_ore";
            definition.WorkPerBatch = float.NaN;
            Reject(() => new OreBatchPlan(definition, catalog));
            definition.WorkPerBatch = 18;
            definition.BonusDrops = new HarvestProfile();
            definition.BonusDrops.Drops.Add(new HarvestDropEntry { ItemId = "iron_ore" });
            Reject(() => new OreBatchPlan(definition, catalog));
            Check(!OreRecipeCompatibility.Allows(live.ResourcePath, new string('0', 64)),
                "Unreviewed save fingerprint accepted.");
            GD.Print("Phase 4 ore batch checks passed.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError("Phase 4 checks failed: " + error);
            GetTree().Quit(1);
        }
    }
}
