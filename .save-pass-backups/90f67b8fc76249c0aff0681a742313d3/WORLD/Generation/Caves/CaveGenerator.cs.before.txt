// Samples connected biome-shaped chambers and winding passages.
// Caches reusable layout geometry while preserving registered entrance ramps.
using Godot;
using System.Collections.Generic;

public sealed class CaveGenerator
{
#region State
public CaveGenerationSettings Settings { get; }
public CaveBiomeWorld Biomes { get; }
public float HubX { get; }

private const int CacheLimit = 1024;
private const int PassageSegments = 12;

private readonly uint _seed;
private readonly float _baseHeight;
private readonly float _entranceRadius;
private readonly CaveWorld _world;

private readonly Dictionary<Vector2I, Room> _rooms = new();
private readonly Queue<Vector2I> _roomOrder = new();

private sealed class Passage
{
    public Vector2[] Points;
    public float[] Radii;
    public Rect2 Bounds;
}

private sealed class Room
{
    public Vector2 Centre;
    public CaveBiomeWorld.Sample Profile;
    public Passage Horizontal, Vertical;
}
#endregion

    #region Construction
// =========================================================
// Apply global underground biome scale without modifying the shared resource.
public CaveGenerator(
    CaveGenerationSettings settings, uint worldSeed,
    CaveWorld world, float baseHeight)
{
    settings.Validate();

    float multiplier = WorldConfig.Find(world).CaveBiomeScaleMultiplier;
    float biomeSize = settings.BiomeSizeTiles * multiplier;

    if (!float.IsFinite(multiplier) || multiplier <= 0f ||
        !float.IsFinite(biomeSize) || biomeSize <= 0f)
    {
        throw new System.InvalidOperationException(
            "CaveBiomeScaleMultiplier must produce a finite, positive biome size.");
    }

    Settings = (CaveGenerationSettings)settings.Duplicate();
    Settings.BiomeSizeTiles = biomeSize;

    _seed = worldSeed ^ Settings.SeedOffset;
    _world = world;
    _baseHeight = baseHeight;
    _entranceRadius = Settings.MaximumTunnelWidth() * 0.5f;
    HubX = Settings.EntranceTunnelLengthTiles + 8f;
    Biomes = new CaveBiomeWorld(Settings, _seed);
}
    #endregion

    #region Floor Sampling
// =========================================================
// Preserve entrance ramps, then sample biome-shaped rooms and connected routes.
public bool IsFloor(Vector2I tile)
{
    Vector2 point = new(tile.X, tile.Y);

    foreach (WorldLayerConnection departure in _world.Departures)
    {
        Vector2 local = departure.Coordinates(point);
        if (local.X >= -2f && local.X <= 1f && Mathf.Abs(local.Y) <= 3f)
            return local.X <= 0f && Mathf.Abs(local.Y) <= 1f;
        if (NearSegment(point, departure.UpperAnchor, departure.TileAt(-1f), 2f))
            return true;
    }

    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
    {
        Vector2 local = hole.Coordinates(point);

        if (local.X >= -2f && local.X <= hole.TunnelLength &&
            Mathf.Abs(local.Y) <= 3f)
        {
            return local.X >= 0f && Mathf.Abs(local.Y) <= 1f;
        }
    }

    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
    {
        Vector2 end = hole.TileAt(hole.TunnelLength);
        Vector2 room = Centre(hole.AnchorCell.X, hole.AnchorCell.Y);

        if (point.DistanceSquaredTo(end) <= 9f ||
            NearSegment(point, end, room, _entranceRadius))
            return true;
    }

    float spacing = Settings.CellSpacingTiles;
    int cx = Mathf.FloorToInt((point.X - HubX) / spacing + 0.5f);
    int cy = Mathf.FloorToInt(point.Y / spacing + 0.5f);

    for (int x = cx - 1; x <= cx + 1; x++)
    for (int y = cy - 1; y <= cy + 1; y++)
    {
        Room room = GetRoom(x, y);

        if (room.Profile.ChamberDistance(
            point, room.Centre, x, y) <= 0f ||
            InPassage(point, room.Horizontal) ||
            InPassage(point, room.Vertical))
            return true;
    }

    return false;
}

