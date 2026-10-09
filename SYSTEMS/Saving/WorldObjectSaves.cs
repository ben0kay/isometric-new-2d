// Snapshots physical world items, containers, wrecks and placed structures.
// Item resources are resolved by catalog ID or an external resource reference.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class WorldObjectsData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> ItemResources { get; set; } = new();
    public List<string> Recipes { get; set; } = new();
    public List<DropSaveData> Drops { get; set; } = new();
    public Dictionary<string, List<StackSaveData>> Storage { get; set; } = new();
    public Dictionary<string, List<StackSaveData>> Loot { get; set; } = new();
    public List<WreckSaveData> Wrecks { get; set; } = new();
    public List<StructureSaveData> Structures { get; set; } = new();
    public List<SceneHealthSaveData> SceneHealth { get; set; } = new();
}
public sealed class DropSaveData
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public string Item { get; set; } = "";
    public int Count { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float PickupRadius { get; set; }
    public double PickupDelay { get; set; }
    public double? RemainingLifetimeSeconds { get; set; }
}
public sealed class WreckSaveData
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public float X { get; set; }
    public float Y { get; set; }
}
public sealed class StructureSaveData
{
    public string Id { get; set; } = "";
    public string Item { get; set; } = "";
    public string WorldScene { get; set; } = "";
    public string ArtworkScene { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Depth { get; set; }
    public int Health { get; set; }
}
public sealed class SceneHealthSaveData
{
    public string Host { get; set; } = "";
    public string HealthPath { get; set; } = "";
    public int Current { get; set; }
}

public partial class WorldObjectSaves : Node
{
    #region State
    public const string Section = "world-objects";
    private WorldObjectsData _saved = new();
    private readonly Dictionary<string, InventoryStorage> _storage = new();
    private readonly Dictionary<string, SceneHealthSaveData> _health = new();
    private readonly HashSet<string> _recipes = new();
    private bool _buildingsRestored, _dropsRestored;
    public WorldObjectsData Saved => _saved;
    public Exception StartupError { get; private set; }
    #endregion

    #region Ownership
    // =========================================================
    // Resolve only the helper belonging to this world.
    public static WorldObjectSaves Find(Node context)
    {
        return WorldConfig.TryFind(context)?.GetParent().GetNodeOrNull<WorldObjectSaves>("WorldObjectSaves");
    }

    // =========================================================
    // Reuse a non-processing helper in Sandbox and direct scene tests too.
    public static WorldObjectSaves Ensure(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        WorldObjectSaves saves = Find(context);
        if (saves != null) return saves;
        saves = new WorldObjectSaves { Name = "WorldObjectSaves" };
        world.AddChild(saves);
        return saves;
    }

    // =========================================================
    // Persistence itself adds no per-frame work.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Surface errors from child Ready callbacks before Campaign can enable play or saving.
    public void ReportLoadFailure(Exception error)
    {
        StartupError ??= error;
    }
    #endregion

