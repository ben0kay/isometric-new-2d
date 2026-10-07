// Caches global half-tile slope classifications from the rendered height field.
// Shared cells keep steep-rock appearance, collision and navigation consistent.
using Godot;
using System;
using System.Collections.Generic;

public partial class TerrainSlopeWorld : Node
{
    #region Samples
    public readonly struct SlopeSample
    {
        public readonly Vector2 Gradient;
        public readonly float Angle;
        public readonly bool Blocked;

        // =========================================================
        // Store one immutable terrain classification.
        public SlopeSample(Vector2 gradient, float angle, bool blocked)
        {
            Gradient = gradient;
            Angle = angle;
            Blocked = blocked;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 65536;
    private readonly Dictionary<Vector2I, SlopeSample> _cache = new();
    private readonly Queue<Vector2I> _order = new();

    private TerrainElevation _elevation;
    private ChunkController _chunks;
    private Node2D _ground;
    private bool _enabled;
    private float _heightScale;

    public float MaximumAngle { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register this scene-owned service.
    public override void _EnterTree()
    {
        AddToGroup("terrain_slopes");
    }

    // =========================================================
    // Snapshot global rules before generating cached terrain.
    public override void _Ready()
    {
        _elevation = GetParent<TerrainElevation>();
        _chunks = _elevation.GetNode<ChunkController>(
            "../ChunkController");
        _ground = _elevation.GetNode<Node2D>("../../GroundChunks");

        WorldConfig config = WorldConfig.TryFind(this);
        _enabled = config?.TerrainSlopesEnabled ?? true;
        MaximumAngle = config?.MaxWalkableSlopeAngle
            ?? WorldConfig.DefaultSlopeAngle;
        _heightScale = config?.SlopeHeightScale
            ?? WorldConfig.DefaultSlopeHeightScale;

        if (!float.IsFinite(MaximumAngle) ||
            MaximumAngle <= 0f || MaximumAngle >= 90f)
            throw new InvalidOperationException(
                "MaxWalkableSlopeAngle must be between 0 and 90.");

        if (!float.IsFinite(_heightScale) || _heightScale <= 0f)
            throw new InvalidOperationException(
                "SlopeHeightScale must be finite and positive.");

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Reuse the world's service or attach it to TerrainElevation.
    public static TerrainSlopeWorld Ensure(Node context)
    {
        TerrainSlopeWorld existing = context.GetTree()
            .GetFirstNodeInGroup("terrain_slopes") as TerrainSlopeWorld;
        if (existing != null) return existing;

        TerrainElevation elevation = context.GetTree()
            .GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

        if (elevation == null)
            throw new InvalidOperationException(
                "Terrain slopes require TerrainElevation.");

        TerrainSlopeWorld service = new() { Name = "Slopes" };
        elevation.AddChild(service);
        return service;
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Match half-tile cells to the existing tile boundaries at minus 0.5.
    public static Vector2I CellAt(Vector2 tile)
    {
        return new Vector2I(
            Mathf.FloorToInt((tile.X + 0.5f) * 2f),
            Mathf.FloorToInt((tile.Y + 0.5f) * 2f));
    }

    // =========================================================
    // Return the absolute tile position at a half-tile cell centre.
    public static Vector2 CellCentre(Vector2I cell)
    {
        return new Vector2(
            cell.X * 0.5f - 0.25f,
            cell.Y * 0.5f - 0.25f);
    }

    // =========================================================
    // Convert logical world positions without including artwork height offsets.
    private Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(point), _chunks.TileSize);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Read the classification used by collision beneath a world position.
    public SlopeSample AtWorld(Vector2 point)
    {
        return SampleCell(CellAt(WorldToTile(point)));
    }

    // =========================================================
    // Reuse bounded cached slope classifications during generation and gameplay.
    public SlopeSample SampleCell(Vector2I cell)
    {
        if (_cache.TryGetValue(cell, out SlopeSample sample))
            return sample;

        Vector2 tile = CellCentre(cell);
        Vector2 gradient = GetGradient(tile);
        float angle = Mathf.RadToDeg(
            Mathf.Atan(gradient.Length() * _heightScale));

        sample = new SlopeSample(
            gradient, angle, _enabled && angle >= MaximumAngle);

        while (_cache.Count >= CacheLimit)
            _cache.Remove(_order.Dequeue());

        _cache.Add(cell, sample);
        _order.Enqueue(cell);
        return sample;
    }

    // =========================================================
    // Measure height change along both isometric axes over half a tile.
    private Vector2 GetGradient(Vector2 tile)
    {
        float alongX =
            RenderedHeight(tile + new Vector2(0.25f, 0f)) -
            RenderedHeight(tile - new Vector2(0.25f, 0f));
        float alongY =
            RenderedHeight(tile + new Vector2(0f, 0.25f)) -
            RenderedHeight(tile - new Vector2(0f, 0.25f));

        return new Vector2(
            (alongX - alongY) /
                Mathf.Max(1f, _chunks.TileSize.X * 0.5f),
            (alongX + alongY) /
                Mathf.Max(1f, _chunks.TileSize.Y * 0.5f));
    }

    // =========================================================
    // Sample the same interpolated triangle surface used by moving artwork.
    private float RenderedHeight(Vector2 tile)
    {
        Vector2 point = _ground.ToGlobal(
            IsoGrid.TileToWorld(tile, _chunks.TileSize));
        return _elevation.SampleWorldHeight(point);
    }
    #endregion

    #region Clearance
    // =========================================================
    // Reject blocked cells touched by a logical circular agent footprint.
    public bool HasClearance(Vector2 point, float radius = 0f)
    {
        Vector2 tile = WorldToTile(point);
        Vector2I centre = CellAt(tile);

        if (SampleCell(centre).Blocked) return false;
        if (radius <= 0f) return true;

        Vector2 dx = WorldToTile(point + Vector2.Right) - tile;
        Vector2 dy = WorldToTile(point + Vector2.Down) - tile;
        Vector2 extent = new(
            Mathf.Sqrt(dx.X * dx.X + dy.X * dy.X) * radius,
            Mathf.Sqrt(dx.Y * dx.Y + dy.Y * dy.Y) * radius);

        Vector2I first = CellAt(tile - extent);
        Vector2I last = CellAt(tile + extent);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            if (!SampleCell(cell).Blocked) continue;
            if (TouchesCell(point, radius, cell)) return false;
        }

        return true;
    }

    // =========================================================
    // Check circle-to-cell edges without allocating polygons for each query.
    private bool TouchesCell(Vector2 point, float radius, Vector2I cell)
    {
        Vector2 low = new(
            cell.X * 0.5f - 0.5f,
            cell.Y * 0.5f - 0.5f);

        Vector2 a = GroundPoint(low);
        Vector2 b = GroundPoint(low + new Vector2(0.5f, 0f));
        Vector2 c = GroundPoint(low + new Vector2(0.5f, 0.5f));
        Vector2 d = GroundPoint(low + new Vector2(0f, 0.5f));
        float squared = radius * radius;

        return SegmentDistanceSquared(point, a, b) <= squared ||
            SegmentDistanceSquared(point, b, c) <= squared ||
            SegmentDistanceSquared(point, c, d) <= squared ||
            SegmentDistanceSquared(point, d, a) <= squared;
    }

    // =========================================================
    // Convert a tile corner into its logical collision position.
    private Vector2 GroundPoint(Vector2 tile)
    {
        return _ground.ToGlobal(
            IsoGrid.TileToWorld(tile, _chunks.TileSize));
    }

    // =========================================================
    // Measure squared point-to-segment distance without temporary resources.
    private static float SegmentDistanceSquared(
        Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        float t = Mathf.Clamp(
            (point - a).Dot(edge) /
            Mathf.Max(0.000001f, edge.LengthSquared()), 0f, 1f);

        return point.DistanceSquaredTo(a + edge * t);
    }
    #endregion
}