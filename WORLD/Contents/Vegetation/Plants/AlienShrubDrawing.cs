// Draws low spreading alien shrub clusters using small leaves and dark branching stems.
// Shares vegetation palettes while keeping its own shape-generation code.
using Godot;

public static class AlienShrubDrawing
{
    #region Drawing
    // =========================================================
    // Build a low irregular cluster with several spreading leafy branches.
    public static void Draw(Node2D painter, int variant)
    {
        int shape = variant % VegetationPalette.ShapeCount;
        int palette = variant / VegetationPalette.ShapeCount;
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(33179 + shape * 1237);

        for (int branch = 0; branch < 11; branch++)
        {
            float spread = (branch - 5) / 5f;
            Vector2 root = new(rng.RandfRange(-9, 9), rng.RandfRange(-4, 1));
            Vector2 tip = new(
                spread * rng.RandfRange(48, 78),
                -rng.RandfRange(32, 76) + Mathf.Abs(spread) * 14f);
            Vector2 direction = (tip - root).Normalized();
            Vector2 normal = new(-direction.Y, direction.X);

            painter.DrawLine(root, tip, new Color("#1b3037"), 1.2f, true);

            for (int leaf = 0; leaf < 6; leaf++)
            {
                float t = 0.2f + leaf * 0.12f;
                float side = leaf % 2 == 0 ? -1f : 1f;
                Vector2 start = root.Lerp(tip, t);
                float length = rng.RandfRange(12, 21) * (1f - t * 0.35f);
                Vector2 end = start
                    + normal * side * length
                    + direction * length * 0.65f;

                Color colour = VegetationPalette.GetLeaf(
                    palette, rng.RandfRange(0.1f, 0.8f),
                    leaf > 3 && rng.Randf() < 0.18f);
                if (branch < 4) colour = colour.Darkened(0.2f);

                VegetationDrawingHelpers.Leaflet(
                    painter, start, end, rng.RandfRange(2.7f, 4.5f), colour);
            }

            VegetationDrawingHelpers.Leaflet(
                painter, root.Lerp(tip, 0.78f), tip, 3f,
                VegetationPalette.GetLeaf(palette, 0.3f));
        }
    }
    #endregion
}