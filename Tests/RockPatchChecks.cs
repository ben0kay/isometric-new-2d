// Run with F6 on a rendering backend: exercises real bake/cache and actor lifetime.
using Godot;
using System;
using System.Threading.Tasks;

public partial class RockPatchChecks : Node2D
{
    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    public override void _Ready() { CallDeferred(nameof(Run)); }
    private async void Run()
    {
        try
        {
            WorldConfig config = new() { Name = "CONFIG" }; AddChild(config);
            AddChild(new Node2D { Name = "WorldObjects" });
            Node systems = new() { Name = "Systems" }; AddChild(systems);
            systems.AddChild(new LargeRockChecks.FixtureChunks { Name = "ChunkController", WorldSeed = 123456 });
            systems.AddChild(new ResourceWorld { Name = "Resources", Catalog = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres") });
            var family = GD.Load<RockDefinition>("res://WORLD/Contents/Rocks/LargeRock/LargeRock.tres");
            Task<ImageTexture> first = RockPatchVisual.GetTextureAsync(GetTree().Root, config, 0);
            Check(first == RockPatchVisual.GetTextureAsync(GetTree().Root, config, 0), "Pending bakes must be shared.");
            ImageTexture texture = await first;
            using Image pixels = texture.GetImage();
            Check(pixels.GetWidth() == 256 && pixels.GetPixel(0, 0).A < 0.01f
                && pixels.GetPixel(128, 128).A > 0.95f, "Patch must have a solid centre and transparent margins.");
            pixels.SavePng("user://rockpatch-preview.png");
            Rock rock = new() { Definition = family, Variant = family.Variants[0], SizeMultiplier = 1.1f, Mirror = true };
            AddChild(rock);
            for (int i = 0; i < 120 && !rock.HasNode("Visual/RockGroundPatch"); i++)
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Sprite2D patch = rock.GetNode<Sprite2D>("Visual/RockGroundPatch");
            Check(patch.Texture != null && !patch.IsProcessing() && patch.ZIndex == -2 && !patch.ZAsRelative,
                "Patch is static and draws below shadows/actors.");
            Check(patch.Scale.IsEqualApprox(rock.Variant.GroundFootprint * config.RockPatchSize / 256f), "Patch dimensions ignore footprint.");
            Check(rock.GetNode<CollisionShape2D>("Footprint").Position == Vector2.Zero
                && ((RectangleShape2D)rock.GetNode<CollisionShape2D>("Footprint").Shape).Size.IsEqualApprox(rock.Footprint),
                "Burial and patch must preserve collision location/dimensions.");
            config.RockPatchesEnabled = false;
            Rock plain = new() { Definition = family, Variant = family.Variants[0] }; AddChild(plain);
            Check(!plain.HasNode("Visual/RockGroundPatch"), "Disabled feature still attaches a patch.");
            plain.Free();
            config.RockPatchesEnabled = true;
            config.RockPatchPebbleDensity = 0;
            ImageTexture soil = await RockPatchVisual.GetTextureAsync(GetTree().Root, config, 0);
            Check(soil != texture, "Density change did not invalidate texture cache.");
            using Image bare = soil.GetImage();
            bool differs = false;
            for (int y = 40; y < 210 && !differs; y += 3)
                for (int x = 40; x < 210; x += 3)
                    if (bare.GetPixel(x, y) != pixels.GetPixel(x, y)) { differs = true; break; }
            Check(differs, "Pebble density must change baked pixels.");
            config.RockPatchPebbleDensity = 0.41f;
            Rock doomed = new() { Definition = family, Variant = family.Variants[0] };
            AddChild(doomed);
            int doomedChoice = (int)(IsoGrid.Hash(0, 0, 73913u) % 4);
            Task<ImageTexture> pending = RockPatchVisual.GetTextureAsync(GetTree().Root, config, doomedChoice);
            doomed.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await pending;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(!GodotObject.IsInstanceValid(doomed), "Pending bake retained a removed rock.");
            config.RockPatchSize = float.NaN;
            try { RockPatchVisual.Validate(config); throw new Exception("NaN accepted."); }
            catch (InvalidOperationException) { }
            GD.Print("ROCK PATCH CHECKS PASSED"); GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
