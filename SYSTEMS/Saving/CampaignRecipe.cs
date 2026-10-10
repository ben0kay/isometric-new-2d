// Records exported world settings and detects incompatible generation resources.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

public static class CampaignRecipe
{
    private static readonly string[] Owners =
        { "CONFIG", "Systems/WorldGenerator", "Systems/ChunkController" };

    // =========================================================
    // Capture exported settings before Ready callbacks derive terrain and cave seeds.
    public static void Capture(Node world, CampaignData data)
    {
        data.Settings.Clear(); data.Resources.Clear();
        foreach (string owner in Owners)
        {
            Node node = world.GetNode(owner);
            Dictionary<string, string> values = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) == 0 ||
                    (usage & PropertyUsageFlags.Storage) == 0) continue;
                string name = property["name"].AsString();
                Variant value = node.Get(name);
                if (value.VariantType == Variant.Type.Object)
                {
                    Resource resource = value.AsGodotObject() as Resource;
                    if (resource == null) { values[name] = "null"; continue; }
                    if (string.IsNullOrEmpty(resource.ResourcePath) || resource.ResourcePath.Contains("::"))
                        throw new InvalidDataException($"Save needs a separate resource file for {owner}/{name}.");
                    values[name] = "@resource:" + resource.ResourcePath;
                    Stamp(resource.ResourcePath, data.Resources);
                }
                else values[name] = GD.VarToStr(value);
            }
            data.Settings[owner] = values;
        }
        // A null generator catalog uses the surface definition inside LayerCatalog.
        Stamp("res://WORLD/Layers/WorldLayers.tres", data.Resources);
    }

    // =========================================================
    // Include hard-coded ground catalogs and scene resource definitions in this save's recipe.
    public static void CaptureResourceDefinitions(CampaignData data)
    {
        Stamp("res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres", data.Resources);
        foreach (ResourceChangeData entry in ResourceChanges.ReadSection(data).Entries)
            Stamp(entry.Definition.Split("::")[0], data.Resources);
    }

    // =========================================================
    // Stamp object scenes, storage/loot definitions and external item references.
    public static void CaptureObjectDefinitions(CampaignData data)
    {
        foreach (string path in WorldObjectSaves.ReadSection(data).Recipes)
            Stamp(path, data.Resources);
    }

    // =========================================================
    // Include entity recipes so death identities cannot silently change species or prefab.
    public static void CaptureEntityDefinitions(CampaignData data)
    {
        foreach (EntitySaveData actor in EntitySaves.ReadSection(data).Entities)
        {
            Stamp(actor.Definition, data.Resources); Stamp(actor.Scene, data.Resources);
        }
        foreach (EntityDeathData entry in EntityDeaths.ReadSection(data).Entries)
        {
            Stamp(entry.Definition, data.Resources);
            Stamp(entry.Scene, data.Resources);
        }
    }

    // =========================================================
    // Hash referenced settings and their dependencies without storing engine objects.
    private static void Stamp(string path, Dictionary<string, string> stamps)
    {
        if (stamps.ContainsKey(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidDataException($"Missing world resource: {path}");
        stamps[path] = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)));
        foreach (string dependency in ResourceLoader.GetDependencies(path))
        {
            string resolved = dependency.Split("::").Last();
            if (resolved.StartsWith("res://", StringComparison.Ordinal)) Stamp(resolved, stamps);
        }
    }

    // =========================================================
    // Refuse changed resource recipes rather than silently rebuilding a different map.
    public static void Validate(CampaignData data)
    {
        if (data.Settings.Count != Owners.Length || data.Resources.Count == 0 || data.Resources.Count > 4096)
            throw new InvalidDataException("Missing or invalid generation recipe.");
        foreach (var stamp in data.Resources)
        {
            if (!stamp.Key.StartsWith("res://", StringComparison.Ordinal) ||
                !Godot.FileAccess.FileExists(stamp.Key))
                throw new InvalidDataException("Saved world resource is unavailable: " + stamp.Key);
            string current = Convert.ToHexString(
                SHA256.HashData(Godot.FileAccess.GetFileAsBytes(stamp.Key)));
            if (current != stamp.Value && !HarvestRecipeCompatibility.Allows(stamp.Key, stamp.Value))
                throw new InvalidDataException("World generation resources changed since this save. " +
                    "Start a new campaign or restore the original resources. " + stamp.Key);
        }
    }

    // =========================================================
    // Apply settings to a detached scene so initialization sees the saved recipe.
    public static void Apply(Node world, CampaignData data)
    {
        Validate(data);
        foreach (string owner in Owners)
        {
            if (!data.Settings.TryGetValue(owner, out var values) || values == null || values.Count > 256)
                throw new InvalidDataException("Missing saved world settings.");
            Node node = world.GetNode(owner);
            HashSet<string> exported = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) != 0 &&
                    (usage & PropertyUsageFlags.Storage) != 0)
                    exported.Add(property["name"].AsString());
            }
            foreach (var setting in values)
            {
                if (!exported.Contains(setting.Key) || setting.Value == null)
                    throw new InvalidDataException("Unsupported saved setting.");
                if (setting.Value.StartsWith("@resource:", StringComparison.Ordinal))
                {
                    string path = setting.Value.Substring(10);
                    if (!data.Resources.ContainsKey(path))
                        throw new InvalidDataException("Unvalidated resource reference.");
                    Resource resource = ResourceLoader.Load(path)
                        ?? throw new InvalidDataException("Saved resource is unavailable.");
                    node.Set(setting.Key, resource);
                }
                else
                {
                    Variant value = GD.StrToVar(setting.Value);
                    if (value.VariantType == Variant.Type.Object || value.VariantType == Variant.Type.Callable ||
                        value.VariantType == Variant.Type.Signal)
                        throw new InvalidDataException("Invalid saved world setting type.");
                    node.Set(setting.Key, value);
                }
            }
        }
        // Older campaigns had no multiplier. Preserve the old surface probability,
        // then include these defaults in the recipe written on the next successful Save.
        const string frequency = nameof(GlobalConfig.LayerConnectionFrequency);
        if (!data.Settings["CONFIG"].ContainsKey(frequency))
        {
            WorldConfig config = WorldConfig.Find(world);
            config.LayerConnectionFrequency = GlobalConfig.DefaultLayerConnectionFrequency();
            data.Settings["CONFIG"][frequency] = GD.VarToStr(config.Get(frequency));
        }
        world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed = data.Seed;
    }
}
