// Builds shared local A* navigation incrementally, with a separate frame budget.
// Keeps pathfinding independent from enemy targeting and movement.
using Godot;
using System.Collections.Generic;
using System.Diagnostics;

public partial class WorldNavigation : Node
{
    #region Configuration
    [Export] public double BuildBudgetMs { get; set; } = 0.35;
    [Export] public int CellSize { get; set; } = 32;
    [Export] public int SearchRadius { get; set; } = 1280;
    [Export] public float AgentClearance { get; set; } = 12f;
    #endregion

    #region State
    private readonly AStarGrid2D _grid = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly PhysicsShapeQueryParameters2D _query = new();
    private readonly CircleShape2D _clearanceShape = new();

    private ChunkController _chunks;
    private Node2D _objects;
    private SceneTree _tree;
    private Rect2I _cachedRegion;
    private int _revision, _builtRevision = -1;
    private IEnumerator<int> _build;
    private bool _gridReady;
    private Node2D _ground;
    public bool IsBuilding => _build != null;
    public double LastWorkMs { get; private set; }
    public double PeakStepMs { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the service and track topology changes from chunk streaming.
    public override void _Ready()
    {
        AddToGroup("world_navigation");
        CellSize = Mathf.Max(16, CellSize);
        SearchRadius = Mathf.Max(512, SearchRadius);
        _chunks = GetNode<ChunkController>("../ChunkController");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _chunks.ChunkAvailabilityChanged += OnChunkAvailabilityChanged;
        SetProcess(false);
        _tree = GetTree();
        _tree.NodeAdded += OnWorldNodeChanged;
        _tree.NodeRemoved += OnWorldNodeChanged;

        _clearanceShape.Radius = AgentClearance;
        _query.Shape = _clearanceShape;
        _query.CollisionMask = 1u | ChasmFeature.CollisionLayer;
        _query.CollideWithAreas = false;

        _grid.CellSize = new Vector2(CellSize, CellSize);
        _grid.Offset = new Vector2(CellSize * 0.5f, CellSize * 0.5f);
        _grid.DiagonalMode = AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles;
        _grid.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Octile;
        _grid.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Octile;
    }

    // =========================================================
    // Remove tree subscriptions and dispose navigation resources.
    public override void _ExitTree()
    {
        if (_tree != null)
        {
            _tree.NodeAdded -= OnWorldNodeChanged;
            _tree.NodeRemoved -= OnWorldNodeChanged;
        }
        if (GodotObject.IsInstanceValid(_chunks))
            _chunks.ChunkAvailabilityChanged -= OnChunkAvailabilityChanged;
        _build?.Dispose(); _build = null;
        _grid.Dispose();
        _query.Dispose();
        _clearanceShape.Dispose();
    }

    // =========================================================
    // Invalidate cached navigation when static world topology changes.
    private void OnWorldNodeChanged(Node node)
    {
        if (node is not Obstacle obstacle || _cachedRegion.Size == Vector2I.Zero) return;
        Rect2 footprint = new(obstacle.GlobalPosition - obstacle.Footprint * 0.5f, obstacle.Footprint);
        if (GridWorldBounds().Intersects(footprint.Grow(AgentClearance + CellSize))) _revision++;
    }

    // =========================================================
    // Ignore distant preparation; invalidate only ready/retiring chunks overlapping this grid.
    private void OnChunkAvailabilityChanged(Vector2I coordinate)
    {
        if (_cachedRegion.Size == Vector2I.Zero) return;
        Vector2 origin = new(coordinate.X * _chunks.ChunkSize - 0.5f,
            coordinate.Y * _chunks.ChunkSize - 0.5f);
        Vector2 a = _ground.ToGlobal(IsoGrid.TileToWorld(origin, _chunks.TileSize));
        Rect2 bounds = new(a, Vector2.Zero);
        bounds = bounds.Expand(_ground.ToGlobal(IsoGrid.TileToWorld(origin + new Vector2(_chunks.ChunkSize, 0), _chunks.TileSize)));
        bounds = bounds.Expand(_ground.ToGlobal(IsoGrid.TileToWorld(origin + new Vector2(0, _chunks.ChunkSize), _chunks.TileSize)));
        bounds = bounds.Expand(_ground.ToGlobal(IsoGrid.TileToWorld(origin + Vector2.One * _chunks.ChunkSize, _chunks.TileSize)));
        if (GridWorldBounds().Intersects(bounds.Grow(AgentClearance + CellSize))) _revision++;
    }

    // =========================================================
    // Return the logical world rectangle covered by the cached navigation grid.
    private Rect2 GridWorldBounds()
    {
        Vector2 position = new(_cachedRegion.Position.X * CellSize, _cachedRegion.Position.Y * CellSize);
        Vector2 size = new(_cachedRegion.Size.X * CellSize, _cachedRegion.Size.Y * CellSize);
        return new Rect2(position, size);
    }

    // =========================================================
    // Build a small portion of the grid each frame; path queries wait for a complete grid.
    public override void _Process(double delta)
    {
        long started = Stopwatch.GetTimestamp();
        double budget = System.Math.Max(0.05, BuildBudgetMs);
        while (_build != null && ElapsedMs(started) < budget)
        {
            long step = Stopwatch.GetTimestamp();
            bool more = _build.MoveNext();
            PeakStepMs = System.Math.Max(PeakStepMs, ElapsedMs(step));
            if (more) continue;
            _build.Dispose(); _build = null;
            SetProcess(false);
        }
        LastWorkMs = ElapsedMs(started);
    }

    // =========================================================
    // Read monotonic elapsed milliseconds without allocating a Stopwatch instance.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
    }
    #endregion

