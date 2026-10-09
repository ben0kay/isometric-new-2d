// Retains session loot contents and restores wrecks created by robot deaths.
// Armoured loot crates are spawned by ChunkController, not this service.
using Godot;
using System;
using System.Collections.Generic;

public partial class LootWorld : Node
{
#region State
private sealed class DeathWreck
{
    public string Id;
    public Vector2 Position;
    public string Layer;
    public Node2D Actor;
}

private readonly Dictionary<string, InventoryStorage> _contents = new();
private readonly Dictionary<string, DeathWreck> _wrecks = new();
private ChunkController _chunks;
private Node2D _objects;
private PackedScene _wreckScene;
private double _timer;
private bool _initialized;
#endregion

    #region Lifecycle
    // =========================================================
    // Register this world's loot service before containers initialize.
    public override void _EnterTree()
    {
        AddToGroup("loot_world");
    }

    // =========================================================
    // Resolve world services and the shared robot wreck scene.
    public override void _Ready()
    {
        try { InitializeServices(); }
        catch (Exception error)
        {
            WorldObjectSaves.Ensure(this).ReportLoadFailure(error);
            throw;
        }
    }

    // =========================================================
    // Allow scene containers to initialize loot before the Systems branch finishes Ready.
    private void InitializeServices()
    {
        if (_initialized) return;
        _chunks = GetNode<ChunkController>("../ChunkController");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _wreckScene = GD.Load<PackedScene>(
            "res://WORLDABLES/Objects/Loot/RobotWrecks/Basic/BasicRobotWreck.tscn");

        if (_wreckScene == null)
            throw new InvalidOperationException("BasicRobotWreck.tscn is missing.");

        WorldObjectSaves.Ensure(this).RememberRecipe(_wreckScene);
        RestoreObjects();
        _initialized = true;
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Find the loot service belonging to the current world.
    public static LootWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("loot_world") as LootWorld;
    }

    // =========================================================
    // Create the service automatically beneath the existing world Systems node.
    public static LootWorld GetOrCreate(Node context)
    {
        LootWorld existing = Find(context);
        if (existing != null) return existing;

        Node generator = context.GetTree().GetFirstNodeInGroup("world_generator");
        if (generator == null)
            throw new InvalidOperationException("Loot requires WorldGenerator.");

        LootWorld world = new() { Name = "LootWorld" };
        generator.GetParent().AddChild(world);
        return world;
    }
    #endregion

    #region Persistence
    // =========================================================
    // Restore cached empty/full loot and wreck locations before any container can reroll.
    private void RestoreObjects()
    {
        WorldObjectSaves owner = WorldObjectSaves.Find(this);
        foreach (var pair in owner.Saved.Loot) _contents.Add(pair.Key, owner.Decode(pair.Value));
        foreach (WreckSaveData saved in owner.Saved.Wrecks)
            _wrecks.Add(saved.Id, new DeathWreck
            {
                Id = saved.Id, Layer = saved.Layer, Position = new Vector2(saved.X, saved.Y)
            });
    }

    // =========================================================
    // Include retained contents and unloaded wrecks rather than only visible container nodes.
    public void CaptureObjects(WorldObjectSaves owner, WorldObjectsData saved)
    {
        saved.Loot.Clear(); saved.Wrecks.Clear();
        foreach (var pair in _contents) saved.Loot.Add(pair.Key, owner.Encode(pair.Value));
        foreach (DeathWreck wreck in _wrecks.Values)
            saved.Wrecks.Add(new WreckSaveData
            {
                Id = wreck.Id, Layer = wreck.Layer, X = wreck.Position.X, Y = wreck.Position.Y
            });
    }
    #endregion

    #region Session Contents
    // =========================================================
    // Generate each container once, including retaining completely empty contents.
    public InventoryStorage GetContents(
        string id, LootTable table, StorageDefinition definition)
    {
        InitializeServices();
        WorldObjectSaves owner = WorldObjectSaves.Ensure(this);
        owner.RememberRecipe(table); owner.RememberRecipe(definition);
        if (_contents.TryGetValue(id, out InventoryStorage existing))
            return existing.Clone();

        ResourceWorld resources = ResourceWorld.Find(this);
        if (resources == null)
            throw new InvalidOperationException("Loot requires ResourceWorld.");

        InventoryStorage generated = table.Generate(
            resources.Catalog, definition, SeedFor(id, _chunks.WorldSeed));

        _contents.Add(id, generated.Clone());
        return generated;
    }

    // =========================================================
    // Retain committed contents independently from their visible container.
    public void StoreContents(string id, InventoryStorage contents)
    {
        _contents[id] = contents.Clone();
    }

    // =========================================================
    // Produce stable loot seeds from world seed and container identity.
    private static ulong SeedFor(string id, uint worldSeed)
    {
        unchecked
        {
            ulong value = 14695981039346656037UL ^ worldSeed;
            foreach (char character in id)
            {
                value ^= character;
                value *= 1099511628211UL;
            }
            return value;
        }
    }
    #endregion

    #region Robot Deaths
// =========================================================
// Retain the death's location and layer independently from its visible wreck.
public void RecordRobotDeath(
    string id, Vector2 position, string layer = WorldLayerId.Surface)
{
    if (_wrecks.ContainsKey(id))
    {
        GD.PushWarning($"[Wreck] Duplicate death identity: {id}");
        return;
    }

    DeathWreck wreck = new()
    {
        Id = id,
        Position = position,
        Layer = layer
    };
    _wrecks.Add(id, wreck);

    Callable.From(() =>
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        try
        {
            RestoreWreck(wreck);
        }
        catch (Exception error)
        {
            GD.PushError($"[Wreck] Creation failed: {error}");
        }
    }).CallDeferred();
}

// =========================================================
// Create the wreck beneath the correct layer's object root.
private void RestoreWreck(DeathWreck record)
{
    if (GodotObject.IsInstanceValid(record.Actor) || !WreckAvailable(record))
        return;

    Node2D root = record.Layer == WorldLayerId.Surface ? _objects
        : WorldLayerRuntime.Find(this).ObjectsFor(record.Layer);

    Node2D wreck = _wreckScene.Instantiate<Node2D>();
    LootContainer loot = wreck.GetNode<LootContainer>("Systems/Loot");
    loot.PersistentId = record.Id;
    wreck.Position = root.ToLocal(record.Position);

    record.Actor = wreck;
    root.AddChild(wreck);
}

// =========================================================
// Retain wreck contents while restoring at most one active-layer wreck per update.
public override void _Process(double delta)
{
    if (!_chunks.WorldReady) return;
    _timer -= delta;
    if (_timer > 0.0) return;
    _timer = 0.25;

    foreach (DeathWreck record in _wrecks.Values)
    {
        if (!GodotObject.IsInstanceValid(record.Actor))
            record.Actor = null;

        if (!WreckAvailable(record) && record.Actor != null)
        {
            record.Actor.QueueFree();
            record.Actor = null;
        }
    }

    foreach (DeathWreck record in _wrecks.Values)
    {
        if (record.Actor != null || !WreckAvailable(record)) continue;
        RestoreWreck(record);
        break;
    }
}

    // =========================================================
// Restore artwork only on its active layer and ready terrain.
private bool WreckAvailable(DeathWreck record)
{
    WorldLayerRuntime runtime = WorldLayerRuntime.Find(this);
    string current = runtime?.ActiveLayer ?? WorldLayerId.Surface;
    if (record.Layer != current) return false;
    return runtime != null
        ? runtime.IsAvailable(record.Layer, record.Position)
        : _chunks.IsNavigationPointAvailable(record.Position);
}
    #endregion
}
