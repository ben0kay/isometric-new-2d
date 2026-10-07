// Samples connected seeded chambers and registered entrance tunnels.
// Every hole connects to its assigned chamber in the same cave coordinate system.
using Godot;
using System.Collections.Generic;

public sealed class CaveGenerator
{
    #region State
    public CaveGenerationSettings Settings { get; }
    public float HubX { get; }

    private readonly uint _seed;
    private readonly float _baseHeight;
        private readonly CaveWorld _world;

    private readonly struct Room
    {
        public readonly Vector2 Centre, Radius;
        public Room(Vector2 centre, Vector2 radius)
        {
            Centre = centre;
            Radius = radius;
        }
    }
    #endregion

    #region Construction
    // =========================================================
    // Use one shared entrance planner and an unbounded chamber coordinate system.
    public CaveGenerator(
        CaveGenerationSettings settings, uint worldSeed,
        CaveWorld world, float baseHeight)
    {
        settings.Validate();
        Settings = settings;
        _seed = worldSeed ^ settings.SeedOffset;
        _world = world;
        _baseHeight = baseHeight;
        HubX = settings.EntranceTunnelLengthTiles + 8f;
    }
    #endregion

    #region Floor Sampling
    // =========================================================
    // Connect every chamber row, with recurring vertical spines joining all rows.
    public bool IsFloor(Vector2I tile)
    {
        Vector2 point = new(tile.X, tile.Y);

        foreach (CaveHole hole in _world.NearbyHoles(point))
        {
            Vector2 local = hole.Coordinates(point);

            if (local.X >= -2f && local.X <= hole.TunnelLength &&
                Mathf.Abs(local.Y) <= 3f)
                return local.X >= 0f && Mathf.Abs(local.Y) <= 1f;
        }

        foreach (CaveHole hole in _world.NearbyHoles(point))
        {
            Vector2 end = hole.TileAt(hole.TunnelLength);
            Vector2 room = GetRoom(
                hole.AnchorCell.X, hole.AnchorCell.Y).Centre;

            if (InConnection(point, end, room, true))
                return true;
        }

        int spacing = Settings.CellSpacingTiles;
        int cx = Mathf.FloorToInt((point.X - HubX) / spacing + 0.5f);
        int cy = Mathf.FloorToInt(point.Y / spacing + 0.5f);

        for (int x = cx - 1; x <= cx + 1; x++)
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            Room room = GetRoom(x, y);
            Vector2 normalized = (point - room.Centre) / room.Radius;

            if (normalized.LengthSquared() <= 1f)
                return true;

            if (InConnection(
                point, room.Centre, GetRoom(x - 1, y).Centre,
                Random(x, y, 5) < 0.5f))
                return true;

            bool vertical = x % 4 == 0 ||
                Random(x, y, 6) < Settings.ExtraConnectionChance;

            if (vertical && InConnection(
                point, room.Centre, GetRoom(x, y - 1).Centre,
                Random(x, y, 7) < 0.5f))
                return true;
        }

        return false;
    }

    // =========================================================
    // Leave the outside edge of nearby entrance mouths physically open.
    public bool IsMouthEdge(Vector2I tile, Vector2I neighbour)
    {
        Vector2 a = new(tile.X, tile.Y);
        Vector2 b = new(neighbour.X, neighbour.Y);

        foreach (CaveHole hole in _world.NearbyHoles(a))
        {
            Vector2 localA = hole.Coordinates(a);
            Vector2 localB = hole.Coordinates(b);

            if (Mathf.Abs(localA.X) < 0.01f &&
                Mathf.Abs(localA.Y) <= 1f &&
                localB.X < 0f)
                return true;
        }

        return false;
    }
    #endregion

    #region Heights
    // =========================================================
    // Join each entrance's surface elevation to the shared underground floor.
    public float VertexHeight(Vector2 tile)
    {
        foreach (CaveHole hole in _world.NearbyHoles(tile))
        {
            Vector2 local = hole.Coordinates(tile);
            if (local.X < -2f || local.X > hole.TunnelLength ||
                Mathf.Abs(local.Y) > 2.5f)
                continue;

            float t = Mathf.Clamp(local.X / hole.TunnelLength, 0f, 1f);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(hole.RimHeight, _baseHeight, t);
        }

        return _baseHeight;
    }

    // =========================================================
    // Match the two floor triangles, including ramps along either tile axis.
    public float HeightAt(Vector2 tile, float unusedRimHeight)
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

    #region Layout Helpers


    // =========================================================
    // Keep connecting routes fixed while varying chamber dimensions by seed.
    private Room GetRoom(int x, int y)
    {
        float spacing = Settings.CellSpacingTiles;
        Vector2 centre = new(HubX + x * spacing, y * spacing);
        Vector2 range = Settings.ChamberRadiusRange;

        return new Room(centre, new Vector2(
            Mathf.Lerp(range.X, range.Y, Random(x, y, 3)),
            Mathf.Lerp(range.X, range.Y, Random(x, y, 4))));
    }

    // =========================================================
    // Connect room centres with a traversable bent passage.
    private bool InConnection(
        Vector2 point, Vector2 a, Vector2 b, bool horizontalFirst)
    {
        Vector2 bend = horizontalFirst
            ? new Vector2(b.X, a.Y) : new Vector2(a.X, b.Y);

        float radius = Settings.TunnelWidthTiles * 0.5f;
        return NearSegment(point, a, bend, radius) ||
            NearSegment(point, bend, b, radius);
    }

    // =========================================================
    // Measure passage distance, including rounded endpoints.
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
    // Produce repeatable coordinate randomness without RNG allocations.
    private float Random(int x, int y, uint salt)
    {
        unchecked
        {
            uint value = (uint)x * 0x8DA6B343u ^
                (uint)y * 0xD8163841u ^ _seed ^ salt;
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            value ^= value >> 16;
            return (value & 0xFFFFFFu) / 16777216f;
        }
    }
    #endregion
}