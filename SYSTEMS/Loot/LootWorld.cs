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
        public Node2D Actor;
    }

    private readonly Dictionary<string, InventoryStorage> _contents = new();
    private readonly Dictionary<string, DeathWreck> _wrecks = new();
    private ChunkController _chunks;
    private Node2D _objects;
    private PackedScene _wreckScene;
    private double _timer;
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
        _chunks = GetNode<ChunkController>("../ChunkController");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _wreckScene = GD.Load<PackedScene>(
            "res://WORLDABLES/Objects/Loot/RobotWrecks/Basic/BasicRobotWreck.tscn");

        if (_wreckScene == null)
            throw new InvalidOperationException("BasicRobotWreck.tscn is missing.");

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

    #region Session Contents
    // =========================================================
    // Generate each container once, including retaining completely empty contents.
    public InventoryStorage GetContents(
        string id, LootTable table, StorageDefinition definition)
    {
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
    // Record one wreck and defer scene creation outside combat physics callbacks.
    public void RecordRobotDeath(string id, Vector2 position)
    {
        if (_wrecks.ContainsKey(id)) return;

        DeathWreck wreck = new() { Id = id, Position = position };
        _wrecks.Add(id, wreck);

        Callable.From(() =>
        {
            if (IsInsideTree() && !IsQueuedForDeletion())
                RestoreWreck(wreck);
        }).CallDeferred();
    }

    // =========================================================
    // Restore the same identity and contents at the robot's death position.
    private void RestoreWreck(DeathWreck record)
    {
        if (GodotObject.IsInstanceValid(record.Actor) ||
            !_chunks.IsNavigationPointAvailable(record.Position)) return;

        Node2D wreck = _wreckScene.Instantiate<Node2D>();
        LootContainer loot = wreck.GetNode<LootContainer>("Systems/Loot");
        loot.PersistentId = record.Id;
        wreck.Position = _objects.ToLocal(record.Position);
        record.Actor = wreck;
        _objects.AddChild(wreck);
    }

    // =========================================================
    // Retire unloaded wreck artwork and restore at most one wreck per update.
    public override void _Process(double delta)
    {
        if (!_chunks.WorldReady) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = 0.25;

        foreach (DeathWreck record in _wrecks.Values)
        {
            if (!GodotObject.IsInstanceValid(record.Actor))
                record.Actor = null;

            bool available = _chunks.IsNavigationPointAvailable(record.Position);
            if (!available && record.Actor != null)
            {
                record.Actor.QueueFree();
                record.Actor = null;
            }
        }

        foreach (DeathWreck record in _wrecks.Values)
        {
            if (record.Actor != null ||
                !_chunks.IsNavigationPointAvailable(record.Position)) continue;
            RestoreWreck(record);
            break;
        }
    }
    #endregion
}