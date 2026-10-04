// Provides curved leaf geometry for independently defined vegetation drawings.
// These drawing calls run during baking, never during normal gameplay.
using Godot;

public static class VegetationDrawingHelpers
{
    #region Geometry
    // =========================================================
    // Evaluate a quadratic curve between the leaf base and tip.
    private static Vector2 Curve(Vector2 start, Vector2 control, Vector2 end, float t)
    {
        float u = 1f - t;
        return u * u * start + 2f * u * t * control + t * t * end;
    }

// =========================================================
// Draw a curved shaded blade with finite geometry and exact tapered endpoints.
public static void Leaf(
    Node2D painter, Vector2 start, Vector2 control, Vector2 end,
    float width, Color shade, float phase)
{
    const int steps = 20;
    Vector2[] centre = new Vector2[steps + 1];
    Vector2[] left = new Vector2[steps + 1];
    Vector2[] right = new Vector2[steps + 1];

    for (int i = 0; i <= steps; i++)
    {
        float t = i / (float)steps;
        centre[i] = Curve(start, control, end, t);

        if (i == 0 || i == steps)
        {
            left[i] = right[i] = centre[i];
            continue;
        }

        Vector2 tangent =
            2f * (1f - t) * (control - start) + 2f * t * (end - control);
        Vector2 normal = tangent.LengthSquared() > 0.000001f
            ? new Vector2(-tangent.Y, tangent.X).Normalized()
            : Vector2.Right;

        float taper = Mathf.Max(0f, Mathf.Sin(Mathf.Pi * t));
        float breadth = width * Mathf.Pow(taper, 0.85f);
        float irregularity = 1f + 0.09f * Mathf.Sin(t * 33f + phase);

        left[i] = centre[i] + normal * breadth * irregularity;
        right[i] = centre[i] - normal * breadth
            * (0.82f + 0.06f * Mathf.Sin(t * 27f - phase));
    }

    Vector2[] silhouette = new Vector2[steps * 2];
    Vector2[] brightSide = new Vector2[steps * 2];
    for (int i = 0; i <= steps; i++)
    {
        silhouette[i] = left[i];
        brightSide[i] = left[i];
    }
    for (int i = 1; i < steps; i++)
    {
        silhouette[steps + i] = right[steps - i];
        brightSide[steps + i] = centre[steps - i];
    }

    painter.DrawColoredPolygon(silhouette, shade.Darkened(0.22f));
    painter.DrawColoredPolygon(brightSide, shade.Lightened(0.12f));
    painter.DrawPolyline(centre, shade.Lightened(0.24f), 0.8f, true);
    painter.DrawPolyline(left, shade.Lightened(0.18f), 0.65f, true);

    for (int i = 4; i < steps - 2; i += 3)
    {
        painter.DrawLine(centre[i - 1], left[i],
            shade.Lightened(0.08f), 0.55f, true);
        painter.DrawLine(centre[i - 1], right[i],
            shade.Darkened(0.12f), 0.55f, true);
    }
}
    #endregion
}