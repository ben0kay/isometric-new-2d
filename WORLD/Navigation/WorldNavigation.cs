// Queues local routes and caches small navigation grids for one world layer.
// Surface and cave instances share a scheduler while retaining separate terrain.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class WorldNavigation : Node
{
    #region Configuration
    public CaveWorld Cave { get; set; }
    public WorldLayer Layer => Cave == null
        ? WorldLayer.Surface : WorldLayer.Cave;

    public int CellSize { get; private set; } = 32;
    public float AgentClearance { get; private set; } = 12f;
    #endregion

    #region Records
    private sealed class GridEntry
    {
        public readonly AStarGrid2D Grid = new();
        public Rect2I Region;
        public IEnumerator<int> Build;
        public bool Valid = true, Ready;
        public int Users;
        public ulong LastUsed;

        // =========================================================
        // Release the iterator and native grid once no request needs them.
        public void Dispose()
        {
            Build?.Dispose();
            Build = null;
            Grid.Dispose();
        }
    }

private sealed class RouteJob
{
    public ulong Id;
    public EnemyMotor Owner;
    public CharacterBody2D Actor;
    public Vector2 Goal;
    public int Padding;
    public long Submitted;
    public bool Checked, WarmOnly;
    public GridEntry Grid;
}

    public readonly struct RouteResult
    {
        public readonly Vector2 Goal;
        public readonly Vector2[] Path;
        public readonly bool Direct, NoRoute;

        // =========================================================
        // Describe a completed route or a failure within the search limits.
        public RouteResult(
            Vector2 goal, Vector2[] path, bool direct, bool noRoute)
        {
            Goal = goal;
            Path = path;
            Direct = direct;
            NoRoute = noRoute;
        }
    }
    #endregion

    #region State
    private readonly List<GridEntry> _cache = new();
    private readonly Queue<RouteJob> _queue = new();
    private readonly Dictionary<ulong, RouteJob> _jobs = new();
    private readonly Dictionary<ulong, RouteResult> _results = new();
    private readonly Dictionary<ulong, Rect2> _failedAreas = new();
    private readonly HashSet<ulong> _changedFailures = new();
    private readonly List<Obstacle> _obstacles = new();

    private readonly PhysicsShapeQueryParameters2D _query = new();
    private readonly CircleShape2D _clearanceShape = new();

    private GlobalConfig _config;
    private NavigationWorkScheduler _scheduler;
    private ChunkController _chunks;
    private Node2D _objects, _ground;
    private SceneTree _tree;

    public bool IsBuilding => _jobs.Count > 0;
    public int PendingCount => _jobs.Count;
    public int CachedGridCount => _cache.Count;
    public int CacheHits { get; private set; }
    public int LastGridCellCount { get; private set; }
    public double LastRouteWaitMs { get; private set; }
    public double LastWorkMs { get; private set; }
    public double PeakStepMs { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve terrain, cache obstacles and join the shared work scheduler.
    public override void _Ready()
    {
        AddToGroup(Cave == null ? "world_navigation" : "cave_navigation");
        _config = WorldConfig.Find(this);
        CellSize = Mathf.Max(16, _config.NavigationCellSize);
        AgentClearance = Mathf.Max(1f, _config.NavigationAgentClearance);

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

        SetProcess(false);
        SetPhysicsProcess(false);
        _scheduler = NavigationWorkScheduler.Ensure(this);
        _scheduler.Register(this);
    }

    // =========================================================
    // Select navigation from the actor's layer ownership.
    public static WorldNavigation For(Node actor)
    {
        if (actor == null || !actor.IsInsideTree()) return null;
        string group = WorldLayerMember.For(actor) == WorldLayer.Cave
            ? "cave_navigation" : "world_navigation";

        return actor.GetTree().GetFirstNodeInGroup(group) as WorldNavigation;
    }

    // =========================================================
    // Disconnect listeners and release all cached native resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_scheduler))
            _scheduler.Unregister(this);

        if (_tree != null)
        {
            _tree.NodeAdded -= OnNodeAdded;
            _tree.NodeRemoved -= OnNodeRemoved;
        }

        if (Cave == null && GodotObject.IsInstanceValid(_chunks))
            _chunks.ChunkAvailabilityChanged -= OnSurfaceChunkChanged;
        else if (GodotObject.IsInstanceValid(Cave?.Streaming))
            Cave.Streaming.ChunkAvailabilityChanged -= OnCaveChunkChanged;

        foreach (GridEntry entry in _cache) entry.Dispose();
        _cache.Clear();
        _jobs.Clear();
        _queue.Clear();
        _results.Clear();
        _failedAreas.Clear();
        _changedFailures.Clear();
        _query.Dispose();
        _clearanceShape.Dispose();
    }

    // =========================================================
    // Cache newly loaded obstacles and invalidate their local area.
    private void OnNodeAdded(Node node)
    {
        if (node is not Obstacle obstacle ||
            !_objects.IsAncestorOf(obstacle)) return;

        _obstacles.Add(obstacle);
        InvalidateBounds(ObstacleBounds(obstacle));
    }

    // =========================================================
    // Remove departing obstacles and invalidate their former area.
    private void OnNodeRemoved(Node node)
    {
        if (node is Obstacle obstacle && _obstacles.Remove(obstacle))
            InvalidateBounds(ObstacleBounds(obstacle));
    }

    // =========================================================
    // Invalidate navigation touched by a changed surface chunk.
    private void OnSurfaceChunkChanged(Vector2I coordinate)
    {
        InvalidateChunk(coordinate, _chunks.ChunkSize, false);
    }

    // =========================================================
    // Invalidate navigation touched by a changed cave chunk.
    private void OnCaveChunkChanged(Vector2I coordinate)
    {
        InvalidateChunk(coordinate, Cave.Settings.ChunkSize, true);
    }

    // =========================================================
    // Project a chunk's tile rectangle into logical world bounds.
    private void InvalidateChunk(Vector2I coordinate, int size, bool cave)
    {
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
        InvalidateBounds(bounds);
    }

    // =========================================================
    // Invalidate intersecting grids and wake relevant failed-route retries.
    private void InvalidateBounds(Rect2 bounds)
    {
        bounds = bounds.Grow(AgentClearance + CellSize);

        foreach (GridEntry entry in _cache)
            if (WorldBounds(entry.Region).Intersects(bounds))
                entry.Valid = false;

        foreach (var failure in _failedAreas)
            if (failure.Value.Intersects(bounds))
                _changedFailures.Add(failure.Key);
    }

    // =========================================================
    // Read an obstacle's logical collision footprint.
    private static Rect2 ObstacleBounds(Obstacle obstacle)
    {
        return new Rect2(
            obstacle.GlobalPosition - obstacle.Footprint * 0.5f,
            obstacle.Footprint);
    }

    // =========================================================
    // Reset this layer's accumulated work time for the current tick.
    internal void BeginWorkTick()
    {
        LastWorkMs = 0.0;
    }

    // =========================================================
    // Retain existing HUD timing fields for navigation diagnostics.
    internal void RecordWork(double elapsed)
    {
        LastWorkMs += elapsed;
        PeakStepMs = Math.Max(PeakStepMs, elapsed);
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
                if (Crosses(ObstacleBounds(obstacle).Grow(radius), from, to))
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
    // Test a swept segment against an expanded obstacle rectangle.
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
    // Ignore departing obstacles and objects belonging to another layer.
    private bool ValidObstacle(Obstacle obstacle)
    {
        return GodotObject.IsInstanceValid(obstacle) &&
            obstacle.IsInsideTree() && !obstacle.IsQueuedForDeletion() &&
            WorldLayerMember.For(obstacle) == Layer;
    }
    #endregion

    #region Requests
// =========================================================
// Queue the same budgeted navigation work for robots and wildlife.
public void RequestRoute(
    EnemyMotor owner, CharacterBody2D actor, Vector2 goal)
{
    ulong id = owner.GetInstanceId();
    if (_jobs.ContainsKey(id)) return;

    _results.Remove(id);
    _failedAreas.Remove(id);
    _changedFailures.Remove(id);

    var job = new RouteJob
    {
        Id = id,
        Owner = owner,
        Actor = actor,
        Goal = goal,
        Padding = Mathf.Max(CellSize * 2,
            _config.NavigationInitialPadding),
        Submitted = Stopwatch.GetTimestamp()
    };

    _jobs.Add(id, job);
    _queue.Enqueue(job);
}

    // =========================================================
    // Test pending state without performing navigation work.
    public bool HasPending(EnemyMotor owner)
    {
        return _jobs.ContainsKey(owner.GetInstanceId());
    }

    // =========================================================
    // Consume a completed result once.
    public bool TryTakeRoute(EnemyMotor owner, out RouteResult result)
    {
        ulong id = owner.GetInstanceId();
        if (!_results.TryGetValue(id, out result)) return false;
        _results.Remove(id);
        return true;
    }

    // =========================================================
    // Allow an early retry after relevant local geometry changes.
    public bool FailedAreaChanged(EnemyMotor owner)
    {
        return _changedFailures.Contains(owner.GetInstanceId());
    }

    // =========================================================
    // Release requests and results when a motor stops or changes layer.
    public void Cancel(EnemyMotor owner)
    {
        ulong id = owner.GetInstanceId();
        if (_jobs.TryGetValue(id, out RouteJob job))
        {
            _jobs.Remove(id);
            ReleaseGrid(job);
        }

        _results.Remove(id);
        _failedAreas.Remove(id);
        _changedFailures.Remove(id);
    }

    // =========================================================
    // Advance one available job step; rotate waiting jobs fairly.
    internal bool Step(bool allowSearch, out bool searched)
    {
        searched = false;
        int candidates = _queue.Count;

        while (candidates-- > 0)
        {
            RouteJob job = _queue.Dequeue();

            if (!_jobs.TryGetValue(job.Id, out RouteJob current) ||
                current != job)
                return true;

            if (!GodotObject.IsInstanceValid(job.Owner) ||
                !GodotObject.IsInstanceValid(job.Actor) ||
                !job.Actor.IsInsideTree() ||
                job.Actor.IsQueuedForDeletion() ||
                WorldLayerMember.For(job.Actor) != Layer)
            {
                _jobs.Remove(job.Id);
                ReleaseGrid(job);
                _results.Remove(job.Id);
                _failedAreas.Remove(job.Id);
                _changedFailures.Remove(job.Id);
                return true;
            }

            if (job.Grid != null && !job.Grid.Valid)
                ReleaseGrid(job);

            if (!job.Checked)
            {
                job.Checked = true;

                if (CanTravelDirectly(job.Actor.GlobalPosition, job.Goal))
                {
                    Publish(job, true, Array.Empty<Vector2>(), false);
                    job.WarmOnly = true;
                }

                _queue.Enqueue(job);
                return true;
            }

            if (job.Grid == null)
            {
                Rect2I region = RequestedRegion(
                    job.Actor.GlobalPosition, job.Goal, job.Padding);

                long cells = (long)region.Size.X * region.Size.Y;
                bool tooLarge = cells >
                    Math.Max(64, _config.NavigationMaximumGridCells);

                if (tooLarge ||
                    !Available(job.Actor.GlobalPosition) ||
                    !Available(job.Goal))
                {
                    Finish(job, Array.Empty<Vector2>(), true, region);
                    return true;
                }

                GridEntry entry = AcquireGrid(region);
                if (entry == null)
                {
                    _queue.Enqueue(job);
                    continue;
                }

                job.Grid = entry;
                entry.Users++;
                entry.LastUsed = (ulong)Engine.GetPhysicsFrames();
                _queue.Enqueue(job);
                return true;
            }

            if (!job.Grid.Ready)
            {
                GridEntry entry = job.Grid;
                if (!entry.Build.MoveNext())
                {
                    entry.Build.Dispose();
                    entry.Build = null;
                    entry.Ready = true;
                }

                _queue.Enqueue(job);
                return true;
            }

            if (job.WarmOnly)
            {
                Finish(job, Array.Empty<Vector2>(), false, job.Grid.Region);
                return true;
            }

            if (!job.Grid.Region.HasPoint(
                    WorldToCell(job.Actor.GlobalPosition)))
            {
                ReleaseGrid(job);
                _queue.Enqueue(job);
                return true;
            }

            if (!allowSearch)
            {
                _queue.Enqueue(job);
                continue;
            }

            searched = true;
            Vector2[] path = CalculatePath(
                job.Grid, job.Actor.GlobalPosition, job.Goal);

            if (path.Length > 0)
            {
                Finish(job, path, false, job.Grid.Region);
                return true;
            }

            int maximum = Mathf.Max(
                _config.NavigationInitialPadding,
                _config.NavigationMaximumPadding);

            if (job.Padding >= maximum)
            {
                Finish(job, path, true, job.Grid.Region);
                return true;
            }

            ReleaseGrid(job);
            job.Padding = Mathf.Min(maximum, job.Padding * 2);
            _queue.Enqueue(job);
            return true;
        }

        return false;
    }

    // =========================================================
    // Publish results independently of the lifetime of cached grids.
    private void Publish(
        RouteJob job, bool direct, Vector2[] path, bool noRoute)
    {
        _results[job.Id] = new RouteResult(
            job.Goal, path, direct, noRoute);
        LastRouteWaitMs =
            NavigationWorkScheduler.ElapsedMs(job.Submitted);
    }

    // =========================================================
    // Complete a request; warm-up jobs preserve their earlier direct result.
    private void Finish(
        RouteJob job, Vector2[] path, bool failed, Rect2I region)
    {
        if (!job.WarmOnly)
        {
            Publish(job, false, path, failed);
            if (failed)
                _failedAreas[job.Id] = WorldBounds(region);
        }

        _jobs.Remove(job.Id);
        ReleaseGrid(job);
    }
    #endregion

    #region Grid Cache
    // =========================================================
    // Reuse covering grids or evict the least recently used unpinned grid.
    private GridEntry AcquireGrid(Rect2I region)
    {
        foreach (GridEntry entry in _cache)
        {
            if (!entry.Valid || !Covers(entry.Region, region)) continue;
            entry.LastUsed = (ulong)Engine.GetPhysicsFrames();
            CacheHits++;
            return entry;
        }

        int limit = Mathf.Max(1, _config.NavigationCachedGridsPerLayer);
        if (_cache.Count >= limit)
        {
            GridEntry oldest = null;
            foreach (GridEntry entry in _cache)
            {
                if (entry.Users > 0) continue;
                if (oldest == null || !entry.Valid ||
                    entry.LastUsed < oldest.LastUsed)
                    oldest = entry;
            }

            if (oldest == null) return null;
            _cache.Remove(oldest);
            oldest.Dispose();
        }

        var created = new GridEntry
        {
            Region = region,
            LastUsed = (ulong)Engine.GetPhysicsFrames()
        };

        created.Grid.CellSize = new Vector2(CellSize, CellSize);
        created.Grid.Offset = Vector2.One * (CellSize * 0.5f);
        created.Grid.DiagonalMode =
            AStarGrid2D.DiagonalModeEnum.OnlyIfNoObstacles;
        created.Grid.DefaultComputeHeuristic = AStarGrid2D.Heuristic.Octile;
        created.Grid.DefaultEstimateHeuristic = AStarGrid2D.Heuristic.Octile;
        created.Build = BuildGridSteps(created).GetEnumerator();

        LastGridCellCount = region.Size.X * region.Size.Y;
        _cache.Add(created);
        return created;
    }

    // =========================================================
    // Unpin a grid and dispose invalid entries once their last user leaves.
    private void ReleaseGrid(RouteJob job)
    {
        GridEntry entry = job.Grid;
        if (entry == null) return;

        job.Grid = null;
        entry.Users--;

        if (!entry.Valid && entry.Users == 0)
        {
            _cache.Remove(entry);
            entry.Dispose();
        }
    }

    // =========================================================
    // Build a padded rectangle around both endpoints, aligned for cache reuse.
    private Rect2I RequestedRegion(Vector2 source, Vector2 goal, int padding)
    {
        Vector2 lowPoint = new(
            Mathf.Min(source.X, goal.X) - padding,
            Mathf.Min(source.Y, goal.Y) - padding);
        Vector2 highPoint = new(
            Mathf.Max(source.X, goal.X) + padding,
            Mathf.Max(source.Y, goal.Y) + padding);

        Vector2I low = WorldToCell(lowPoint);
        Vector2I high = WorldToCell(highPoint) + Vector2I.One;

        low = new Vector2I(
            Mathf.FloorToInt(low.X / 4f) * 4,
            Mathf.FloorToInt(low.Y / 4f) * 4);
        high = new Vector2I(
            Mathf.CeilToInt(high.X / 4f) * 4,
            Mathf.CeilToInt(high.Y / 4f) * 4);

        return new Rect2I(low, high - low);
    }

    // =========================================================
    // Check complete rectangle coverage rather than only its endpoints.
    private static bool Covers(Rect2I outer, Rect2I inner)
    {
        return outer.Position.X <= inner.Position.X &&
            outer.Position.Y <= inner.Position.Y &&
            outer.End.X >= inner.End.X &&
            outer.End.Y >= inner.End.Y;
    }

    // =========================================================
    // Convert cell bounds into logical world bounds.
    private Rect2 WorldBounds(Rect2I region)
    {
        return new Rect2(
            new Vector2(region.Position.X, region.Position.Y) * CellSize,
            new Vector2(region.Size.X, region.Size.Y) * CellSize);
    }

    // =========================================================
    // Incrementally mark terrain and obstacle clearance in the local grid.
    private IEnumerable<int> BuildGridSteps(GridEntry entry)
    {
        AStarGrid2D grid = entry.Grid;
        Rect2I region = entry.Region;
        grid.Region = region;
        grid.Update();
        yield return 0;

        // Snapshot membership so streaming removals cannot skip later entries.
        Obstacle[] obstacles = _obstacles.ToArray();
        yield return 0;

        float clearance = AgentClearance + CellSize * 0.707107f;
        for (int y = region.Position.Y; y < region.End.Y; y++)
        for (int x = region.Position.X; x < region.End.X; x++)
        {
            Vector2I cell = new(x, y);
            if (!Available(grid.GetPointPosition(cell), clearance))
                grid.SetPointSolid(cell);
            yield return 0;
        }

        Rect2 bounds = WorldBounds(region);
        foreach (Obstacle obstacle in obstacles)
        {
            yield return 0;
            if (!ValidObstacle(obstacle)) continue;

            Rect2 blocked = ObstacleBounds(obstacle).Grow(
                AgentClearance + CellSize * 0.5f);
            if (!blocked.Intersects(bounds)) continue;

            Vector2I first = WorldToCell(blocked.Position);
            Vector2I last = WorldToCell(blocked.End);

            for (int y = Mathf.Max(first.Y, region.Position.Y);
                y <= Mathf.Min(last.Y, region.End.Y - 1); y++)
            for (int x = Mathf.Max(first.X, region.Position.X);
                x <= Mathf.Min(last.X, region.End.X - 1); x++)
            {
                Vector2I cell = new(x, y);
                if (blocked.HasPoint(grid.GetPointPosition(cell)))
                    grid.SetPointSolid(cell);
                yield return 0;
            }
        }
    }
    #endregion

    #region Path Searches
    // =========================================================
    // Search a completed grid and connect its waypoints to the exact goal.
    private Vector2[] CalculatePath(
        GridEntry entry, Vector2 source, Vector2 goal)
    {
        if (!FindOpenCell(entry, source, out Vector2I start) ||
            !FindOpenCell(entry, goal, out Vector2I end))
            return Array.Empty<Vector2>();

        Vector2[] points = entry.Grid.GetPointPath(start, end);
        if (points.Length == 0) return points;

        int first = 0;
        int limit = Math.Min(points.Length - 1, 6);
        for (int i = 1; i <= limit; i++)
        {
            if (!CanTravelDirectly(source, points[i])) break;
            first = i;
        }

        bool appendGoal =
            points[points.Length - 1].DistanceSquaredTo(goal) > 0.01f;
        int length = points.Length - first;
        var result = new Vector2[length + (appendGoal ? 1 : 0)];
        Array.Copy(points, first, result, 0, length);
        if (appendGoal) result[length] = goal;
        return result;
    }

    // =========================================================
    // Try the nearest cell first, then nearby rings with reachable clearance.
    private bool FindOpenCell(
        GridEntry entry, Vector2 point, out Vector2I result)
    {
        Vector2I centre = WorldToCell(point);
        result = centre;

        for (int ring = 0; ring <= 3; ring++)
        {
            float best = float.MaxValue;

            for (int y = -ring; y <= ring; y++)
            for (int x = -ring; x <= ring; x++)
            {
                if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring) continue;
                Vector2I cell = centre + new Vector2I(x, y);
                if (!entry.Region.HasPoint(cell) ||
                    entry.Grid.IsPointSolid(cell)) continue;

                Vector2 destination = entry.Grid.GetPointPosition(cell);
                float distance = point.DistanceSquaredTo(destination);
                if (distance >= best ||
                    !CanTravelDirectly(point, destination)) continue;

                best = distance;
                result = cell;
            }

            if (best < float.MaxValue) return true;
        }

        return false;
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