    // =========================================================
    // Keep the outside edge of each registered entrance physically open.
    public bool IsMouthEdge(Vector2I tile, Vector2I neighbour)
    {
        Vector2 a = new(tile.X, tile.Y);
        Vector2 b = new(neighbour.X, neighbour.Y);

        foreach (WorldLayerConnection departure in _world.Departures)
        {
            Vector2 localA = departure.Coordinates(a);
            Vector2 localB = departure.Coordinates(b);
            if (localA.X <= 0f && localA.X > -1.01f && Mathf.Abs(localA.Y) <= 1f &&
                localB.X > 0f) return true;
        }

        foreach (WorldLayerConnection hole in _world.NearbyConnections(a))
        {
            Vector2 localA = hole.Coordinates(a);
            Vector2 localB = hole.Coordinates(b);

            if (localA.X >= 0f && localA.X < 1.01f &&
                Mathf.Abs(localA.Y) <= 1f && localB.X < 0f)
                return true;
        }

        return false;
    }
    #endregion

    #region Heights
    // =========================================================
    // Join entrance elevation to the local underground floor.
    public float VertexHeight(Vector2 tile)
    {
        float floor = _baseHeight + Biomes.At(tile).FloorHeight(tile);

        foreach (WorldLayerConnection departure in _world.Departures)
        {
            Vector2 local = departure.Coordinates(tile);
            if (local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) <= 2.5f)
                return Mathf.Lerp(floor, departure.RimHeight,
                    Mathf.Clamp((local.X + 2f) / 2f, 0f, 1f));
        }

        foreach (WorldLayerConnection hole in _world.NearbyConnections(tile))
        {
            Vector2 local = hole.Coordinates(tile);
            if (local.X < -2f || local.X > hole.TunnelLength ||
                Mathf.Abs(local.Y) > 2.5f)
                continue;

            float t = Mathf.Clamp(local.X / hole.TunnelLength, 0f, 1f);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(hole.RimHeight, floor, t);
        }

