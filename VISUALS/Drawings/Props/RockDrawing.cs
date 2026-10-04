// Defines the canonical rock artwork independently of atlas layout and gameplay.
// The baker supplies the atmosphere used to shade its facets.
using Godot;

public static class RockDrawing
{
    #region Drawing
    // =========================================================
    // Bake the rock with a sun-facing facet and brighter upper-left edge.
    public static void Draw(CanvasItem canvas, WorldAtmosphere atmosphere)
    {
        const float w = 62.4f, d = 24f, height = 72f;
        DrawingHelpers.Shadow(canvas, w, 0.5f, 0.3f);

        Color dark = new("#47505c"), light = new("#5c6876");
        if (atmosphere != null)
        {
            dark = atmosphere.ShadeFace(dark, new Vector2(1f, 0.35f));
            light = atmosphere.ShadeFace(light, new Vector2(-1f, -0.4f));
        }

        canvas.DrawColoredPolygon(new Vector2[]
        {
            new(-w, -d), new(-w * 0.75f, -height),
            new(-w * 0.15f, -height - 16), new(w * 0.65f, -height + 4),
            new(w, -d), new(w * 0.55f, d), new(-w * 0.55f, d)
        }, dark);

        canvas.DrawColoredPolygon(new Vector2[]
        {
            new(-w, -d), new(-w * 0.75f, -height),
            new(-w * 0.15f, -height - 16), new(w * 0.1f, -d * 0.4f),
            new(-w * 0.55f, d)
        }, light);

        canvas.DrawPolyline(new Vector2[]
        {
            new(-w, -d), new(-w * 0.75f, -height),
            new(-w * 0.15f, -height - 16)
        }, new Color("#a2b2b7"), 2f, true);

        canvas.DrawPolyline(new Vector2[]
        {
            new(-w * 0.4f, -height * 0.8f),
            new(-w * 0.1f, -height * 0.5f),
            new(w * 0.3f, -height * 0.35f)
        }, new Color("#71b5c4"), 3f, true);
    }
    #endregion
}