    #region Queries
// =========================================================
// Check loaded ground and sweep the enemy body against rocks and chasms.
public bool CanTravelDirectly(Vector2 from, Vector2 to)
{
    const float bodyRadius = 10f;
    if (!_chunks.IsNavigationPointAvailable(from) ||
        !_chunks.IsNavigationPointAvailable(to)) return false;

    _clearanceShape.Radius = bodyRadius;
    _query.Margin = 0f;
    _query.Transform = new Transform2D(0f, from);
    _query.Motion = Vector2.Zero;

    var space = _objects.GetWorld2D().DirectSpaceState;
    if (space.IntersectShape(_query, 1).Count > 0) return false;
    if (from.DistanceSquaredTo(to) <= 0.000001f) return true;

    _query.Motion = to - from;
    float[] result = space.CastMotion(_query);
    return result.Length >= 2 && result[0] >= 0.9999f;
}

    // =========================================================
    // Find a path through loaded ground with clearance around obstacle bases.
    public Vector2[] FindPath(Vector2 from, Vector2 to)
    {
        if (!_chunks.IsNavigationPointAvailable(from) || !_chunks.IsNavigationPointAvailable(to))
            return System.Array.Empty<Vector2>();

        if (!PrepareGrid(from)) return System.Array.Empty<Vector2>();
        if (!FindOpenCell(from, out Vector2I start) || !FindOpenCell(to, out Vector2I goal))
            return System.Array.Empty<Vector2>();

        Vector2 startPoint = _grid.GetPointPosition(start);
        if (!CanTravelDirectly(from, startPoint)) return System.Array.Empty<Vector2>();
        return _grid.GetPointPath(start, goal);
    }

// =========================================================
// Find the closest open cell that can actually be reached from
// the supplied position without crossing an obstacle.
private bool FindOpenCell(Vector2 point, out Vector2I result)
{
    Vector2I centre = WorldToCell(point);
    result = centre;
    float bestDistance = float.MaxValue;

    for (int y = -3; y <= 3; y++)
    for (int x = -3; x <= 3; x++)
    {
        Vector2I cell = centre + new Vector2I(x, y);
        if (!_cachedRegion.HasPoint(cell) || _grid.IsPointSolid(cell)) continue;

        Vector2 cellPoint = _grid.GetPointPosition(cell);
        float distance = cellPoint.DistanceSquaredTo(point);
        if (distance >= bestDistance) continue;
        if (!CanTravelDirectly(point, cellPoint)) continue;

        result = cell;
        bestDistance = distance;
    }
    return bestDistance < float.MaxValue;
}
    #endregion

    #region Grid
// =========================================================
// Request an incremental grid rebuild when its region or relevant topology changes.
private bool PrepareGrid(Vector2 source)
{
    int bucket = CellSize * 8;
    Vector2 anchor = new(
        Mathf.Floor(source.X / bucket) * bucket,
        Mathf.Floor(source.Y / bucket) * bucket);
    Vector2I low = WorldToCell(anchor - Vector2.One * SearchRadius);
    Vector2I high = WorldToCell(anchor + Vector2.One * SearchRadius);
    Rect2I region = new(low, high - low + Vector2I.One);
    if (_gridReady && _builtRevision == _revision && region == _cachedRegion) return true;
    if (_build != null && region == _cachedRegion) return false;
    _build?.Dispose();
    _cachedRegion = region; _gridReady = false;
    _build = BuildGridSteps(region, _revision).GetEnumerator();
    SetProcess(true);
    return false;
}

// =========================================================
// Fill terrain cells incrementally, then stamp currently loaded solid footprints.
private IEnumerable<int> BuildGridSteps(Rect2I region, int revision)
{
    _grid.Region = region;
    _grid.Update();
    yield return 0;

    float terrainClearance = AgentClearance + CellSize * 0.707107f;
    for (int y = region.Position.Y; y < region.End.Y; y++)
    for (int x = region.Position.X; x < region.End.X; x++)
    {
        yield return 0;
        Vector2I cell = new(x, y);
        if (!_chunks.IsNavigationPointAvailable(_grid.GetPointPosition(cell), terrainClearance))
            _grid.SetPointSolid(cell);
    }

    _obstacles.Clear();
    _obstacles.AddRange(WorldPlacement.CollectObstacles(_objects));
    yield return 0;

    foreach (Obstacle obstacle in _obstacles)
    {
        yield return 0;
        if (!GodotObject.IsInstanceValid(obstacle) || obstacle.IsQueuedForDeletion()) continue;
        Rect2 footprint = new(obstacle.GlobalPosition - obstacle.Footprint * 0.5f, obstacle.Footprint);
        Rect2 blocked = footprint.Grow(AgentClearance + CellSize * 0.5f);
        Vector2I first = WorldToCell(blocked.Position);
        Vector2I last = WorldToCell(blocked.End);

        for (int y = Mathf.Max(first.Y, region.Position.Y); y <= Mathf.Min(last.Y, region.End.Y - 1); y++)
        for (int x = Mathf.Max(first.X, region.Position.X); x <= Mathf.Min(last.X, region.End.X - 1); x++)
        {
            yield return 0;
            Vector2I cell = new(x, y);
            if (blocked.HasPoint(_grid.GetPointPosition(cell))) _grid.SetPointSolid(cell);
        }
    }
    _builtRevision = revision; _gridReady = true;
}

    // =========================================================
    // Convert a world position to a navigation cell, including negative positions.
    private Vector2I WorldToCell(Vector2 point)
    {
        return new Vector2I(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Y / CellSize));
    }
    #endregion
}