        return floor;
    }

    // =========================================================
    // Match the rendered floor triangles when sampling actor elevation.
    public float HeightAt(Vector2 tile)
    {
        Vector2 centre = new(
            Mathf.Floor(tile.X + 0.5f),
            Mathf.Floor(tile.Y + 0.5f));

        float u = tile.X - centre.X + 0.5f;
        float v = tile.Y - centre.Y + 0.5f;

        float a = VertexHeight(centre + new Vector2(-0.5f, -0.5f));
        float b = VertexHeight(centre + new Vector2(0.5f, -0.5f));
        float c = VertexHeight(centre + new Vector2(0.5f, 0.5f));
        float d = VertexHeight(centre + new Vector2(-0.5f, 0.5f));

        return v <= u
            ? a * (1f - u) + b * (u - v) + c * v
            : a * (1f - v) + c * u + d * (v - u);
    }
    #endregion

    #region Connection Anchors
    // =========================================================
    // Expose the shared chamber lattice for reserved connection approaches.
    public Vector2 RoomCentre(Vector2I cell) { return Centre(cell.X, cell.Y); }

    // =========================================================
    // Select a nearby normal chamber for the lower landing connector.
    public Vector2I RoomCell(Vector2 point)
    {
        return new Vector2I(Mathf.RoundToInt((point.X - HubX) / Settings.CellSpacingTiles),
            Mathf.RoundToInt(point.Y / Settings.CellSpacingTiles));
    }
    #endregion

    #region Cached Layout
    // =========================================================
    // Preserve shared room anchors so every biome and entrance can connect.
    private Vector2 Centre(int x, int y)
    {
        return new Vector2(
            HubX + x * Settings.CellSpacingTiles,
            y * Settings.CellSpacingTiles);
    }

    // =========================================================
    // Cache room profiles and curved paths without retaining unlimited world data.
    private Room GetRoom(int x, int y)
    {
        Vector2I key = new(x, y);
        if (_rooms.TryGetValue(key, out Room existing)) return existing;

        Vector2 centre = Centre(x, y);
        CaveBiomeWorld.Sample profile = Biomes.At(centre);

        Room room = new()
        {
            Centre = centre,
            Profile = profile,
            Horizontal = BuildPassage(
                centre, Centre(x - 1, y),
                IsoGrid.Hash(x, y, _seed ^ 5u))
        };

        float chance = profile.ExtraConnections;
        bool vertical = x % 4 == 0 ||
            Random(x, y, 6) < chance;

        if (vertical)
        {
            room.Vertical = BuildPassage(
                centre, Centre(x, y - 1),
                IsoGrid.Hash(x, y, _seed ^ 7u));
        }

        if (_rooms.Count >= CacheLimit)
            _rooms.Remove(_roomOrder.Dequeue());

        _rooms.Add(key, room);
        _roomOrder.Enqueue(key);
        return room;
    }

    // =========================================================
    // Build one curved route, blending biome width and bend along its length.
    private Passage BuildPassage(Vector2 a, Vector2 b, uint seed)
    {
        Passage passage = new()
        {
            Points = new Vector2[PassageSegments + 1],
            Radii = new float[PassageSegments + 1]
        };

        Vector2 normal = (b - a).Normalized().Orthogonal();
        Vector2 minimum = a, maximum = a;
        float maximumRadius = 0f;

        for (int i = 0; i <= PassageSegments; i++)
        {
            float t = (float)i / PassageSegments;
            Vector2 basePoint = a.Lerp(b, t);
            CaveBiomeWorld.Sample profile = Biomes.At(basePoint);

            Vector2 point = basePoint +
                normal * profile.PassageOffset(t, seed);

            float radius = profile.TunnelWidth * 0.5f;
            passage.Points[i] = point;
            passage.Radii[i] = radius;
            maximumRadius = Mathf.Max(maximumRadius, radius);

            minimum = new Vector2(
                Mathf.Min(minimum.X, point.X),
                Mathf.Min(minimum.Y, point.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, point.X),
                Mathf.Max(maximum.Y, point.Y));
        }

        passage.Bounds = new Rect2(minimum, maximum - minimum)
            .Grow(maximumRadius + 0.01f);
        return passage;
    }

    // =========================================================
    // Reject distant routes before testing their short connected segments.
    private static bool InPassage(Vector2 point, Passage passage)
    {
        if (passage == null || !passage.Bounds.HasPoint(point))
            return false;

        for (int i = 0; i < passage.Points.Length - 1; i++)
        {
            float radius = Mathf.Max(
                passage.Radii[i], passage.Radii[i + 1]);

            if (NearSegment(
                point, passage.Points[i], passage.Points[i + 1], radius))
                return true;
        }

        return false;
    }

    // =========================================================
    // Include rounded segment ends so adjacent route sections remain connected.
    private static bool NearSegment(
        Vector2 point, Vector2 a, Vector2 b, float radius)
    {
        Vector2 line = b - a;
        float length = line.LengthSquared();
        float t = length > 0.0001f
            ? Mathf.Clamp((point - a).Dot(line) / length, 0f, 1f) : 0f;

        return point.DistanceSquaredTo(a + line * t) <= radius * radius;
    }

    // =========================================================
    // Choose optional links consistently without random-generator allocations.
    private float Random(int x, int y, uint salt)
    {
        uint hash = IsoGrid.Hash(x, y, _seed ^ salt);
        return (hash & 0xFFFFFFu) / 16777216f;
    }
    #endregion
}
