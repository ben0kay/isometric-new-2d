// Defines shared ground/void terrain for rendering, collision and navigation.
// Coordinates describe tile centres; movement remains on the logical ground plane.
using Godot;

public static class TerrainLayout
{
    #region Configuration
    public static readonly Vector2I ChasmStart = new(4, -4);
    public static readonly Vector2I ChasmSize = new(8, 6);
    public const float CliffDepth = 128f;
    public const uint CollisionLayer = 8u; // Inspector layer 4.
    #endregion

    #region Terrain Queries
    // =========================================================
    // Report whether a tile belongs to the test chasm.
    public static bool IsVoidTile(int x, int y)
    {
        return x >= ChasmStart.X && y >= ChasmStart.Y &&
            x < ChasmStart.X + ChasmSize.X &&
            y < ChasmStart.Y + ChasmSize.Y;
    }

    // =========================================================
    // Return the chasm's continuous tile bounds, including tile edges.
    public static Rect2 GetTileBounds()
    {
        return new Rect2(
            new Vector2(ChasmStart.X - 0.5f, ChasmStart.Y - 0.5f),
            new Vector2(ChasmSize.X, ChasmSize.Y)
        );
    }

    // =========================================================
    // Keep a circular footprint outside the chasm in ground-local coordinates.
    public static bool HasGroundClearance(Vector2 point, Vector2 tileSize, float radius = 0f)
    {
        Rect2 bounds = GetTileBounds();
        Vector2 tile = IsoGrid.WorldToTile(point, tileSize);
        if (bounds.HasPoint(tile)) return false;
        if (radius <= 0f) return true;

        Vector2 a = IsoGrid.TileToWorld(bounds.Position, tileSize);
        Vector2 b = IsoGrid.TileToWorld(new(bounds.End.X, bounds.Position.Y), tileSize);
        Vector2 c = IsoGrid.TileToWorld(bounds.End, tileSize);
        Vector2 d = IsoGrid.TileToWorld(new(bounds.Position.X, bounds.End.Y), tileSize);
        float squared = radius * radius;

        return DistanceToSegmentSquared(point, a, b) > squared &&
            DistanceToSegmentSquared(point, b, c) > squared &&
            DistanceToSegmentSquared(point, c, d) > squared &&
            DistanceToSegmentSquared(point, d, a) > squared;
    }

    // =========================================================
    // Measure distance to an edge without allocating temporary geometry.
    private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
    {
        Vector2 edge = b - a;
        float fraction = Mathf.Clamp((point - a).Dot(edge) / edge.LengthSquared(), 0f, 1f);
        return point.DistanceSquaredTo(a + edge * fraction);
    }
    #endregion
}