// Defines eight deterministic rock silhouettes with independently shaded facets.
// Surface grain and fractures are applied by the shader during atlas baking.
using Godot;

public static class RockDrawing
{
    #region Configuration
    public const int VariantCount = 8;
    #endregion

    #region Drawing
    // =========================================================
// Draw an irregular boulder with terrain-independent, sun-shaded facets.
public static void Draw(CanvasItem canvas, WorldAtmosphere atmosphere, int variant)
{
    uint state = 0x9E3779B9u ^ ((uint)variant + 1u) * 7919u;
    float w = DrawingHelpers.Range(ref state, 50f, 61f);
    float h = DrawingHelpers.Range(ref state, 58f, 80f);
    float d = DrawingHelpers.Range(ref state, 19f, 24f);

    Vector2[] outline =
    {
        new(-w, -d * 0.35f),
        new(-w * 0.92f, -h * 0.55f),
        new(-w * 0.60f, -h * DrawingHelpers.Range(ref state, 0.92f, 1.10f)),
        new(w * DrawingHelpers.Range(ref state, -0.10f, 0.15f),
            -h * DrawingHelpers.Range(ref state, 1.05f, 1.18f)),
        new(w * 0.62f, -h * DrawingHelpers.Range(ref state, 0.80f, 1.00f)),
        new(w, -h * 0.36f),
        new(w * 0.88f, d * 0.25f),
        new(w * 0.10f, d),
        new(-w * 0.72f, d * 0.45f)
    };

    Vector2 hub = new(-w * 0.08f, -h * 0.48f);
    Color stone = (variant % 3) switch
    {
        0 => new Color("#484b4b"),
        1 => new Color("#4c4943"),
        _ => new Color("#41494e")
    };

    for (int i = 0; i < outline.Length; i++)
    {
        Vector2 a = outline[i], b = outline[(i + 1) % outline.Length];
        Vector2 normal = ((a + b) * 0.5f - hub).Normalized();
        float variation = DrawingHelpers.Range(ref state, 0.88f, 1.12f);
        Color face = new(stone.R * variation, stone.G * variation,
            stone.B * variation, 1f);
        if (atmosphere != null) face = atmosphere.ShadeFace(face, normal);

        canvas.DrawColoredPolygon(new Vector2[] { hub, a, b }, face);
    }

    canvas.DrawPolyline(new Vector2[] { outline[0], outline[1], outline[2] },
        new Color(0.56f, 0.57f, 0.53f, 0.30f), 1f, true);
}
    #endregion
}