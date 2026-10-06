// Retains loot contents for the current world session and streams uncommon wrecks.
// Spawn settings live here until the separate CONFIG pass is implemented.
using Godot;
using System;
using System.Collections.Generic;

public partial class LootWorld : Node
{
    #region Configuration
    [ExportGroup("Wreck Spawning")]
    [Export] public PackedScene WreckScene { get; set; }
    [Export] public bool SpawnWrecks { get; set; } = true;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float ChancePerChunk { get; set; } = 0.10f;
    [Export] public int ScanRadiusChunks { get; set; } = 3;
    [Export] public double UpdateInterval { get; set; } = 0.5;
    [Export] public int PlacementAttempts { get; set; } = 8;
    [Export] public int MaximumLiveWrecks { get; set; } = 24;
    [Export] public Vector2 WreckFootprint { get; set; } = new(112, 56);
    #endregion

    #region State
    private sealed class Placement
    {
        public string Id;
        public Vector2 Point;
        public Obstacle Actor;
    }

    private readonly Dictionary<string, InventoryStorage> _contents = new();
    private readonly Dictionary<Vector2I, Placement> _placements = new();
    private readonly HashSet<Vector2I> _evaluated = new();
    private ChunkController _chunks;
    private Node2D _ground, _objects;
    private Player _player;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before hand-placed loot containers run their Ready callbacks.
    public override void _EnterTree()
    {
        AddToGroup("loot_world");
    }

    // =========================================================
    // Resolve the same world services used by existing chunk spawners.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _player = _objects.GetNode<Player>("Player");
    }

    // =========================================================
    // Find the scene-owned loot service.
    public static LootWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("loot_world") as LootWorld;
    }
    #endregion

    #region Session Contents
    // =========================================================
    // Generate a stable container once and retain even completely empty contents.
    public InventoryStorage GetContents(
        string id, LootTable table, StorageDefinition definition)
    {
        if (_contents.TryGetValue(id, out InventoryStorage existing))
            return existing.Clone();

        ResourceWorld resources = ResourceWorld.Find(this);
        if (resources == null)
            throw new InvalidOperationException("Loot requires ResourceWorld.");

        ChunkController chunks = GetNode<ChunkController>("../ChunkController");
        InventoryStorage generated = table.Generate(
            resources.Catalog, definition, SeedFor(id, chunks.WorldSeed));
        _contents.Add(id, generated.Clone());
        return generated;
    }

    // =========================================================
    // Save a committed snapshot independently from its streamed object.
    public void StoreContents(string id, InventoryStorage contents)
    {
        _contents[id] = contents.Clone();
    }

    // =========================================================
    // Hash stable IDs without platform-dependent string hash codes.
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

    #region Streaming
    // =========================================================
    // Scan nearby completed chunks periodically rather than every frame.
    public override void _Process(double delta)
    {
        if (!_chunks.WorldReady || !SpawnWrecks || WreckScene == null) return;
        _timer -= delta;
        if (_timer > 0) return;
        _timer = Math.Max(0.1, UpdateInterval);

        int live = 0;
        foreach (Placement placement in _placements.Values)
        {
            if (!GodotObject.IsInstanceValid(placement.Actor))
            {
                placement.Actor = null;
                continue;
            }

            if (!_chunks.IsNavigationPointAvailable(placement.Point))
            {
                placement.Actor.QueueFree();
                placement.Actor = null;
            }
            else live++;
        }

        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
        Vector2I centre = new(
            Mathf.FloorToInt((tile.X + 0.5f) / _chunks.ChunkSize),
            Mathf.FloorToInt((tile.Y + 0.5f) / _chunks.ChunkSize));

        int radius = Math.Clamp(ScanRadiusChunks, 1, 6);
        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);

        // Bound scene instantiation to one new wreck per update.
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (live >= Math.Max(1, MaximumLiveWrecks)) return;
            Vector2I coordinate = centre + new Vector2I(x, y);

            float middle = (_chunks.ChunkSize - 1) * 0.5f;
            Vector2 chunkCentre = _ground.ToGlobal(IsoGrid.TileToWorld(
                new Vector2(coordinate.X * _chunks.ChunkSize + middle,
                    coordinate.Y * _chunks.ChunkSize + middle),
                _chunks.TileSize));

            if (!_chunks.IsNavigationPointAvailable(chunkCentre)) continue;

            if (!_evaluated.Contains(coordinate))
                EvaluateChunk(coordinate, obstacles);

            if (!_placements.TryGetValue(coordinate, out Placement placement) ||
                GodotObject.IsInstanceValid(placement.Actor) ||
                !_chunks.IsNavigationPointAvailable(placement.Point)) continue;

            SpawnPlacement(placement);
            return;
        }
    }

    // =========================================================
    // Choose at most one clear, seeded wreck location in this chunk.
    private void EvaluateChunk(Vector2I coordinate, List<Obstacle> obstacles)
    {
        _evaluated.Add(coordinate);
        using RandomNumberGenerator rng = new()
        {
            Seed = IsoGrid.Hash(
                coordinate.X, coordinate.Y, _chunks.WorldSeed ^ 0x10A7u)
        };

        if (rng.Randf() >= Mathf.Clamp(ChancePerChunk, 0f, 1f)) return;

        float lowX = coordinate.X * _chunks.ChunkSize - 0.5f;
        float lowY = coordinate.Y * _chunks.ChunkSize - 0.5f;

        for (int attempt = 0; attempt < Math.Clamp(PlacementAttempts, 1, 32); attempt++)
        {
            Vector2 tile = new(
                rng.RandfRange(lowX, lowX + _chunks.ChunkSize),
                rng.RandfRange(lowY, lowY + _chunks.ChunkSize));
            Vector2 point = _ground.ToGlobal(
                IsoGrid.TileToWorld(tile, _chunks.TileSize));

            if (point.DistanceSquaredTo(_player.GlobalPosition) <
                _chunks.SpawnClearRadius * _chunks.SpawnClearRadius) continue;
            if (!_chunks.IsNavigationPointAvailable(
                point, WreckFootprint.Length() * 0.5f + 12f)) continue;
            if (WorldPlacement.IsBlocked(
                _objects, point, WreckFootprint, obstacles, new Vector2(24, 16)))
                continue;

            _placements.Add(coordinate, new Placement
            {
                Id = $"robot_wreck:{coordinate.X}:{coordinate.Y}",
                Point = point
            });
            return;
        }
    }

    // =========================================================
    // Restore the same wreck identity when its terrain becomes available again.
    private void SpawnPlacement(Placement placement)
    {
        Obstacle wreck = WreckScene.Instantiate<Obstacle>();
        LootContainer loot = wreck.GetNode<LootContainer>("Systems/Loot");
        loot.PersistentId = placement.Id;
        wreck.Position = _objects.ToLocal(placement.Point);
        placement.Actor = wreck;
        _objects.AddChild(wreck);
    }
    #endregion
}