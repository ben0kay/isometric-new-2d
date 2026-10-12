// Run with F6 after building; checks imported variants without writing campaign files.
using Godot;
using System;
using System.Linq;
using System.IO;
using SHA256 = System.Security.Cryptography.SHA256;

public partial class LargeRockChecks : Node2D
{
    public partial class FixtureChunks : ChunkController
    {
        public override void _Ready() { SetProcess(false); }
        public override void _Process(double delta) { }
    }

    public partial class FixturePlayer : Player
    {
        public override void _Ready() { SetPhysicsProcess(false); }
        public override void _PhysicsProcess(double delta) { }
    }

    // =========================================================
    // Assert an observable contract rather than silently accepting bad content.
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    // =========================================================
    // Exercise real resources, actor collision/artwork, harvest delivery and save records.
    public override async void _Ready()
    {
        try
        {
            RockDefinition family = GD.Load<RockDefinition>("res://WORLD/Contents/Rocks/LargeRock/LargeRock.tres");
            family.ValidateVariants();
            Check(family.Id == "large_rock" && family.Variants.Count == 4, "One family/four variants.");
            using RandomNumberGenerator a = new() { Seed = 123456 };
            using RandomNumberGenerator b = new() { Seed = 123456 };
            bool[] seen = new bool[4];
            for (int i = 0; i < 1000; i++)
            {
                var first = family.RollVariant(a); var second = family.RollVariant(b);
                Check(first == second, "Seeded variant choices differ.");
                seen[family.Variants.IndexOf(first)] = true;
                Check(family.RollSize(a) == family.RollSize(b), "Seeded uniform scale differs.");
                Check(family.RollMirror(a) == family.RollMirror(b), "Seeded mirroring differs.");
            }
            Check(seen.All(v => v), "All four variants must be reachable.");
            Check(family.Variants[1].GroundFootprint.X > family.Variants[2].GroundFootprint.X &&
                family.Variants[2].VisualHeight > family.Variants[1].VisualHeight, "Wide/tall shapes are distinct.");
            var invalid = new RockVariantDefinition();
            try { invalid.Validate(); throw new Exception("Missing image accepted."); }
            catch (InvalidOperationException) { }
            invalid.Dispose();
            using RockDefinition emptyFamily = new() { RequireImportedVariants = true };
            try { emptyFamily.ValidateVariants(); throw new Exception("Imported family silently fell back with no variants."); }
            catch (InvalidOperationException) { }

            using ShaderMaterial lit = new() { Shader = GD.Load<Shader>("res://VISUALS/Lighting/WorldSurface.gdshader") };
            lit.SetShaderParameter("surface_brightness", 0.7f);
            lit.SetShaderParameter("surface_shape_strength", 0.4f);
            lit.SetShaderParameter("surface_normal_enabled", true);
            lit.SetShaderParameter("surface_normal", family.Variants[0].Visual.Image);
            var copied = family.Variants[0].BurialMaterial(lit, new Rect2(0, 0, 256, 256), -14, true);
            Check(Mathf.IsEqualApprox(copied.GetShaderParameter("surface_brightness").AsSingle(), 0.7f) &&
                Mathf.IsEqualApprox(copied.GetShaderParameter("surface_shape_strength").AsSingle(), 0.4f) &&
                copied.GetShaderParameter("surface_normal_enabled").AsBool() &&
                copied.GetShaderParameter("surface_normal").AsGodotObject() == family.Variants[0].Visual.Image,
                "Burial must preserve brightness, shape lighting and normal maps.");
            Check(lit.Shader.ResourcePath.EndsWith("WorldSurface.gdshader"), "Shared source shader was modified.");

            string[] names = { "BasaltFlats", "RockyMountains", "RollingHills", "CarbonForest", "SwampyMarsh", "VerdigrisWilds" };
            int[] counts = { 8, 5, 3, 2, 1, 2 };
            for (int i = 0; i < names.Length; i++)
            {
                var biome = GD.Load<BiomeDefinition>($"res://WORLD/Generation/Biomes/Definitions/{names[i]}/{names[i]}.tres");
                Check(biome.RocksPerChunk == counts[i] && biome.Rocks.Count == 1 && biome.Rocks[0].Definition == family,
                    $"{names[i]}: biome count or species changed incorrectly.");
            }

            AddChild(new WorldConfig { Name = "CONFIG", RockPatchesEnabled = false });
            Node2D objects = new() { Name = "WorldObjects" }; AddChild(objects);
            Node systems = new() { Name = "Systems" }; AddChild(systems);
            systems.AddChild(new FixtureChunks { Name = "ChunkController", WorldSeed = 123456 });
            var catalog = GD.Load<ItemCatalog>("res://ITEMS/ItemCatalog.tres");
            var resources = new ResourceWorld { Name = "Resources", Catalog = catalog }; systems.AddChild(resources);
            var changes = ResourceChanges.Ensure(this);
            for (int i = 0; i < 4; i++)
            {
                var variant = family.Variants[i];
                var rock = new Rock { Definition = family, Variant = variant, SizeMultiplier = 1.1f,
                    Mirror = i % 2 == 0, Position = new Vector2(500 + i * 400, 500) };
                changes.BindGenerated(rock, family, "rock", Vector2I.Zero, i);
                objects.AddChild(rock);
                Check(rock.Footprint.IsEqualApprox(variant.FootprintFor(1.1f)) &&
                    Mathf.IsEqualApprox(rock.Height, variant.HeightFor(1.1f)), "Actor and placement geometry disagree.");
                Check(rock.GetNode<CollisionShape2D>("Footprint").Shape is RectangleShape2D shape &&
                    shape.Size.IsEqualApprox(rock.Footprint), "Collision must match the authored ground footprint.");
                TerrainVisual visual = rock.GetNode<TerrainVisual>("Visual");
                Sprite2D sprite = visual.GetNode<Sprite2D>("Artwork");
                Check(sprite.Texture == variant.Visual.Image &&
                    visual.Scale.Abs().IsEqualApprox(Vector2.One * 1.1f) &&
                    sprite.Scale.X == sprite.Scale.Y, "PNG selection/proportions/random scale mismatch.");
                if (variant.BurialEnabled)
                {
                    Check(sprite.Material is ShaderMaterial buried &&
                        buried.Shader.ResourcePath.EndsWith("RockBurial.gdshader"), "Burial shader not installed.");
                    Check(Mathf.IsEqualApprox(sprite.Position.Y,
                        variant.Visual.Offset.Y + variant.BurialDepthPixels * sprite.Scale.Y), "Burial shift is not scaled with artwork.");
                    ShaderMaterial material = (ShaderMaterial)sprite.Material;
                    float cutoff = material.GetShaderParameter("burial_cutoff_y").AsSingle();
                    Check(Mathf.IsEqualApprox(sprite.Position.Y + cutoff * sprite.Scale.Y, 0), "Cutoff misses ground anchor.");
                    Check(material.Shader.GetShaderUniformList().Count > 0, "Burial shader uniforms unavailable.");
                    Check(variant.BurialMaterial(null, sprite.GetRect(), cutoff, false) == material,
                        "Burial material should be shared for matching variants.");
                    variant.BurialEnabled = false;
                    Rock unburied = new() { Definition = family, Variant = variant,
                        SizeMultiplier = 1.1f, Position = new Vector2(2500, 2500) };
                    objects.AddChild(unburied);
                    Sprite2D original = unburied.GetNode<Sprite2D>("Visual/Artwork");
                    Check(original.Position == variant.Visual.Offset && original.Material == null &&
                        unburied.Footprint.IsEqualApprox(rock.Footprint), "Disabled burial must restore ordinary artwork and preserve collision.");
                    unburied.Free(); variant.BurialEnabled = true;
                }
                Check(!rock.HasMeta("shared_shadow_override"), "Automatic obstacle shadows must remain single-path.");
                var blockers = new System.Collections.Generic.List<Obstacle> { rock };
                Check(WorldPlacement.IsBlocked(this, rock.GlobalPosition, rock.Footprint, blockers, Vector2.Zero), "Solid overlap accepted.");
                ResourceHarvest harvest = rock.GetNode<ResourceHarvest>("Harvest");
                Check(!harvest.Mine(0, 100), "Mining strength requirement bypassed.");
                Check(harvest.Mine(1, family.HarvestWork / 2), "Partial mining failed.");
                Check(!rock.IsQueuedForDeletion(), "Partial mining depleted rock early.");
                Check(harvest.Mine(1, family.HarvestWork / 2) && rock.IsQueuedForDeletion(), "Harvest completion failed.");
                Check(changes.Get(rock).Depleted && changes.Get(rock).Width == rock.Footprint.X, "Depletion reservation lost footprint.");
            }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var drops = resources.SaveDrops().ToArray();
            Check(drops.Length == 4 && drops.All(d => d.Item.Id == "rock" && d.Count == 1), "Harvest must deliver exactly one rock per actor.");
            for (int i = 0; i < 4; i++)
            {
                Rock replay = new() { Definition = family, Variant = family.Variants[i] };
                Check(!changes.BindGenerated(replay, family, "rock", Vector2I.Zero, i), "Depleted rock respawned on chunk replay.");
                replay.Free();
            }
            CampaignData saved = new(); changes.Capture(saved);
            Check(ResourceChanges.ReadSection(saved).Entries.Count == 4, "Generated changes save roundtrip failed.");
            // Exercise the real BiomeScatter/RockSpawner in a renderer-independent world fixture.
            Node2D ground = new() { Name = "GroundChunks" }; AddChild(ground);
            var config = GetNode<WorldConfig>("CONFIG");
            config.GenerateBiomeBasins = false; config.GenerateCaves = false;
            objects.AddChild(new FixturePlayer { Name = "Player" });
            var generator = new WorldGenerator { Name = "WorldGenerator",
                Catalog = GD.Load<BiomeCatalog>("res://WORLD/Generation/Biomes/BiomeCatalog.tres"),
                PlacementMode = BiomePlacementMode.Single, SandboxBiomeId = "basalt_flats" };
            systems.AddChild(generator);
            var spawner = new RockSpawner { Generator = generator }; systems.AddChild(spawner);
            var owned = new System.Collections.Generic.List<Obstacle>();
            var firstRun = new System.Collections.Generic.List<string>();
            for (int pass = 0; pass < 2; pass++)
            {
                var run = new System.Collections.Generic.List<string>();
                for (int y = 10; y < 15; y++) for (int x = 10; x < 15; x++)
                {
                    foreach (var step in spawner.PopulateSteps(new Vector2I(x, y), 16,
                        new Vector2(64, 32), 123456, ground, objects, Vector2.Zero, 256, owned)) { }
                }
                foreach (Rock rock in owned)
                {
                    Check(rock.Variant != null && rock.Footprint.IsEqualApprox(rock.Variant.FootprintFor(rock.SizeMultiplier)),
                        "Spawner/actor footprint mismatch.");
                    run.Add(FormattableString.Invariant($"{rock.Name}|{rock.Position}|{rock.Variant.ResourcePath}|{rock.SizeMultiplier:R}|{rock.Mirror}"));
                }
                Check(run.Count > 0, "Real spawner produced no rocks.");
                if (pass == 0) firstRun = run;
                else Check(firstRun.SequenceEqual(run), "Chunk replay changed seeded positions, variants or scales.");
                foreach (Obstacle rock in owned) rock.Free();
                owned.Clear();
            }
            // Current resources validate; old biome bytes must be rejected without a broad whitelist.
            string path = "res://WORLD/Generation/Biomes/Definitions/BasaltFlats/BasaltFlats.tres";
            CampaignData recipe = new();
            recipe.Settings["CONFIG"] = new(); recipe.Settings["Systems/WorldGenerator"] = new(); recipe.Settings["Systems/ChunkController"] = new();
            recipe.Resources[path] = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)));
            CampaignRecipe.Validate(recipe);
            recipe.Resources[path] = "7A682C8198C2BD6D832345CA271324E8C89147977C7BF3EE05C5824CCA998B95";
            try { CampaignRecipe.Validate(recipe); throw new Exception("Changed campaign recipe accepted."); }
            catch (InvalidDataException) { }
            GD.Print("PASS: burial enable/disable, ground alignment, cached material, preserved lighting; four PNG variants, seeded selection/scale, biome counts, actor collision/artwork, overlap rejection, harvest rewards, depletion/save records and recipe rejection.");
            GetTree().Quit();
        }
        catch (Exception error) { GD.PushError(error.ToString()); GetTree().Quit(1); }
    }
}