    #region Format Validation
    // =========================================================
    // Validate the named section before a save can replace the current file.
    public static WorldObjectsData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version <= 2) return new WorldObjectsData();
            throw new InvalidDataException("Missing world-object save section.");
        }
        WorldObjectsData saved = section.Deserialize<WorldObjectsData>();
        if (saved == null || saved.Version != 1 || saved.ItemResources == null ||
            saved.Recipes == null || saved.Drops == null || saved.Storage == null ||
            saved.Loot == null || saved.Wrecks == null || saved.Structures == null || saved.SceneHealth == null ||
            saved.ItemResources.Count > 4096 || saved.Recipes.Count > 4096 ||
            saved.Drops.Count + saved.Storage.Count + saved.Loot.Count + saved.Wrecks.Count + saved.Structures.Count + saved.SceneHealth.Count > 100000)
            throw new InvalidDataException("Invalid world-object save section.");
        foreach (var item in saved.ItemResources)
            if (string.IsNullOrWhiteSpace(item.Key) || !ResourcePath(item.Value, ".tres"))
                throw new InvalidDataException("Invalid saved item reference.");
        foreach (string recipe in saved.Recipes)
            if (!ResourcePath(recipe)) throw new InvalidDataException("Invalid saved object recipe.");
        HashSet<string> ids = new();
        foreach (DropSaveData drop in saved.Drops)
        {
            if (drop == null || !Guid.TryParseExact(drop.Id, "N", out _) || !ids.Add(drop.Id) ||
                string.IsNullOrWhiteSpace(drop.Item) || drop.Count <= 0 ||
                !float.IsFinite(drop.X) || !float.IsFinite(drop.Y) ||
                !float.IsFinite(drop.PickupRadius) || drop.PickupRadius <= 0 ||
                !double.IsFinite(drop.PickupDelay) || drop.PickupDelay < 0 ||
                (drop.RemainingLifetimeSeconds is double lifetime && (!double.IsFinite(lifetime) || lifetime < 0)))
                throw new InvalidDataException("Invalid saved drop.");
            WorldLayerId.Validate(drop.Layer);
        }
        ids.Clear();
        foreach (WreckSaveData wreck in saved.Wrecks)
        {
            if (wreck == null || string.IsNullOrWhiteSpace(wreck.Id) || !ids.Add(wreck.Id) ||
                !float.IsFinite(wreck.X) || !float.IsFinite(wreck.Y))
                throw new InvalidDataException("Invalid saved wreck.");
            WorldLayerId.Validate(wreck.Layer);
        }
        ids.Clear();
        foreach (StructureSaveData structure in saved.Structures)
            if (structure == null || !Guid.TryParseExact(structure.Id, "N", out _) || !ids.Add(structure.Id) ||
                string.IsNullOrWhiteSpace(structure.Item) || !ResourcePath(structure.WorldScene, ".tscn") ||
                !ResourcePath(structure.ArtworkScene, ".tscn") || structure.Health < 1 ||
                structure.Width < 1 || structure.Width > 8 || structure.Depth < 1 || structure.Depth > 8)
                throw new InvalidDataException("Invalid saved structure.");
        ids.Clear();
        foreach (SceneHealthSaveData health in saved.SceneHealth)
            if (health == null || !LocalPath(health.Host) || !health.Host.StartsWith("WorldObjects/", StringComparison.Ordinal) ||
                !LocalPath(health.HealthPath) || !ids.Add(health.Host + "/" + health.HealthPath) || health.Current < 0)
                throw new InvalidDataException("Invalid scene-object health.");
        foreach (var contents in saved.Storage.Concat(saved.Loot))
        {
            if (string.IsNullOrWhiteSpace(contents.Key) || contents.Key.Length > 2048 ||
                contents.Value == null || contents.Value.Count < 1 || contents.Value.Count > 96)
                throw new InvalidDataException("Invalid saved container layout.");
            foreach (StackSaveData stack in contents.Value)
                if (stack == null || stack.Item == null || stack.Count < 0 ||
                    (stack.Item.Length == 0) != (stack.Count == 0))
                    throw new InvalidDataException("Invalid saved container stack.");
        }
        return saved;
    }

    // =========================================================
    // Keep references inside the project and separate from embedded resource IDs.
    private static bool ResourcePath(string path, string extension = null)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith("res://", StringComparison.Ordinal) &&
            !path.Contains("::") && !path.Contains("..") && (extension == null || path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================
    // Allow only ordinary world-relative node paths.
    private static bool LocalPath(string path)
    {
        return !string.IsNullOrEmpty(path) && !path.StartsWith('/') && !path.Contains(':') &&
            !path.Split('/').Any(part => part is "" or "." or "..");
    }
    #endregion

    #region Startup
    // =========================================================
    // Initialize before containers enter Ready and remove destroyed scene originals.
    public void Initialize(CampaignData data, Node world)
    {
        _saved = ReadSection(data);
        foreach (string recipe in _saved.Recipes) _recipes.Add(recipe);
        foreach (SceneHealthSaveData health in _saved.SceneHealth)
        {
            _health.Add(health.Host + "/" + health.HealthPath, health);
            if (health.Current != 0) continue;
            Node host = world.GetNodeOrNull(health.Host);
            if (host != null) { host.GetParent().RemoveChild(host); host.Free(); }
        }
    }

    // =========================================================
    // Restore structures before natural objects and navigation populate their footprints.
    public void RestoreBuildings(Node world)
    {
        if (StartupError != null) throw StartupError;
        if (_buildingsRestored) return;
        if (_saved.Wrecks.Count > 0 || _saved.Loot.Count > 0) LootWorld.GetOrCreate(world);
        if (StartupError != null) throw StartupError;
        foreach (StructureSaveData structure in _saved.Structures)
            PlacementWorld.Ensure(world).RestoreSaved(structure, Resolve(structure.Item));
        TrackSceneHealth(world.GetNode("WorldObjects"));
        _buildingsRestored = true;
    }

    // =========================================================
    // Restore drop ownership once, after player state is ready and while actions are frozen.
    public void RestoreDrops(Node context)
    {
        if (_dropsRestored) return;
        ResourceWorld resources = ResourceWorld.Find(context);
        foreach (DropSaveData drop in _saved.Drops)
        {
            if (drop.RemainingLifetimeSeconds == 0) continue;
            resources.RestoreDrop(drop, Resolve(drop.Item));
        }
        _dropsRestored = true;
    }
    #endregion

    #region Item Codec
    // =========================================================
    // Remember external items absent from the master catalog, such as starter tools.
    private void Remember(ItemDefinition item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id))
            throw new InvalidDataException("World items need a stable item identity.");
        if (string.IsNullOrEmpty(item.ResourcePath)) return;
        if (!ResourcePath(item.ResourcePath, ".tres"))
            throw new InvalidDataException("Saved items need external resources or catalog identities.");
        _saved.ItemResources[item.Id] = item.ResourcePath;
        _recipes.Add(item.ResourcePath);
    }

    // =========================================================
    // Resolve the catalog first and check external resource identities before using them.
    public ItemDefinition Resolve(string id)
    {
        ItemDefinition item = ResourceWorld.Find(this).Catalog.Get(id);
        if (item != null) return item;
        if (!_saved.ItemResources.TryGetValue(id, out string path))
            throw new InvalidDataException("Saved world item is unavailable: " + id);
        item = GD.Load<ItemDefinition>(path);
        if (item?.Id != id) throw new InvalidDataException("Saved world-item identity changed: " + id);
        return item;
    }

    // =========================================================
    // Copy exact physical slots, including empty slots and external item references.
    public List<StackSaveData> Encode(InventoryStorage contents)
    {
        List<StackSaveData> slots = new();
        for (int i = 0; i < contents.SlotCount; i++)
        {
            InventoryStack stack = contents.Get(i);
            if (!stack.IsEmpty) Remember(stack.Item);
            slots.Add(new StackSaveData { Item = stack.Item?.Id ?? "", Count = stack.Count });
        }
        return slots;
    }

    // =========================================================
    // Stage a container without collection, rerolling loot or merging its saved slots.
    public InventoryStorage Decode(List<StackSaveData> slots)
    {
        InventoryStorage contents = new(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            StackSaveData stack = slots[i];
            if (stack.Count == 0) continue;
            ItemDefinition item = Resolve(stack.Item);
            if (stack.Count > Math.Max(1, item.MaxStack))
                throw new InvalidDataException("Saved container stack exceeds its item limit.");
            contents.Set(i, new InventoryStack(item, stack.Count));
        }
        return contents;
    }

    // =========================================================
    // Include embedded definitions through their containing scene or resource file.
    public void RememberRecipe(Resource resource)
    {
        string path = resource?.ResourcePath?.Split("::")[0];
        if (!string.IsNullOrEmpty(path)) _recipes.Add(path);
    }
    #endregion

    #region Containers
    // =========================================================
    // Separate explicit IDs, placed-object GUIDs and scene paths inside each layer.
    public static string StorageKey(WorldStorage storage)
    {
        string layer = WorldLayerMember.For(storage.Host);
        if (!string.IsNullOrWhiteSpace(storage.PersistentId)) return layer + "/id/" + storage.PersistentId;
        for (Node node = storage.Host; node != null; node = node.GetParent())
            if (node is PlacedObject placed)
                return layer + "/placed/" + placed.PersistentId + "/" + placed.GetPathTo(storage);
        return layer + "/scene/" + WorldConfig.Find(storage).GetParent().GetPathTo(storage);
    }

    // =========================================================
    // Restore ordinary storage before its first interaction; empty contents remain empty.
    public void RestoreStorage(WorldStorage storage)
    {
        string key = StorageKey(storage);
        RememberRecipe(storage.Definition);
        if (!_storage.TryGetValue(key, out InventoryStorage contents) && _saved.Storage.TryGetValue(key, out List<StackSaveData> slots))
            contents = Decode(slots);
        if (contents != null) storage.ImportContents(contents);
        else StoreStorage(storage);
    }

    // =========================================================
    // Retain committed storage contents independently of its visible host.
    public void StoreStorage(WorldStorage storage)
    {
        if (storage is LootContainer) return;
        _storage[StorageKey(storage)] = storage.ExportContents();
    }
    #endregion

    #region Scene Health
    // =========================================================
    // Track scene world-object health while leaving actors and placed structures to their owners.
    private void TrackSceneHealth(Node node)
    {
        if (node is Player or Entity or PlacedObject) return;
        if (node is Health health)
        {
            Node2D host = null;
            for (Node ancestor = health.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
                if (ancestor is Node2D actor) { host = actor; break; }
            if (host != null)
            {
                Node world = GetParent();
                if (!string.IsNullOrEmpty(host.SceneFilePath)) _recipes.Add(host.SceneFilePath);
                string hostPath = world.GetPathTo(host).ToString(), healthPath = host.GetPathTo(health).ToString();
                string key = hostPath + "/" + healthPath;
                if (_health.TryGetValue(key, out SceneHealthSaveData saved) && saved.Current > 0)
                    health.RestoreState(saved.Current);
                SceneHealthSaveData state = new() { Host = hostPath, HealthPath = healthPath, Current = health.Current };
                _health[key] = state;
                health.Changed += (current, maximum) => state.Current = current;
            }
        }
        foreach (Node child in node.GetChildren()) TrackSceneHealth(child);
    }
    #endregion

    #region Capture
    // =========================================================
    // Snapshot live and deferred drops, cached container contents, wrecks and surviving structures.
    public void Capture(CampaignData data)
    {
        Node world = GetParent();
        _saved.Drops.Clear(); _saved.Structures.Clear();
        HashSet<string> dropIds = new();
        foreach (WorldPickup drop in ResourceWorld.Find(this).SaveDrops())
        {
            if (!GodotObject.IsInstanceValid(drop) || drop.IsQueuedForDeletion() || drop.Count <= 0 || drop.RemainingLifetimeSeconds == 0) continue;
            if (!dropIds.Add(drop.PersistentId)) throw new InvalidDataException("Duplicate dropped-item identity.");
            Remember(drop.Item);
            _saved.Drops.Add(drop.CaptureSave());
        }
        HashSet<string> storageIds = new();
        foreach (Node node in GetTree().GetNodesInGroup("world_storage"))
        {
            if (node is not WorldStorage storage || !world.IsAncestorOf(storage) || storage.IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(storage.Host) || storage.Host.IsQueuedForDeletion()) continue;
            if (!storageIds.Add(StorageKey(storage))) throw new InvalidDataException("Duplicate world-container identity.");
            RememberRecipe(storage.Definition);
            StoreStorage(storage);
        }
        foreach (var storage in _storage) _saved.Storage[storage.Key] = Encode(storage.Value);
        LootWorld.Find(this)?.CaptureObjects(this, _saved);
        foreach (Node node in GetTree().GetNodesInGroup("world_obstacles"))
        {
            if (node is not PlacedObject placed || !world.IsAncestorOf(placed) || placed.IsQueuedForDeletion() || !placed.ObjectHealth.IsAlive) continue;
            Remember(placed.SourceItem);
            RememberRecipe(placed.Definition.WorldScene);
            RememberRecipe(placed.Definition.ArtworkScene);
            _saved.Structures.Add(placed.CaptureSave());
        }
        _saved.SceneHealth = _health.Values.ToList();
        _saved.Recipes = _recipes.OrderBy(path => path, StringComparer.Ordinal).ToList();
        data.Sections[Section] = JsonSerializer.SerializeToElement(_saved);
        data.Version = 3; data.Coverage = "world-player-resources-objects";
    }
    #endregion
}
