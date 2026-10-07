// Builds budgeted local navigation for either surface or cave terrain.
// Separate instances retain separate grids; hidden pursuit uses logical solids.
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

    public CaveWorld Cave { get; set; }
    public WorldLayer Layer => Cave == null
        ? WorldLayer.Surface : WorldLayer.Cave;
    #endregion

    #region State
    private readonly AStarGrid2D _grid = new();
    private readonly List<Obstacle> _obstacles = new();
    private readonly PhysicsShapeQueryParameters2D _query = new();
    private readonly CircleShape2D _clearanceShape = new();

    private ChunkController _chunks;
    private Node2D _objects, _ground;
    private SceneTree _tree;
    private Rect2I _cachedRegion;
    private int _revision, _builtRevision = -1;
    private IEnumerator<int> _build;
    private bool _gridReady;

    public bool IsBuilding => _build != null;
    public double LastWorkMs { get; private set; }
    public double PeakStepMs { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve shared world services and the owning layer's object root.
    public override void _Ready()
    {
        AddToGroup(Cave == null ? "world_navigation" : "cave_navigation");
        CellSize = Mathf.Max(16, CellSize);
        SearchRadius = Mathf.Max(512, SearchRadius);

        Node generator = GetTree().GetFirstNodeInGroup("world_generator");
        Node systems = generator.GetParent();
        _chunks = systems.GetNode<ChunkController>("ChunkController");
        _ground = systems.GetParent().GetNode<Node2D>("GroundChunks");
        _objects = Cave?.Objects ??
            systems.GetParent().GetNode<Node2D>("WorldObjects");

        _obstacles.AddRange(WorldPlacement.CollectObstacles(_objects));
        if (Cave == null)
            _chunks.ChunkAvailabilityChanged += OnSurfaceChunkChanged;
        else
            Cave.Streaming.ChunkAvailabilityChanged += OnCaveChunkChanged;

        _tree = GetTree();
        _tree.NodeAdded += OnNodeAdded;
        _tree.NodeRemoved += OnNodeRemoved;

        _clearanceShape.Radius = 10f;
        _query.Shape = _clearanceShape;
        _query.CollisionMask = 1u | ChasmFeature.CollisionLayer;
        _query.CollideWithAreas = false;

        _grid.CellSize = new Vector2(CellSize, CellSize);
        _grid.Offset = new Vector2(CellSize * 0.5f, CellSize * 0.5f);
        _grid.DiagonalMode =
            AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles;
        _grid.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Octile;
        _grid.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Octile;
        SetProcess(false);
    }

    // =========================================================
    // Select navigation from the actor's ownership, not the player's layer.
    public static WorldNavigation For(Node actor)
    {
        if (actor == null || !actor.IsInsideTree()) return null;
        string group = WorldLayerMember.For(actor) == WorldLayer.Cave
            ? "cave_navigation" : "world_navigation";
        return actor.GetTree().GetFirstNodeInGroup(group) as WorldNavigation;
    }

    // =========================================================
    // Disconnect topology listeners and release native navigation resources.
    public override void _ExitTree()
    {
        if (_tree != null)
        {
            _tree.NodeAdded -= OnNodeAdded;
            _tree.NodeRemoved -= OnNodeRemoved;
        }

        if (Cave == null && GodotObject.IsInstanceValid(_chunks))
            _chunks.ChunkAvailabilityChanged -= OnSurfaceChunkChanged;
        else if (GodotObject.IsInstanceValid(Cave?.Streaming))
            Cave.Streaming.ChunkAvailabilityChanged -= OnCaveChunkChanged;

        _build?.Dispose();
        _grid.Dispose();
        _query.Dispose();
        _clearanceShape.Dispose();
    }

    // =========================================================
    // Cache newly loaded logical obstacles without rescanning every frame.
    private void OnNodeAdded(Node node)
    {
        if (node is not Obstacle obstacle ||
            !_objects.IsAncestorOf(obstacle)) return;

        _obstacles.Add(obstacle);
        _revision++;
    }

    // =========================================================
    // Remove retired obstacles from the logical collision cache.
    private void OnNodeRemoved(Node node)
    {
        if (node is Obstacle obstacle && _obstacles.Remove(obstacle))
            _revision++;
    }

    // =========================================================
    // Invalidate only surface chunks intersecting the current navigation grid.
    private void OnSurfaceChunkChanged(Vector2I coordinate)
    {
        InvalidateChunk(coordinate, _chunks.ChunkSize, false);
    }

    // =========================================================
    // Invalidate only cave chunks intersecting the current navigation grid.
    private void OnCaveChunkChanged(Vector2I coordinate)
    {
        InvalidateChunk(coordinate, Cave.Settings.ChunkSize, true);
    }

    // =========================================================
    // Project one chunk's tile bounds into logical world coordinates.
    private void InvalidateChunk(Vector2I coordinate, int size, bool cave)
    {
        if (_cachedRegion.Size == Vector2I.Zero) return;

        Vector2 origin = new(
            coordinate.X * size - 0.5f,
            coordinate.Y * size - 0.5f);

        Vector2 Project(Vector2 tile) => cave
            ? Cave.TileToWorld(tile)
            : _ground.ToGlobal(IsoGrid.TileToWorld(tile, _chunks.TileSize));

        Rect2 bounds = new(Project(origin), Vector2.Zero);
        bounds = bounds.Expand(Project(origin + new Vector2(size, 0)));
        bounds = bounds.Expand(Project(origin + new Vector2(0, size)));
        bounds = bounds.Expand(Project(origin + Vector2.One * size));

        Rect2 gridBounds = new(
            new Vector2(_cachedRegion.Position.X * CellSize,
                _cachedRegion.Position.Y * CellSize),
            new Vector2(_cachedRegion.Size.X * CellSize,
                _cachedRegion.Size.Y * CellSize));

        if (gridBounds.Intersects(bounds.Grow(AgentClearance + CellSize)))
            _revision++;
    }

    // =========================================================
    // Resume navigation construction under its existing soft frame budget.
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

            _build.Dispose();
            _build = null;
            SetProcess(false);
        }
        LastWorkMs = ElapsedMs(started);
    }

    // =========================================================
    // Read monotonic elapsed time without allocating stopwatch objects.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion

    #region Travel
    // =========================================================
    // Read ready terrain from this navigation instance's layer.
    private bool Available(Vector2 point, float clearance = 0f)
    {
        return Cave != null
            ? Cave.Streaming.IsAvailable(point, clearance)
            : _chunks.IsNavigationPointAvailable(point, clearance);
    }

    // =========================================================
    // Sweep visible physics or check hidden-layer logical ground and solids.
    public bool CanTravelDirectly(Vector2 from, Vector2 to)
    {
        const float radius = 10f;
        if (!Available(from, radius) || !Available(to, radius))
            return false;

        bool visible = (WorldLayerController.Find(this)?.Current ??
            WorldLayer.Surface) == Layer;

        // Cave floor and hidden surface ground must remain continuous.
        if (Cave != null || !visible)
        {
            int steps = Mathf.Max(1,
                Mathf.CeilToInt(from.DistanceTo(to) / 16f));

            for (int i = 1; i <= steps; i++)
                if (!Available(from.Lerp(to, (float)i / steps), radius))
                    return false;
        }

        if (!visible)
        {
            foreach (Obstacle obstacle in _obstacles)
            {
                if (!ValidObstacle(obstacle)) continue;
                Rect2 footprint = new(
                    obstacle.GlobalPosition - obstacle.Footprint * 0.5f,
                    obstacle.Footprint);

                if (Crosses(footprint.Grow(radius), from, to))
                    return false;
            }
            return true;
        }

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
    // Keep cave and hidden-layer movement on loaded, traversable ground.
    public Vector2 ConstrainVelocity(
        Vector2 position, Vector2 velocity, double delta)
    {
        if (delta <= 0.0) return Vector2.Zero;

        bool visible = (WorldLayerController.Find(this)?.Current ??
            WorldLayer.Surface) == Layer;
        if (Cave == null && visible) return velocity;

        Vector2 motion = velocity * (float)delta;
        if (CanTravelDirectly(position, position + motion)) return velocity;

        bool x = CanTravelDirectly(
            position, position + new Vector2(motion.X, 0f));
        bool y = CanTravelDirectly(
            position, position + new Vector2(0f, motion.Y));

        if (x && (!y || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y)))
            return new Vector2(velocity.X, 0f);
        if (y) return new Vector2(0f, velocity.Y);
        return Vector2.Zero;
    }

    // =========================================================
    // Test a swept ground segment against an expanded obstacle rectangle.
    private static bool Crosses(Rect2 bounds, Vector2 from, Vector2 to)
    {
        float enter = 0f, exit = 1f;
        Vector2 motion = to - from;

        for (int axis = 0; axis < 2; axis++)
        {
            float start = axis == 0 ? from.X : from.Y;
            float direction = axis == 0 ? motion.X : motion.Y;
            float low = axis == 0 ? bounds.Position.X : bounds.Position.Y;
            float high = axis == 0 ? bounds.End.X : bounds.End.Y;

            if (Mathf.Abs(direction) < 0.00001f)
            {
                if (start < low || start > high) return false;
                continue;
            }

            float a = (low - start) / direction;
            float b = (high - start) / direction;
            enter = Mathf.Max(enter, Mathf.Min(a, b));
            exit = Mathf.Min(exit, Mathf.Max(a, b));
            if (enter > exit) return false;
        }
        return true;
    }

    // =========================================================
    // Ignore departing obstacles and obstacles owned by another layer.
    private bool ValidObstacle(Obstacle obstacle)
    {
        return GodotObject.IsInstanceValid(obstacle) &&
            obstacle.IsInsideTree() && !obstacle.IsQueuedForDeletion() &&
            WorldLayerMember.For(obstacle) == Layer;
    }
    #endregion

    #region Paths
    // =========================================================
    // Request a completed local grid before returning a path.
    public Vector2[] FindPath(Vector2 from, Vector2 to)
    {
        if (!Available(from) || !Available(to) || !PrepareGrid(from, to) ||
            !FindOpenCell(from, out Vector2I start) ||
            !FindOpenCell(to, out Vector2I goal))
            return System.Array.Empty<Vector2>();

        if (!CanTravelDirectly(from, _grid.GetPointPosition(start)))
            return System.Array.Empty<Vector2>();

        return _grid.GetPointPath(start, goal);
    }

    // =========================================================
    // Find a nearby open cell reachable without crossing a solid.
    private bool FindOpenCell(Vector2 point, out Vector2I result)
    {
        Vector2I centre = WorldToCell(point);
        result = centre;
        float best = float.MaxValue;

        for (int y = -3; y <= 3; y++)
        for (int x = -3; x <= 3; x++)
        {
            Vector2I cell = centre + new Vector2I(x, y);
            if (!_cachedRegion.HasPoint(cell) || _grid.IsPointSolid(cell))
                continue;

            Vector2 destination = _grid.GetPointPosition(cell);
            float distance = point.DistanceSquaredTo(destination);
            if (distance >= best || !CanTravelDirectly(point, destination))
                continue;

            result = cell;
            best = distance;
        }
        return best < float.MaxValue;
    }

    // =========================================================
    // Reuse covering grids and let pending construction finish.
    private bool PrepareGrid(Vector2 source, Vector2 destination)
    {
        Vector2I start = WorldToCell(source);
        Vector2I goal = WorldToCell(destination);

        if (_gridReady && _builtRevision == _revision &&
            _cachedRegion.HasPoint(start) && _cachedRegion.HasPoint(goal))
            return true;
        if (_build != null) return false;

        int bucket = CellSize * 8;
        Vector2 anchor = new(
            Mathf.Floor(source.X / bucket) * bucket,
            Mathf.Floor(source.Y / bucket) * bucket);

        Vector2I low = WorldToCell(anchor - Vector2.One * SearchRadius);
        Vector2I high = WorldToCell(anchor + Vector2.One * SearchRadius);
        Rect2I region = new(low, high - low + Vector2I.One);

        if (!region.HasPoint(start) || !region.HasPoint(goal)) return false;

        _cachedRegion = region;
        _gridReady = false;
        _build = BuildGridSteps(region, _revision).GetEnumerator();
        SetProcess(true);
        return false;
    }

    // =========================================================
    // Incrementally mark terrain and logical obstacle clearance.
    private IEnumerable<int> BuildGridSteps(Rect2I region, int revision)
    {
        _grid.Region = region;
        _grid.Update();
        yield return 0;

        float clearance = AgentClearance + CellSize * 0.707107f;

        for (int y = region.Position.Y; y < region.End.Y; y++)
        for (int x = region.Position.X; x < region.End.X; x++)
        {
            yield return 0;
            Vector2I cell = new(x, y);
            if (!Available(_grid.GetPointPosition(cell), clearance))
                _grid.SetPointSolid(cell);
        }

        for (int i = 0; i < _obstacles.Count; i++)
        {
            Obstacle obstacle = _obstacles[i];
            yield return 0;
            if (!ValidObstacle(obstacle)) continue;

            Rect2 blocked = new Rect2(
                obstacle.GlobalPosition - obstacle.Footprint * 0.5f,
                obstacle.Footprint).Grow(AgentClearance + CellSize * 0.5f);

            Vector2I first = WorldToCell(blocked.Position);
            Vector2I last = WorldToCell(blocked.End);

            for (int y = Mathf.Max(first.Y, region.Position.Y);
                y <= Mathf.Min(last.Y, region.End.Y - 1); y++)
            for (int x = Mathf.Max(first.X, region.Position.X);
                x <= Mathf.Min(last.X, region.End.X - 1); x++)
            {
                yield return 0;
                Vector2I cell = new(x, y);
                if (blocked.HasPoint(_grid.GetPointPosition(cell)))
                    _grid.SetPointSolid(cell);
            }
        }

        _builtRevision = revision;
        _gridReady = true;
    }

    // =========================================================
    // Convert logical world coordinates into navigation cells.
    private Vector2I WorldToCell(Vector2 point)
    {
        return new Vector2I(
            Mathf.FloorToInt(point.X / CellSize),
            Mathf.FloorToInt(point.Y / CellSize));
    }
    #endregion
}