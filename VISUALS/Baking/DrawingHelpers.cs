// Provides shared drawing and deterministic randomness for baked artwork.
// These methods are called during the initial atlas bake, not during gameplay.
using Godot;

public static class DrawingHelpers
{
    #region Ground
    private static readonly Vector2[] Diamond =
    {
        new(0, -32), new(64, 0), new(0, 32), new(-64, 0)
    };

    // =========================================================
    // Draw the shared dark ground base and restrained surface grain.
    public static void GroundBase(CanvasItem canvas, uint seed)
    {
        canvas.DrawColoredPolygon(Diamond, new Color("#18212b"));
        uint state = seed;

        for (int i = 0; i < 80; i++)
        {
            Vector2 point = GroundPoint(ref state, 0.44f);
            float brightness = Range(ref state, 0.25f, 0.42f);
            Color color = new(brightness, brightness + 0.025f,
                brightness + 0.04f, Range(ref state, 0.08f, 0.19f));
            canvas.DrawRect(new Rect2(point, new Vector2(1.5f, 0.75f)), color);
        }
    }

    // =========================================================
    // Place a detail inside an inset diamond using logical tile coordinates.
    public static Vector2 GroundPoint(ref uint state, float extent = 0.32f)
    {
        float u = Range(ref state, -extent, extent);
        float v = Range(ref state, -extent, extent);
        return new Vector2((u - v) * 64f, (u + v) * 32f);
    }
    #endregion

    #region Shapes
    // =========================================================
    // Draw an ellipse using a small polygon during baking.
    public static void Ellipse(CanvasItem canvas, Vector2 center,
        Vector2 radius, Color color, int segments = 24)
    {
        Vector2[] points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            points[i] = center + new Vector2(
                Mathf.Cos(angle) * radius.X, Mathf.Sin(angle) * radius.Y);
        }
        canvas.DrawColoredPolygon(points, color);
    }

    // =========================================================
    // Draw a canonical feet shadow for a baked character or prop.
    public static void Shadow(CanvasItem canvas, float radius,
        float verticalScale, float opacity)
    {
        Ellipse(canvas, Vector2.Zero, new Vector2(radius, radius * verticalScale),
            new Color(0, 0, 0, opacity), 32);
    }
    #endregion

    #region Randomness
    // =========================================================
    // Advance a local deterministic random sequence without global RNG state.
    public static float Next(ref uint state)
    {
        if (state == 0) state = 0x6D2B79F5u;
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFFu) / 16777216f;
    }

    // =========================================================
    // Sample a value within the supplied range.
    public static float Range(ref uint state, float minimum, float maximum)
    {
        return minimum + (maximum - minimum) * Next(ref state);
    }
    #endregion
}