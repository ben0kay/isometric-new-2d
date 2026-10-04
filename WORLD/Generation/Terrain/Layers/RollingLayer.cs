// Samples smooth seeded rolling terrain using absolute tile coordinates.
// Stateless coordinate sampling keeps neighbouring chunk edges consistent.
using Godot;

public static class RollingLayer
{
    #region Sampling
    // =========================================================
    // Generate broad rolling ground without fine bumps on every tile.
    public static float Sample(
        Vector2 tile, uint seed, float height, float featureSize)
    {
        return Noise(tile / Mathf.Max(4f, featureSize), seed)
            * Mathf.Max(0f, height);
    }

    // =========================================================
    // Smoothly interpolate deterministic lattice values without allocations.
    private static float Noise(Vector2 point, uint seed)
    {
        int x = Mathf.FloorToInt(point.X);
        int y = Mathf.FloorToInt(point.Y);
        float u = point.X - x, v = point.Y - y;
        u = u * u * (3f - 2f * u);
        v = v * v * (3f - 2f * v);

        float a = (IsoGrid.Hash(x, y, seed) & 65535u) / 65535f;
        float b = (IsoGrid.Hash(x + 1, y, seed) & 65535u) / 65535f;
        float c = (IsoGrid.Hash(x, y + 1, seed) & 65535u) / 65535f;
        float d = (IsoGrid.Hash(x + 1, y + 1, seed) & 65535u) / 65535f;
        return Mathf.Lerp(
            Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
    #endregion
}