// Streams cave geometry around the player under a soft frame budget.
// Completed floor data also supplies layer-specific movement availability.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class CaveChunkController : Node
{
    #region State
    public int LoadedCount => _chunks.Count;
    public int ReadyCount
    {
        get
        {
            int count = 0;
            foreach (CaveChunk chunk in _chunks.Values)
                if (chunk.Ready) count++;
            return count;
        }
    }

    private CaveWorld _world;
    private Player _player;
    private bool _active, _preloadEntrances;
    private Vector2? _preloadPoint;
    private Vector2I _focus;
    private readonly Dictionary<Vector2I, CaveChunk> _chunks = new();
    private CaveChunk _building;
    private IEnumerator<int> _work;
    private bool _failed;
    #endregion

    #region Navigation Updates
public event Action<Vector2I> ChunkAvailabilityChanged;
#endregion

    #region Lifecycle
    // =========================================================
    // Connect the cave before enabling any streaming work.
    public void Configure(CaveWorld world)
    {
        _world = world;
        SetProcess(false);
    }

    // =========================================================
    // Supply the shared player without creating a second cave player.
    public void ConfigurePlayer(Player player)
    {
        _player = player;
    }

    // =========================================================
    // Stream around the cave player or prepare the nearest discovered entrance.
    public override void _Process(double delta)
    {
        if (_world == null || _failed ||
            !GodotObject.IsInstanceValid(_player))
            return;

        try
        {
            Vector2 tile;

            if (_active)
                tile = _world.WorldToTile(_player.GlobalPosition);
            else if (_preloadPoint.HasValue)
                tile = _world.WorldToTile(_preloadPoint.Value);
            else
            {
                if (!_preloadEntrances) return;
                CaveHole hole =
                    _world.NearestSurfaceHole(_player.GlobalPosition);
                if (hole == null) return;
                tile = hole.MouthTile;
            }

            _focus = CoordinateAt(tile);
            RunBuildBudget();
            RetireDistant();
        }
        catch (Exception error)
        {
            _failed = true;
            _work?.Dispose();
            _work = null;
            GD.PushError($"[Caves] Streaming failed: {error}");
        }
    }

    // =========================================================
    // Dispose unfinished CPU work when the cave feature is removed.
    public override void _ExitTree()
    {
        _work?.Dispose();
        _work = null;
    }
    #endregion

    #region Activation And Availability
    // =========================================================
    // Prepare surface mouths only in the layer explicitly connected to surface.
    public void SetEntrancePreloading(bool enabled)
    {
        _preloadEntrances = enabled;
        SetProcess(_active || enabled || _preloadPoint.HasValue);
    }

    // =========================================================
    // Prepare a requested destination without enabling its collisions.
    public void RequestPreload(Vector2 point)
    {
        _preloadPoint = point;
        SetProcess(true);
    }

    // =========================================================
    // Stop temporary work while retaining already-built destination chunks.
    public void CancelPreload()
    {
        _preloadPoint = null;
        SetProcess(_active || _preloadEntrances);
    }


    // =========================================================
    // Change collision and visibility for every completed cave chunk.
    public void SetActive(bool active)
    {
        _active = active;
        SetProcess(_active || _preloadEntrances || _preloadPoint.HasValue);
        foreach (CaveChunk chunk in _chunks.Values)
            chunk.SetActive(active);
    }

    // =========================================================
    // Require a completed buffer around an actual registered mouth.
    public bool EntryReady(CaveHole hole = null)
    {
        if (_failed || hole == null) return false;

        Vector2I centre = CoordinateAt(hole.MouthTile);

        for (int x = centre.X - 1; x <= centre.X + 1; x++)
        for (int y = centre.Y - 1; y <= centre.Y + 1; y++)
            if (!_chunks.TryGetValue(new Vector2I(x, y), out CaveChunk chunk) ||
                !chunk.Ready)
                return false;

        return true;
    }

    // =========================================================
    // Test the player's footprint against ready floor, including chunk edges.
    public bool IsAvailable(Vector2 point, float clearance = 14f)
    {
        if (_failed || !PointAvailable(point)) return false;

        for (int i = 0; i < 16; i++)
        {
            Vector2 sample = point +
                Vector2.FromAngle(Mathf.Tau * i / 16f) * clearance;
            if (!PointAvailable(sample)) return false;
        }
        return true;
    }

// =========================================================
// Permit the small outward apron at either mouth, then require ready cave floor.
private bool PointAvailable(Vector2 point)
{
    Vector2 tile = _world.WorldToTile(point);

    foreach (CaveHole hole in _world.Holes)
    {
        Vector2 local = hole.Coordinates(tile);
        if (local.X >= -1.5f && local.X < 0f &&
            Mathf.Abs(local.Y) < 1.4f)
            return true;
    }

    Vector2I cell = new(
        Mathf.FloorToInt(tile.X + 0.5f),
        Mathf.FloorToInt(tile.Y + 0.5f));

    return _chunks.TryGetValue(CoordinateForCell(cell), out CaveChunk chunk) &&
        chunk.HasFloor(cell);
}

    // =========================================================
    // Map continuous tile coordinates to the same tile-centred chunk convention.
    private Vector2I CoordinateAt(Vector2 tile)
    {
        return CoordinateForCell(new Vector2I(
            Mathf.FloorToInt(tile.X + 0.5f),
            Mathf.FloorToInt(tile.Y + 0.5f)));
    }

    // =========================================================
    // Use floor division so negative chunk coordinates remain correct.
    private Vector2I CoordinateForCell(Vector2I cell)
    {
        int size = _world.Settings.ChunkSize;
        return new Vector2I(
            Mathf.FloorToInt((float)cell.X / size),
            Mathf.FloorToInt((float)cell.Y / size));
    }
    #endregion

    #region Scheduling
// =========================================================
// Build ready cave chunks and notify the cave navigation cache.
private void RunBuildBudget()
{
    long started = Stopwatch.GetTimestamp();

    while (ElapsedMs(started) < _world.Settings.BuildBudgetMs)
    {
        if (_building == null)
        {
            Vector2I? coordinate = NextCoordinate();
            if (coordinate == null) break;

            _building = new CaveChunk
            {
                Name = $"CaveChunk_{coordinate.Value.X}_{coordinate.Value.Y}"
            };
            _world.Root.AddChild(_building);
            _building.Configure(_world, coordinate.Value);
            _chunks.Add(coordinate.Value, _building);
            _work = _building.BuildSteps().GetEnumerator();
        }

        if (_work.MoveNext()) continue;

        _work.Dispose();
        _work = null;
        _building.Finish(_active);
        ChunkAvailabilityChanged?.Invoke(_building.Coordinate);
        _building = null;
    }
}

    // =========================================================
    // Build nearest missing chunks first instead of following dictionary order.
    private Vector2I? NextCoordinate()
    {
        int radius = _world.Settings.LoadRadiusChunks;
        Vector2I? best = null;
        int bestDistance = int.MaxValue;

        for (int x = _focus.X - radius; x <= _focus.X + radius; x++)
        for (int y = _focus.Y - radius; y <= _focus.Y + radius; y++)
        {
            Vector2I coordinate = new(x, y);
            if (_chunks.ContainsKey(coordinate)) continue;

            int dx = x - _focus.X, dy = y - _focus.Y;
            int distance = dx * dx + dy * dy;
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = coordinate;
        }

        return best;
    }

// =========================================================
// Retire cave chunks gradually while protecting active pursuit routes.
private void RetireDistant()
{
    int radius = _world.Settings.RetainRadiusChunks;
    int limit = _world.Settings.RetireChunksPerFrame;
    List<Vector2I> remove = new();
    CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);

    foreach (var pair in _chunks)
    {
        if (pair.Value == _building) continue;

        Vector2I difference = pair.Key - _focus;
        if ((Mathf.Abs(difference.X) <= radius &&
            Mathf.Abs(difference.Y) <= radius) ||
            pursuit?.RetainCave(_world, pair.Key) == true)
            continue;

        remove.Add(pair.Key);
        if (remove.Count >= limit) break;
    }

    foreach (Vector2I coordinate in remove)
    {
        CaveChunk chunk = _chunks[coordinate];
        chunk.SetActive(false);
        chunk.Visible = false;
        chunk.QueueFree();
        _chunks.Remove(coordinate);
        ChunkAvailabilityChanged?.Invoke(coordinate);
    }
}

    // =========================================================
    // Measure monotonic elapsed time without allocating stopwatch objects.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion
}
