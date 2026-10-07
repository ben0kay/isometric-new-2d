// Samples a deterministic network of chambers and connecting tunnels.
// Mandatory connections guarantee a route back to the entrance.
using Godot;

public sealed class CaveGenerator
{
    #region State
    public CaveGenerationSettings Settings { get; }
    public float HubX { get; }
    private readonly uint _seed;

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
    // Use a separate seed stream without changing surface generation.
    public CaveGenerator(CaveGenerationSettings settings, uint worldSeed)
    {
        settings.Validate();
        Settings = settings;
        _seed = worldSeed ^ settings.SeedOffset;
        HubX = settings.EntranceTunnelLengthTiles + 8f;
    }
    #endregion

    #region Sampling
    // =========================================================
    // Classify one absolute cave tile using nearby rooms and their connections.
    public bool IsFloor(Vector2I tile)
    {
        if (tile.X < 0) return false;

        if (tile.X <= HubX && Mathf.Abs(tile.Y) <= 1)
            return true;

        Vector2 point = new(tile.X, tile.Y);
        int spacing = Settings.CellSpacingTiles;
        int cx = Mathf.FloorToInt((point.X - HubX) / spacing + 0.5f);
        int cy = Mathf.FloorToInt(point.Y / spacing + 0.5f);

        for (int x = cx - 1; x <= cx + 1; x++)
        for (int y = cy - 1; y <= cy + 1; y++)
        {
            if (!ValidCell(x, y)) continue;
            Room room = GetRoom(x, y);
            Vector2 normalized = (point - room.Centre) / room.Radius;

            if (normalized.LengthSquared() <= 1f)
                return true;

            if (x > 0 &&
                InConnection(point, room.Centre,
                    GetRoom(x - 1, y).Centre, Random(x, y, 5) < 0.5f))
                return true;

            bool vertical = y > -Settings.CellsEitherSide &&
                (x == 0 || Random(x, y, 6) < Settings.ExtraConnectionChance);

            if (vertical &&
                InConnection(point, room.Centre,
                    GetRoom(x, y - 1).Centre, Random(x, y, 7) < 0.5f))
                return true;
        }

        return false;
    }

    // =========================================================
    // Sample tunnel descent at mesh vertices; underground chambers stay level.
    public float VertexHeight(float x, float rimHeight)
    {
        float t = Mathf.Clamp(
            x / Settings.EntranceTunnelLengthTiles, 0f, 1f);
        t = t * t * (3f - 2f * t);
        return rimHeight - Settings.DepthPixels * t;
    }

    // =========================================================
    // Match interpolation to the floor mesh across the entrance ramp.
    public float HeightAt(Vector2 tile, float rimHeight)
    {
        float centre = Mathf.Floor(tile.X + 0.5f);
        float fraction = tile.X - centre + 0.5f;
        return Mathf.Lerp(
            VertexHeight(centre - 0.5f, rimHeight),
            VertexHeight(centre + 0.5f, rimHeight), fraction);
    }
    #endregion

    #region Layout Helpers
    // =========================================================
    // Keep the generated network finite while allowing its size to be adjusted.
    private bool ValidCell(int x, int y)
    {
        return x >= 0 && x < Settings.CellsAcross &&
            y >= -Settings.CellsEitherSide &&
            y <= Settings.CellsEitherSide;
    }

    // =========================================================
    // Jitter chamber positions and radii without depending on generation order.
    private Room GetRoom(int x, int y)
    {
        float spacing = Settings.CellSpacingTiles;
        Vector2 centre = new(HubX + x * spacing, y * spacing);

        if (x != 0 || y != 0)
            centre += new Vector2(
                Random(x, y, 1) - 0.5f,
                Random(x, y, 2) - 0.5f) * spacing * 0.375f;

        Vector2 range = Settings.ChamberRadiusRange;
        return new Room(centre, new Vector2(
            Mathf.Lerp(range.X, range.Y, Random(x, y, 3)),
            Mathf.Lerp(range.X, range.Y, Random(x, y, 4))));
    }

    // =========================================================
    // Join chambers with a bent passage rather than disconnected room placement.
    private bool InConnection(Vector2 point, Vector2 a, Vector2 b, bool horizontalFirst)
    {
        Vector2 bend = horizontalFirst
            ? new Vector2(b.X, a.Y) : new Vector2(a.X, b.Y);

        float radius = Settings.TunnelWidthTiles * 0.5f;
        return NearSegment(point, a, bend, radius) ||
            NearSegment(point, bend, b, radius);
    }

    // =========================================================
    // Measure distance to a passage segment, including its rounded endpoints.
    private static bool NearSegment(Vector2 point, Vector2 a, Vector2 b, float radius)
    {
        Vector2 line = b - a;
        float length = line.LengthSquared();
        float t = length > 0.0001f
            ? Mathf.Clamp((point - a).Dot(line) / length, 0f, 1f) : 0f;
        return point.DistanceSquaredTo(a + line * t) <= radius * radius;
    }

    // =========================================================
    // Produce repeatable coordinate randomness without allocating RNG instances.
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