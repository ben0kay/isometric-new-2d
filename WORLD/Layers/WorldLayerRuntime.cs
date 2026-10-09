// Owns independent underground runtime worlds and resolves them by exact layer ID.
// Dormant layers own services but build no chunks until activated or preloaded.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldLayerRuntime : Node
{
    #region State
    public string ActiveLayer { get; private set; } = WorldLayerId.Surface;
    public string SurfaceEntranceLayerId { get; private set; }
    public CaveWorld SurfaceUnderground => GetUnderground(SurfaceEntranceLayerId);

    private Node2D _surfaceObjects;
    private ChunkController _surfaceChunks;
    private readonly Dictionary<string, CaveWorld> _underground =
        new(StringComparer.Ordinal);
    public IEnumerable<CaveWorld> UndergroundWorlds => _underground.Values;
    #endregion

    #region Construction
    // =========================================================
    // Register one runtime owner for this gameplay world.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_runtime");
        SetProcess(false);
    }

    // =========================================================
    // Build service instances only; chunk construction remains demand-driven.
    public void Initialize(Node world, Player player,
        ChunkController chunks, CaveEntrancePlanner surfacePlanner)
    {
        WorldLayerCatalog catalog = WorldConfig.Find(world).GetLayerCatalog();
        SurfaceEntranceLayerId = catalog.SurfaceEntranceLayerId;
        _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
        _surfaceChunks = chunks;
        Node2D ground = world.GetNode<Node2D>("GroundChunks");

        foreach (WorldLayerDefinition definition in catalog.Layers)
        {
            if (definition.Kind != WorldLayerKind.Underground) continue;
            bool surfaceDestination = definition.Id == SurfaceEntranceLayerId;

            CaveWorld cave = new()
            {
                Name = definition.Id,
                LayerId = definition.Id,
                Planner = surfaceDestination ? surfacePlanner : null
            };
            AddChild(cave);
            cave.Build(ground.GlobalPosition, chunks.TileSize, 0f,
                SeedFor(chunks.WorldSeed, definition.Id),
                surfaceDestination ? surfacePlanner.Settings
                    : definition.CreateCaveSettings(),
                Array.Empty<CaveHole>());
            cave.Streaming.ConfigurePlayer(player);
            cave.SetActive(false);
            _underground.Add(definition.Id, cave);
        }

        ActivateLayer(WorldLayerId.Surface);
        GD.Print($"[Layers] {_underground.Count} independent underground worlds ready.");
    }

    // =========================================================
    // Derive deterministic seeds without relying on randomized string hashes.
    private static uint SeedFor(uint worldSeed, string id)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char character in id)
                hash = (hash ^ character) * 16777619u;
            return worldSeed ^ hash;
        }
    }
    #endregion

    #region Queries
    // =========================================================
    // Find the runtime registry in the current gameplay scene.
    public static WorldLayerRuntime Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_runtime") as WorldLayerRuntime;
    }

    // =========================================================
    // Resolve a constructed underground world without falling back to another.
    public CaveWorld GetUnderground(string id)
    {
        return _underground.TryGetValue(id, out CaveWorld cave)
            ? cave : throw new InvalidOperationException(
                $"No underground runtime exists for '{id}'.");
    }

    // =========================================================
    // Query optional ownership without creating any world or chunk.
    public CaveWorld TryGetUnderground(string id)
    {
        return id != null && _underground.TryGetValue(id, out CaveWorld cave)
            ? cave : null;
    }

    // =========================================================
    // Select the exact layer's world-object root.
    public Node2D ObjectsFor(string id)
    {
        return id == WorldLayerId.Surface
            ? _surfaceObjects : GetUnderground(id).Objects;
    }

    // =========================================================
    // Check completed terrain in the requested layer.
    public bool IsAvailable(string id, Vector2 point, float clearance = 0f)
    {
        return id == WorldLayerId.Surface
            ? _surfaceChunks.IsNavigationPointAvailable(point, clearance)
            : GetUnderground(id).Streaming.IsAvailable(point, clearance);
    }
    #endregion

    #region Activation
    // =========================================================
    // Activate one underground world and disable all other underground collisions.
    public void ActivateLayer(string id)
    {
        if (id != WorldLayerId.Surface) GetUnderground(id);
        ActiveLayer = id;

        foreach (CaveWorld cave in _underground.Values)
        {
            cave.SetActive(cave.LayerId == id);
            cave.Streaming.SetEntrancePreloading(
                id == WorldLayerId.Surface &&
                cave.LayerId == SurfaceEntranceLayerId);
        }
    }

    // =========================================================
    // Request destination preparation without revealing or activating its world.
    public void Preload(string id, Vector2 point)
    {
        GetUnderground(id).Streaming.RequestPreload(point);
    }

    // =========================================================
    // Release temporary preparation once a connection no longer needs it.
    public void CancelPreload(string id)
    {
        GetUnderground(id).Streaming.CancelPreload();
    }
    #endregion
}
