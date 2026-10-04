// Defines the canonical sci-fi crate artwork independently of gameplay.
// The baker supplies the atmosphere used to shade its sides.
using Godot;

public static class CrateDrawing
{
    #region Drawing
    // =========================================================
    // Bake the crate with shaded sides, a bright top and its cyan strip.
    public static void Draw(CanvasItem canvas, WorldAtmosphere atmosphere)
    {
        const float w = 48f, d = 24f, height = 72f;
        DrawingHelpers.Shadow(canvas, 62.4f, 0.5f, 0.3f);
        Vector2 a = new(-w, -height), b = new(0, -height - d);
        Vector2 c = new(w, -height), e = new(0, -height + d);

        Color left = new("#344756"), right = new("#263541"), top = new("#61798a");
        if (atmosphere != null)
        {
            left = atmosphere.ShadeFace(left, new Vector2(-1f, 0f));
            right = atmosphere.ShadeFace(right, new Vector2(1f, 0f));
            top = atmosphere.ShadeFace(top, new Vector2(0f, -1f));
        }

        canvas.DrawColoredPolygon(new Vector2[] { a, e, new(0, d), new(-w, 0) }, left);
        canvas.DrawColoredPolygon(new Vector2[] { e, c, new(w, 0), new(0, d) }, right);
        canvas.DrawColoredPolygon(new Vector2[] { a, b, c, e }, top);
        canvas.DrawPolyline(new Vector2[] { a, b, c }, new Color("#acbbc0"), 2f, true);
        canvas.DrawLine(new Vector2(w * 0.25f, -height * 0.45f),
            new Vector2(w * 0.75f, -height * 0.65f), new Color("#76e2e7"), 3f);
    }
    #endregion
}