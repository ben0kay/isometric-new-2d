// Shares irregular patch boundaries and conservative terrain-placement checks.
using Godot;

public static class SurfaceGeometry
{
    #region Shape
    // =========================================================
    // Keep gameplay boundaries consistent with the shader's angular outline.
    public static float Edge(float angle, float phase)
    {
        return 1f +
            Mathf.Sin(angle * 3f + phase) * 0.06f +
            Mathf.Sin(angle * 5f - phase * 1.7f) * 0.04f;
    }

    // =========================================================
    // Fade effects through the shoreline instead of switching abruptly.
    public static float Influence(
        Vector2 difference, Vector2 radius, float rotation, float phase)
    {
        Vector2 rotated = difference.Rotated(-rotation);
        Vector2 normalized = new(rotated.X / radius.X, rotated.Y / radius.Y);
        float edge = Edge(normalized.Angle(), phase);
        return Mathf.Clamp((edge - normalized.Length()) / 0.18f, 0f, 1f);
    }
    #endregion

    #region Placement
    // =========================================================
    // Preserve the existing half-tile sampling and reject chasms or excessive slope.
    public static bool IsFlat(
        TerrainElevation elevation, Vector2 centre, float radius, float tolerance)
    {
        int left = Mathf.FloorToInt((centre.X - radius) * 2f);
        int right = Mathf.CeilToInt((centre.X + radius) * 2f);
        int top = Mathf.FloorToInt((centre.Y - radius) * 2f);
        int bottom = Mathf.CeilToInt((centre.Y + radius) * 2f);
        float minimum = float.PositiveInfinity;
        float maximum = float.NegativeInfinity;

        for (int y = top; y <= bottom; y++)
        for (int x = left; x <= right; x++)
        {
            Vector2 tile = new(x * 0.5f, y * 0.5f);
            int tx = Mathf.FloorToInt(tile.X + 0.5f);
            int ty = Mathf.FloorToInt(tile.Y + 0.5f);
            if (ChasmFeature.IsVoidTile(tx, ty)) return false;

            float height = elevation.GetHeight(tile);
            minimum = Mathf.Min(minimum, height);
            maximum = Mathf.Max(maximum, height);
            if (maximum - minimum > tolerance) return false;
        }
        return true;
    }
    #endregion
}