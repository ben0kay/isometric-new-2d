// Draws curved alien grass blades with varied colours and directional shading.
// Geometry runs only during atlas baking; gameplay uses cached sprites.
using Godot;

public static class GrassDrawingHelpers
{
    #region Drawing
    // =========================================================
    // Build one irregular tuft using a selected shape and related colour palette.
    public static void Draw(
        Node2D painter, int variant, float height, int bladeCount)
    {
        int shape = variant % VegetationPalette.ShapeCount;
        int palette = variant / VegetationPalette.ShapeCount;
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(74119 + shape * 997);

        for (int blade = 0; blade < bladeCount; blade++)
        {
            Vector2 root = new(
                rng.RandfRange(-17f, 17f), rng.RandfRange(-3f, 3f));
            float length = height * rng.RandfRange(0.55f, 1f);
            Vector2 tip = root + new Vector2(
                rng.RandfRange(-10f, 10f), -length);
            Vector2 control = root.Lerp(tip, 0.5f)
                + new Vector2(rng.RandfRange(-5f, 5f), -length * 0.15f);

            Color colour = VegetationPalette.GetLeaf(
                palette, rng.Randf(), rng.Randf() < 0.12f);
            colour = colour.Darkened(rng.RandfRange(0.05f, 0.22f));

            DrawBlade(painter, root, control, tip,
                rng.RandfRange(0.6f, 1.25f), colour);
        }
    }

    // =========================================================
    // Evaluate a curved blade from its ground contact to its tip.
    private static Vector2 Curve(
        Vector2 root, Vector2 control, Vector2 tip, float t)
    {
        float u = 1f - t;
        return root * u * u + control * 2f * u * t + tip * t * t;
    }

    // =========================================================
    // Draw a tapered blade as explicit triangles with shaded bases and bright tips.
    private static void DrawBlade(
        Node2D painter, Vector2 root, Vector2 control,
        Vector2 tip, float width, Color colour)
    {
        const int steps = 6;
        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            Vector2 a = Curve(root, control, tip, t0);
            Vector2 b = Curve(root, control, tip, t1);
            Vector2 direction = b - a;
            if (direction.LengthSquared() < 0.0001f) continue;

            Vector2 normal = new Vector2(-direction.Y, direction.X).Normalized();
            float w0 = width * (1f - t0);
            float w1 = width * (1f - t1);
            Vector2 leftA = a + normal * w0;
            Vector2 rightA = a - normal * w0;
            Vector2 leftB = b + normal * w1;
            Vector2 rightB = b - normal * w1;

            Color lower = colour.Darkened(0.6f * (1f - t0));
            Color upper = colour.Darkened(0.6f * (1f - t1));
            Triangle(painter, leftA, rightA, rightB,
                lower.Lightened(0.04f), lower, upper);
            Triangle(painter, leftA, rightB, leftB,
                lower.Lightened(0.04f), upper, upper.Lightened(0.04f));
        }
    }

    // =========================================================
    // Skip collapsed tip triangles and preserve consistent polygon winding.
    private static void Triangle(
        Node2D painter, Vector2 a, Vector2 b, Vector2 c,
        Color ca, Color cb, Color cc)
    {
        float area = (b - a).Cross(c - a);
        if (!float.IsFinite(area) || Mathf.Abs(area) < 0.0001f) return;
        if (area < 0f)
        {
            (b, c) = (c, b);
            (cb, cc) = (cc, cb);
        }
        painter.DrawPolygon(
            new Vector2[] { a, b, c }, new Color[] { ca, cb, cc });
    }
    #endregion
}