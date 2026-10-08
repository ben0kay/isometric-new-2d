// Draws charcoal alien trees with curved bark and layered coloured foliage.
// Runs only during the shared tree bake, never during gameplay.
using Godot;

public static class CarbonTreeDrawing
{
    #region Configuration
    public const int VariantCount = 16;
    private static readonly Color Bark = new("#252c32");
    #endregion

    #region Drawing
    // =========================================================
    // Build a varied tree silhouette with roots, branches and overlapping crowns.
    public static void Draw(Node2D painter, int variant)
    {
        int shape = variant % 4;
        int palette = variant / 4;
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(38117 + shape * 1597);

        float height = rng.RandfRange(250f, 300f);
        float lean = rng.RandfRange(-24f, 24f);
        Vector2 crown = new(lean, -height);

        // Rear foliage sits behind the trunk and main branches.
        for (int i = 0; i < 7; i++)
        {
            Vector2 centre = crown + new Vector2(
                rng.RandfRange(-105f, 105f),
                rng.RandfRange(-15f, 62f));
            DrawCrown(painter, rng, centre, palette, true);
        }

        // Exposed roots spread along the ground plane.
        for (int i = 0; i < 7; i++)
        {
            float spread = (i - 3f) / 3f;
            Vector2 tip = new(
                spread * rng.RandfRange(36f, 65f),
                rng.RandfRange(-4f, 13f));
            DrawBranch(painter, new Vector2(0, -24),
                new Vector2(tip.X * 0.25f, -3), tip, 12f, 1.5f);
        }

        DrawBranch(painter, Vector2.Zero,
            new Vector2(-lean * 0.6f, -height * 0.5f),
            crown, 18f, 5f);

        // Branch tips support independently shaped foliage clusters.
        for (int i = 0; i < 8; i++)
        {
            float spread = (i - 3.5f) / 3.5f;
            Vector2 start = new(
                lean * 0.35f, -height * rng.RandfRange(0.38f, 0.68f));
            Vector2 tip = crown + new Vector2(
                spread * rng.RandfRange(95f, 145f),
                rng.RandfRange(4f, 75f));
            Vector2 control = new(
                tip.X * 0.6f, start.Y - rng.RandfRange(35f, 75f));

            DrawBranch(painter, start, control, tip,
                rng.RandfRange(6f, 10f), 1.3f);
            DrawCrown(painter, rng, tip, palette, false);
        }

        DrawCrown(painter, rng, crown + new Vector2(-12, -12),
            palette, false);
    }

    // =========================================================
    // Evaluate one curved branch or foliage stem.
    private static Vector2 Curve(
        Vector2 start, Vector2 control, Vector2 end, float t)
    {
        float u = 1f - t;
        return start * u * u + control * 2f * u * t + end * t * t;
    }

    // =========================================================
    // Draw tapered bark as shaded strips instead of a centre-point polygon fan.
    private static void DrawBranch(
        Node2D painter, Vector2 start, Vector2 control, Vector2 end,
        float baseWidth, float tipWidth)
    {
        const int steps = 18;
        const int bands = 4;
        float[] lighting = { 1.45f, 1.1f, 0.72f, 0.45f };

        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;
            Vector2 a = Curve(start, control, end, t0);
            Vector2 b = Curve(start, control, end, t1);
            Vector2 direction = b - a;
            if (direction.LengthSquared() < 0.001f) continue;

            Vector2 normal = new Vector2(-direction.Y, direction.X).Normalized();
            float widthA = Mathf.Lerp(baseWidth, tipWidth, t0);
            float widthB = Mathf.Lerp(baseWidth, tipWidth, t1);

            for (int band = 0; band < bands; band++)
            {
                float s0 = -1f + band * 2f / bands;
                float s1 = -1f + (band + 1) * 2f / bands;
                float variation = 1f + Mathf.Sin(i * 0.8f + band) * 0.07f;
                float shade = lighting[band] * variation;
                Color colour = new(
                    Bark.R * shade, Bark.G * shade, Bark.B * shade);

                painter.DrawColoredPolygon(new Vector2[]
                {
                    a + normal * widthA * s0,
                    a + normal * widthA * s1,
                    b + normal * widthB * s1,
                    b + normal * widthB * s0
                }, colour);
            }
        }

        // Fine bark grooves follow the branch rather than radiating from its centre.
        for (int groove = 0; groove < 3; groove++)
        {
            Vector2[] points = new Vector2[steps + 1];
            float across = (groove - 1f) * 0.48f;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 tangent = (control - start) * (1f - t)
                    + (end - control) * t;
                Vector2 normal = tangent.LengthSquared() > 0.001f
                    ? new Vector2(-tangent.Y, tangent.X).Normalized()
                    : Vector2.Right;
                points[i] = Curve(start, control, end, t)
                    + normal * Mathf.Lerp(baseWidth, tipWidth, t) * across;
            }
            painter.DrawPolyline(points, new Color(0.04f, 0.06f, 0.08f, 0.5f),
                0.8f, true);
        }
    }

    // =========================================================
    // Layer small feather-like sprays with coherent blue, purple and cyan shades.
    private static void DrawCrown(
        Node2D painter, RandomNumberGenerator rng,
        Vector2 centre, int palette, bool rear)
    {
        for (int spray = 0; spray < 7; spray++)
        {
            float angle = rng.RandfRange(-Mathf.Pi, 0f);
            Vector2 start = centre + new Vector2(
                rng.RandfRange(-14f, 14f), rng.RandfRange(-5f, 8f));
            Vector2 end = start + new Vector2(
                Mathf.Cos(angle) * rng.RandfRange(30f, 62f),
                Mathf.Sin(angle) * rng.RandfRange(22f, 45f));
            Vector2 control = start.Lerp(end, 0.5f) + new Vector2(0, -9);

            Vector2[] stem = new Vector2[9];
            for (int i = 0; i < stem.Length; i++)
                stem[i] = Curve(start, control, end, i / 8f);
            painter.DrawPolyline(stem, new Color("#263c46"), 1.1f, true);

            for (int pair = 0; pair < 6; pair++)
            {
                float t = 0.14f + pair * 0.135f;
                Vector2 root = Curve(start, control, end, t);
                Vector2 tangent = (
                    (control - start) * (1f - t) + (end - control) * t
                ).Normalized();
                Vector2 normal = new(-tangent.Y, tangent.X);
                float length = Mathf.Lerp(17f, 6f, pair / 5f);

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 tip = root
                        + normal * side * length * rng.RandfRange(0.8f, 1.2f)
                        + tangent * length * 0.55f;
                    Color colour = VegetationPalette.GetLeaf(
                        palette, rng.Randf(),
                        !rear && rng.Randf() < 0.2f);
                    colour = rear
                        ? colour.Darkened(0.32f)
                        : colour.Lightened(rng.RandfRange(0f, 0.12f));

                    VegetationDrawingHelpers.Leaflet(
                        painter, root, tip,
                        Mathf.Lerp(3.2f, 1.3f, pair / 5f), colour);
                }
            }
        }
    }
    #endregion
}