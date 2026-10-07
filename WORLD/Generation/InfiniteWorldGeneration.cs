// Connects local surface metadata and the shared infinite cave network.
// Both chunk streamers prepare the same seeded reservations before construction.
using Godot;
using System;
using System.Collections.Generic;

public partial class InfiniteWorldGeneration : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    [Export] public CaveGenerationSettings GenerationSettings { get; set; }
    #endregion

    #region State
    private Node _world;
    private WaterBasinWorld _basins;
    private CaveEntrancePlanner _planner;
    private bool _initialized;

    public CaveWorld Cave { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before streamers begin their first frame of construction.
    public override void _EnterTree()
    {
        AddToGroup("infinite_generation");
    }

    // =========================================================
    // Build shared services without scanning any world-sized area.
    public override void _Ready()
    {
        _world = GetParent();
        _basins = WaterBasinWorld.Find(_world);

        ChunkController chunks =
            _world.GetNode<ChunkController>("Systems/ChunkController");
        WorldConfig config = WorldConfig.Find(_world);

        if (_basins == null)
            throw new InvalidOperationException("Missing water basin service.");

        if (!float.IsFinite(config.CaveDiscoveryRadiusTiles) ||
            config.CaveDiscoveryRadiusTiles < 128f)
            throw new InvalidOperationException(
                "CaveDiscoveryRadiusTiles must be finite and at least 128.");

        if (Enabled && config.GenerateCaves)
        {
            CaveGenerationSettings settings = GenerationSettings ??
                GD.Load<CaveGenerationSettings>(
                    "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

            if (settings == null)
                throw new InvalidOperationException("Missing cave settings.");

            _planner = new CaveEntrancePlanner(_world, chunks, settings);
            Cave = new CaveWorld { Name = "CaveWorld", Planner = _planner };
            AddChild(Cave);

            Node2D ground = _world.GetNode<Node2D>("GroundChunks");
            Cave.Build(
                ground.GlobalPosition, chunks.TileSize, 0f,
                chunks.WorldSeed, _planner.Settings, Array.Empty<CaveHole>());

            WorldLayerController layers = new() { Name = "WorldLayers" };
            AddChild(layers);
            layers.Configure(
                _world, _world.GetNode<Player>("WorldObjects/Player"), Cave);
        }

        _initialized = true;
        SetProcess(false);
    }

    // =========================================================
    // Resolve shared planning from either layer's chunk builder.
    public static InfiniteWorldGeneration Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup(
            "infinite_generation") as InfiniteWorldGeneration;
    }
    #endregion

    #region Local Preparation
    // =========================================================
    // Finish local water and entrance decisions before geometry or props sample them.
    public IEnumerable<int> PrepareArea(Rect2 area)
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Infinite generation did not initialize successfully.");

        foreach (int step in _basins.PrepareArea(area.Grow(2f)))
            yield return step;

        if (_planner != null)
            foreach (int step in _planner.PrepareArea(area, Cave))
                yield return step;
    }
    #endregion

        // =========================================================
    // Hold both surface and entrance metadata until the owning chunk retires.
    public IDisposable PinArea(Rect2 area)
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Infinite generation did not initialize successfully.");

        IDisposable water = _basins.PinArea(area.Grow(4f));
        IDisposable entrances = null;

        try
        {
            entrances = _planner?.PinArea(area);
        }
        catch
        {
            water.Dispose();
            throw;
        }

        return new GenerationLease(() =>
        {
            entrances?.Dispose();
            water.Dispose();
        });
    }
}