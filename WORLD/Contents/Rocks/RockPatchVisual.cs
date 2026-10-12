// Four shared disk-cached ground textures, never a node or collision per pebble.
using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public static class RockPatchVisual
{
    private static readonly Dictionary<string, Task<ImageTexture>> Textures = new();
    private static readonly Dictionary<bool, ShaderMaterial> Materials = new();
    private static ImageTexture _white;

    public static async Task AttachAsync(Rock rock, TerrainVisual visual)
    {
        WorldConfig config = WorldConfig.TryFind(rock);
        if (config == null || !config.RockPatchesEnabled) return;
        Validate(config);
        uint seed = IsoGrid.Hash(Mathf.RoundToInt(rock.GlobalPosition.X),
            Mathf.RoundToInt(rock.GlobalPosition.Y), 73913u);
        int choice = (int)(seed % 4);
        ImageTexture texture = await GetTextureAsync(rock.GetTree().Root, config, choice);
        // Chunk unload/depletion can remove the actor while a first bake is pending.
        if (!GodotObject.IsInstanceValid(rock) || !rock.IsInsideTree() || rock.IsQueuedForDeletion()
            || !GodotObject.IsInstanceValid(visual) || visual.IsQueuedForDeletion()) return;
        bool lighting = WorldLighting.Find(rock) != null;
        if (!Materials.TryGetValue(lighting, out ShaderMaterial material))
        {
            material = ArtworkBaker.LoadMaterial("res://VISUALS/Drawings/Ground/RockPatchDisplay.gdshader");
            material.SetShaderParameter("patch_lighting", lighting);
            Materials.Add(lighting, material);
        }
        Sprite2D patch = new()
        {
            Name = "RockGroundPatch", Texture = texture, Material = material,
            // Ground is -2, shadows -1 and actors 0. Patch joins ground draw order.
            ZAsRelative = false, ZIndex = -2,
            TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            Scale = rock.Variant.GroundFootprint * config.RockPatchSize / 256f,
            Modulate = new Color(1, 1, 1, config.RockPatchOpacity)
        };
        // Visual carries terrain elevation, instance scale and mirroring; artwork burial does not.
        visual.AddChild(patch);
        patch.SetProcess(false);
    }

    private static ImageTexture WhitePixel()
    {
        if (_white != null) return _white;
        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        return _white = ImageTexture.CreateFromImage(image);
    }

    public static void Validate(GlobalConfig config)
    {
        if (!float.IsFinite(config.RockPatchSize) || config.RockPatchSize < 1 || config.RockPatchSize > 3
            || !float.IsFinite(config.RockPatchOpacity) || config.RockPatchOpacity < 0 || config.RockPatchOpacity > 1
            || !float.IsFinite(config.RockPatchPebbleDensity) || config.RockPatchPebbleDensity < 0 || config.RockPatchPebbleDensity > 1)
            throw new InvalidOperationException("Invalid rock ground patch tuning.");
    }

    public static Task<ImageTexture> GetTextureAsync(Node host, GlobalConfig config, int choice)
    {
        Validate(config);
        if (choice < 0 || choice > 3) throw new ArgumentOutOfRangeException(nameof(choice));
        string key = FormattableString.Invariant($"v1|{choice}|{config.RockPatchPebbleDensity:R}|{config.RockPatchSoilColour}|{config.RockPatchStoneColour}");
        if (Textures.TryGetValue(key, out Task<ImageTexture> texture)) return texture;
        // Snapshot tuning so the key always describes the actual baked pixels.
        float density = config.RockPatchPebbleDensity;
        Color soil = config.RockPatchSoilColour, stone = config.RockPatchStoneColour;
        texture = ArtworkBaker.LoadOrBake(host, "RockPatch", key, new Vector2I(256, 256), () =>
        {
            ShaderMaterial bake = ArtworkBaker.LoadMaterial("res://VISUALS/Drawings/Ground/RockPatchBake.gdshader");
            bake.SetShaderParameter("patch_seed", (float)choice);
            bake.SetShaderParameter("pebble_density", density);
            bake.SetShaderParameter("soil_colour", soil);
            bake.SetShaderParameter("stone_colour", stone);
            return new Sprite2D { Texture = WhitePixel(), Centered = false,
                Scale = new Vector2(256, 256), Material = bake };
        });
        Textures.Add(key, texture);
        return texture;
    }
}
