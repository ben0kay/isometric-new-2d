// Optional standalone checks for selection, identity and actual settings.
using Godot;
using System;

public partial class OreSpawnChecks : Node
{
    public override void _Ready()
    {
        try
        {
            ItemCatalog catalog = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
            catalog.Initialize();
            GD.Load<OreSpawnSettings>("res://WORLD/Contents/Ores/DefaultOreSpawnSettings.tres").Validate(catalog);
            int selected = 0;
            for (int y = -50; y < 50; y++)
            for (int x = -50; x < 50; x++)
            {
                Vector2I chunk = new(x, y);
                if (OreSpawner.Selected(chunk, 64, 0) || !OreSpawner.Selected(chunk, 64, 1))
                    throw new InvalidOperationException("0%/100% selection failed.");
                bool first = OreSpawner.Selected(chunk, 64, 0.15);
                if (first != OreSpawner.Selected(chunk, 64, 0.15))
                    throw new InvalidOperationException("Chunk selection is not repeatable.");
                if (first) selected++;
            }
            if (selected < 1000 || selected > 2000)
                throw new InvalidOperationException("Unexpected 15% sample count: " + selected);
            if (OreSpawner.CandidateIdentity(new Vector2I(-2, 3), 4) != "surface/ore/-2/3/4")
                throw new InvalidOperationException("Persistent identity changed.");
            GD.Print("Ore spawn selection checks passed: " + selected + "/10000 chunks.");
            GetTree().Quit(0);
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
