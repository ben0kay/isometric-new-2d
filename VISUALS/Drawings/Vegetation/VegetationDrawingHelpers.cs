// Builds curved vegetation leaves with graduated shading and fine surface detail.
// Geometry and drawing run only during the shared vegetation bake.
using Godot;

public static class VegetationDrawingHelpers
{
    #region Geometry
    // =========================================================
    // Evaluate a quadratic curve from the leaf base to its tip.
    private static Vector2 Curve(
        Vector2 start, Vector2 control, Vector2 end, float t)
    {
        float u = 1f - t;
        return u * u * start + 2f * u * t * control + t * t * end;
    }

    // =========================================================
    // Sample a gently irregular surface across the width of a curved leaf.
    private static Vector2 Surface(
        Vector2 start, Vector2 control, Vector2 end,
        float width, float phase, float t, float across)
    {
        Vector2 centre = Curve(start, control, end, t);
        if (t <= 0f || t >= 1f) return centre;

        Vector2 tangent =
            2f * (1f - t) * (control - start) + 2f * t * (end - control);
        Vector2 normal = tangent.LengthSquared() > 0.000001f
            ? new Vector2(-tangent.Y, tangent.X).Normalized()
            : Vector2.Right;

        float taper = Mathf.Pow(
            Mathf.Max(0f, Mathf.Sin(Mathf.Pi * t)), 0.8f);
        float irregularity = 1f + 0.035f * Mathf.Sin(t * 25f + phase);
        float asymmetry = across < 0f ? 0.88f : 1f;
        return centre + normal * across * width
            * taper * irregularity * asymmetry;
    }

    // =========================================================
    // Shade the base, curved surface and upper-left exposure independently.
    private static Color SurfaceColour(
        Color shade, Vector2 point, float t, float across, float phase)
    {
        float raisedCentre = 1f - Mathf.Abs(across);
        float exposure = Mathf.Clamp(
            0.5f - point.X / 200f - point.Y / 400f, 0f, 1f);
        float mottling = Mathf.Sin(point.X * 0.19f + phase)
            * Mathf.Sin(point.Y * 0.13f - phase) * 0.035f;
        float brightness = 0.47f + t * 0.22f
            + raisedCentre * 0.12f + exposure * 0.18f + mottling;

        return new Color(
            shade.R * brightness,
            shade.G * brightness,
            shade.B * brightness, 1f);
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw a curved leaf with smooth shading, restrained veins and fine mottling.
    public static void Leaf(
        Node2D painter, Vector2 start, Vector2 control, Vector2 end,
        float width, Color shade, float phase)
    {
        const int steps = 24;
        const int bands = 4;

        for (int i = 0; i < steps; i++)
        {
            float t0 = i / (float)steps;
            float t1 = (i + 1) / (float)steps;

            for (int band = 0; band < bands; band++)
            {
                float s0 = -1f + band * 2f / bands;
                float s1 = -1f + (band + 1) * 2f / bands;
                Vector2 a = Surface(start, control, end, width, phase, t0, s0);
                Vector2 b = Surface(start, control, end, width, phase, t0, s1);
                Vector2 c = Surface(start, control, end, width, phase, t1, s1);
                Vector2 d = Surface(start, control, end, width, phase, t1, s0);

                Color ca = SurfaceColour(shade, a, t0, s0, phase);
                Color cb = SurfaceColour(shade, b, t0, s1, phase);
                Color cc = SurfaceColour(shade, c, t1, s1, phase);
                Color cd = SurfaceColour(shade, d, t1, s0, phase);

                if (i == 0)
                {
                    painter.DrawPolygon(
                        new Vector2[] { a, c, d },
                        new Color[] { ca, cc, cd });
                }
                else if (i == steps - 1)
                {
                    painter.DrawPolygon(
                        new Vector2[] { a, b, c },
                        new Color[] { ca, cb, cc });
                }
                else
                {
                    painter.DrawPolygon(
                        new Vector2[] { a, b, c, d },
                        new Color[] { ca, cb, cc, cd });
                }
            }
        }

        Vector2[] vein = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
            vein[i] = Curve(start, control, end, i / (float)steps);

        painter.DrawPolyline(vein,
            new Color(shade.R * 0.8f, shade.G * 0.9f, shade.B, 0.42f),
            0.7f, true);

        for (int i = 4; i < steps - 3; i += 3)
        {
            float t = i / (float)steps;
            Vector2 root = Curve(start, control, end, t - 0.035f);
            painter.DrawLine(root,
                Surface(start, control, end, width, phase, t, -0.85f),
                new Color(0.06f, 0.1f, 0.13f, 0.28f), 0.55f, true);
            painter.DrawLine(root,
                Surface(start, control, end, width, phase, t, 0.85f),
                new Color(0.06f, 0.1f, 0.13f, 0.22f), 0.55f, true);
        }

        for (int i = 0; i < 28; i++)
        {
            float t = 0.12f + i / 28f * 0.76f;
            float across = Mathf.Sin(i * 7.13f + phase) * 0.75f;
            Vector2 point = Surface(
                start, control, end, width, phase, t, across);
            painter.DrawCircle(point, 0.45f,
                new Color(0.3f, 0.45f, 0.5f, 0.12f));
        }
    }
    #endregion
}