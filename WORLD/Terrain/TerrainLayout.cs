// Generates a cached winding ravine shared by terrain, collision and navigation.
// The void follows tile edges, with bends and varying width across its length.
using Godot;
using System.Collections.Generic;

public static class TerrainLayout
{
    #region Configuration
    public static readonly Vector2I RavineStart = new(4, -4);
    public const int RavineLength = 28;
    public const float BendAmount = 3f;
    public const float WidthVariation = 1.8f;
    public const float CliffDepth = 128f;
    public const uint CollisionLayer = 8u; // Inspector layer 4.
    #endregion

    #region State
    private static readonly HashSet<Vector2I> VoidTiles = BuildVoidTiles();
    #endregion

    #region Generation
    // =========================================================
    // Generate the ravine once with broad bends, smaller deviations and tapered width.
    private static HashSet<Vector2I> BuildVoidTiles()
    {
        HashSet<Vector2I> tiles = new();
        int length = Mathf.Max(2, RavineLength);

        for (int i = 0; i < length; i++)
        {
            float t = i / (float)(length - 1);
            float curve =
                Mathf.Sin(t * Mathf.Tau * 0.8f) * BendAmount +
                Mathf.Sin(t * Mathf.Tau * 2.1f);

            int centre = Mathf.FloorToInt(RavineStart.Y + curve + 0.5f);
            int halfWidth = Mathf.Max(1,
                Mathf.FloorToInt(1f + Mathf.Sin(t * Mathf.Pi) * WidthVariation));

            for (int y = centre - halfWidth; y <= centre + halfWidth; y++)
                tiles.Add(new Vector2I(RavineStart.X + i, y));
        }
        return tiles;
    }
    #endregion

    #region Terrain Queries
    // =========================================================
    // Read cached terrain classification without regenerating the ravine.
    public static bool IsVoidTile(int x, int y)
    {
        return VoidTiles.Contains(new Vector2I(x, y));
    }

    // =========================================================
    // Check nearby void tiles against a circular logical footprint.
    public static bool HasGroundClearance(Vector2 point, Vector2 tileSize, float radius = 0f)
    {
        Vector2 tile = IsoGrid.WorldToTile(point, tileSize);
        int centreX = Mathf.FloorToInt(tile.X + 0.5f);
        int centreY = Mathf.FloorToInt(tile.Y + 0.5f);
        if (IsVoidTile(centreX, centreY)) return false;
        if (radius <= 0f) return true;

        // Both inverse tile axes have this world-space gradient length.
        float gradient = Mathf.Sqrt(
            1f / (tileSize.X * tileSize.X) +
            1f / (tileSize.Y * tileSize.Y));
        int reach = Mathf.CeilToInt(radius * gradient) + 1;
        float squared = radius * radius;

        for (int y = centreY - reach; y <= centreY + reach; y++)
        for (int x = centreX - reach; x <= centreX + reach; x++)
        {
            if (!IsVoidTile(x, y)) continue;

            Vector2 a = IsoGrid.TileToWorld(new(x - 0.5f, y - 0.5f), tileSize);
            Vector2 b = IsoGrid.TileToWorld(new(x + 0.5f, y - 0.5f), tileSize);
            Vector2 c = IsoGrid.TileToWorld(new(x + 0.5f, y + 0.5f), tileSize);
            Vector2 d = IsoGrid.TileToWorld(new(x - 0.5f, y + 0.5f), tileSize);

            if (DistanceToSegmentSquared(point, a, b) <= squared ||
                DistanceToSegmentSquared(point, b, c) <= squared ||
                DistanceToSegmentSquared(point, c, d) <= squared ||
                DistanceToSegmentSquared(point, d, a) <= squared)
                return false;
        }
        return true;
    }

    // =========================================================
    // Measure footprint clearance from a tile edge without allocations.
    private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        float fraction = Mathf.Clamp((point - a).Dot(edge) / edge.LengthSquared(), 0f, 1f);
        return point.DistanceSquaredTo(a + edge * fraction);
    }
    #endregion
}