// Prepares nearby cave metadata under a small budget without loading chunks.
// Combines cave entrances and optional authored markers into map entries.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public sealed class WorldMapPoiPreview : IDisposable
{
    #region Entries
    public readonly record struct Entry(
        string Name, Vector2 Tile, DebugMapPoiKind Kind);
    #endregion

    #region State
    private readonly Node _context;
    private readonly Node2D _ground;
    private readonly ChunkController _chunks;
    private readonly CaveWorld _cave;
    private readonly double _budget;
    private readonly List<Entry> _entries = new();

    private IDisposable _protection;
    private IEnumerator<int> _work;
    private double _refreshTimer;

    public float Radius { get; }
    public bool Building => _work != null;
    public IReadOnlyList<Entry> Entries => _entries;
    #endregion

    #region Construction
    // =========================================================
    // Protect nearby decisions while preparing only their generation metadata.
    public WorldMapPoiPreview(
        Node context, Node2D ground, ChunkController chunks,
        WorldConfig config, Vector2 centre)
    {
        _context = context;
        _ground = ground;
        _chunks = chunks;
        Radius = config.DebugMapPoiRadiusTiles;
        _budget = config.DebugMapPoiBudgetMs;

        if (!float.IsFinite(Radius) || Radius < 16f)
            throw new InvalidOperationException(
                "DebugMapPoiRadiusTiles must be finite and at least 16.");

        if (!double.IsFinite(_budget) || _budget <= 0)
            throw new InvalidOperationException(
                "DebugMapPoiBudgetMs must be finite and positive.");

        _cave = context.GetTree().GetFirstNodeInGroup(
            "cave_world") as CaveWorld;

        InfiniteWorldGeneration generation =
            InfiniteWorldGeneration.Find(context);

        if (_cave != null && generation != null)
        {
            Rect2 area = new(
                centre - Vector2.One * Radius,
                Vector2.One * (Radius * 2f));

            _protection = generation.PinArea(area);

            try
            {
                _work = generation.PrepareArea(area).GetEnumerator();
            }
            catch
            {
                _protection.Dispose();
                _protection = null;
                throw;
            }
        }

        Refresh(centre);
    }
    #endregion

    #region Updates
    // =========================================================
    // Resume metadata preparation and periodically refresh nearby marker entries.
    public void Tick(double delta, Vector2 playerTile)
    {
        if (_work != null)
        {
            long started = Stopwatch.GetTimestamp();

            while ((Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency < _budget)
            {
                if (_work.MoveNext()) continue;

                _work.Dispose();
                _work = null;
                _refreshTimer = 0;
                break;
            }
        }

        _refreshTimer -= delta;
        if (_refreshTimer > 0) return;
        _refreshTimer = 0.25;
        Refresh(playerTile);
    }

    // =========================================================
    // Gather only markers inside the configured logical tile radius.
    private void Refresh(Vector2 playerTile)
    {
        _entries.Clear();
        float squared = Radius * Radius;

        if (GodotObject.IsInstanceValid(_cave))
            foreach (CaveHole hole in _cave.Holes)
                if (playerTile.DistanceSquaredTo(hole.MouthTile) <= squared)
                    _entries.Add(new Entry(
                        hole.Id, hole.MouthTile,
                        DebugMapPoiKind.CaveEntrance));

        foreach (Node node in _context.GetTree().GetNodesInGroup(
            "debug_map_poi"))
        {
            if (node is not DebugMapPoi poi ||
                poi.IsQueuedForDeletion())
                continue;

            Vector2 tile = IsoGrid.WorldToTile(
                _ground.ToLocal(poi.GlobalPosition), _chunks.TileSize);

            if (playerTile.DistanceSquaredTo(tile) <= squared)
                _entries.Add(new Entry(poi.DisplayName, tile, poi.Kind));
        }
    }
    #endregion

    #region Cleanup
    // =========================================================
    // Release suspended work and map-owned metadata protection.
    public void Dispose()
    {
        _work?.Dispose();
        _work = null;
        _protection?.Dispose();
        _protection = null;
        _entries.Clear();
    }
    #endregion
}