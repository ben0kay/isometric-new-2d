// Exercises the real resource loader and harvest evaluator in a disposable scene.
using Godot;
using System;
using System.Collections.Generic;

public partial class HarvestProfileChecks : Node
{
    // =========================================================
    // Run focused configuration/evaluation checks and return a useful process exit code.
    public override void _Ready()
    {
        try
        {
            ItemCatalog items = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
            items.Initialize();
            Run(items);
            GD.Print("HarvestProfile checks passed. Full world/save checks still require gameplay.");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError(error.ToString());
            GetTree().Quit(1);
        }
    }

    // =========================================================
    // Verify public profile semantics independently from world-node lifecycle.
    private static void Run(ItemCatalog items)
    {
        HarvestProfile guaranteed = Profile(Drop("carbon", 2, 4), Drop("bark", 3, 3));
        HarvestDropPlan plan = guaranteed.Compile(items);
        for (ulong seed = 0; seed < 512; seed++)
        {
            var rewards = plan.Roll(seed);
            Check(rewards.Count == 2 && rewards[0].Count >= 2 && rewards[0].Count <= 4 &&
                rewards[1].Count == 3, "Independent guaranteed rewards or quantity bounds failed.");
        }
        Check(Profile().Compile(items).Roll(1).Count == 0, "Empty profile should succeed with no rewards.");
        Check(Profile(Drop("bark", 1, 3, 0)).Compile(items).Roll(1).Count == 0, "0% should never drop.");

        HarvestDropPlan optional = Profile(Drop("bark", 1, 3, .75)).Compile(items);
        int successful = 0;
        for (ulong seed = 0; seed < 512; seed++)
        {
            var first = optional.Roll(seed); var retry = optional.Roll(seed);
            Check(first.Count == retry.Count, "Retry changed reward presence.");
            if (first.Count == 0) continue;
            successful++;
            Check(first[0].Item.Id == "bark" && first[0].Count == retry[0].Count &&
                first[0].Count >= 1 && first[0].Count <= 3, "Retry or optional quantity mismatch.");
        }
        Check(successful > 0 && successful < 512, "Optional rewards must sometimes be absent.");

        Reject(() => Profile(Drop("__missing__", 1, 1, 0)).Compile(items));
        Reject(() => Profile(Drop("bark", 1, 1), Drop("bark", 1, 1)).Compile(items));
        Reject(() => Profile(Drop("bark", 4, 2)).Compile(items));
        Reject(() => Profile(Drop("bark", 0, 1)).Compile(items));
        Reject(() => Profile(Drop("bark", 1, 1, double.NaN)).Compile(items));
        Reject(() => Profile(Drop("bark", 1, 1, 1.1)).Compile(items));

        const string carbon = "res://WORLD/Contents/Vegetation/Trees/CarbonTree.tres";
        Check(HarvestRecipeCompatibility.Allows(carbon, "8FFBD4514ED7242FF7B168259DFA2E230D9EBE321D3FD3CC85041898C70E545C"), "Known old recipe migration failed.");
        Check(!HarvestRecipeCompatibility.Allows(carbon, new string('0', 64)), "Unknown recipe was accepted.");
        Check(!HarvestRecipeCompatibility.Allows("res://WORLD/Contents/Rocks/SmallRock.tres", "8FFBD4514ED7242FF7B168259DFA2E230D9EBE321D3FD3CC85041898C70E545C"), "Cross-species fingerprint accepted.");
        ulong identitySeed = HarvestDropPlan.Seed(64, "surface/tree/0/0/1", carbon);
        Check(identitySeed == 611570190029799331UL, "Stable seed contract changed.");
        Check(identitySeed == HarvestDropPlan.Seed(64, "surface/tree/0/0/1", carbon), "Seed changed.");
        Check(identitySeed != HarvestDropPlan.Seed(65, "surface/tree/0/0/1", carbon), "World seed ignored.");
        Check(identitySeed != HarvestDropPlan.Seed(64, "underground_1/tree/0/0/1", carbon), "Layer identity ignored.");

        foreach (string path in SpeciesPaths)
        {
            WorldObjectDefinition species = GD.Load<WorldObjectDefinition>(path);
            Check(species?.HarvestDrops != null, "Species is missing profile: " + path);
            var actual = species.HarvestDrops.Compile(items).Roll(identitySeed);
            Check(actual.Count >= 1 && actual[0].Count == 1, "Original primary quantity changed: " + path);
        }
    }

    // =========================================================
    // Build independent test configurations without altering project resource files.
    private static HarvestDropEntry Drop(string id, int low, int high, double chance = 1) =>
        new() { ItemId = id, MinimumCount = low, MaximumCount = high, Chance = chance };

    // =========================================================
    // Assemble a temporary profile for public API checks.
    private static HarvestProfile Profile(params HarvestDropEntry[] drops)
    {
        HarvestProfile profile = new();
        foreach (var drop in drops) profile.Drops.Add(drop);
        return profile;
    }

    // =========================================================
    // Require configuration errors rather than accepting malformed reward data.
    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected configuration rejection.");
    }

    // =========================================================
    // Fail clearly on an incorrect observable result.
    private static void Check(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private static readonly string[] SpeciesPaths =
    {
        "res://WORLD/Contents/Rocks/Boulder.tres",
        "res://WORLD/Contents/Rocks/CliffRock.tres",
        "res://WORLD/Contents/Rocks/SmallRock.tres",
        "res://WORLD/Contents/Vegetation/Plants/AlienShrub.tres",
        "res://WORLD/Contents/Vegetation/Plants/Frond.tres",
        "res://WORLD/Contents/Vegetation/Trees/CarbonTree.tres",
        "res://WORLD/Generation/Biomes/Definitions/SwampyMarsh/Vegetation/Plants/MarshPlant01/MarshPlant01.tres",
        "res://WORLD/Generation/Biomes/Definitions/SwampyMarsh/Vegetation/Plants/MarshPlant02/MarshPlant02.tres",
        "res://WORLD/Generation/Biomes/Definitions/VerdigrisWilds/Vegetation/Trees/Tree01/Tree01.tres",
        "res://WORLD/Generation/Biomes/Definitions/VerdigrisWilds/Vegetation/Trees/Tree02/Tree02.tres",
        "res://WORLD/Generation/Biomes/Definitions/VerdigrisWilds/Vegetation/Trees/Tree03/Tree03.tres",
        "res://WORLD/Generation/Biomes/Definitions/VerdigrisWilds/Vegetation/Trees/Tree04/Tree04.tres"
    };
}
