// Draws a lightweight translucent six-sided sci-fi frame for both notification systems.
// No textures, shaders, custom fonts, or per-frame drawing updates are required.
using Godot;

public partial class NotificationFrame : Control
{
    #region Appearance
    public Color Fill { get; set; } = new(0.025f, 0.075f, 0.10f, 0.86f);
    public Color Accent { get; set; } = new("#68dce2");
    #endregion

    #region Lifecycle
    // =========================================================
    // Redraw only when dimensions change.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += QueueRedraw;
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw an elongated hexagonal panel with a slim coloured outline.
    public override void _Draw()
    {
        float w = Size.X;
        float h = Size.Y;
        if (w < 8f || h < 8f) return;

        float tip = Mathf.Min(18f, h * 0.45f);
        Vector2[] points =
        {
            new(tip, 0f), new(w - tip, 0f),
            new(w, h * 0.5f), new(w - tip, h),
            new(tip, h), new(0f, h * 0.5f)
        };

        DrawColoredPolygon(points, Fill);

        Vector2[] outline =
        {
            points[0], points[1], points[2], points[3],
            points[4], points[5], points[0]
        };
        DrawPolyline(outline, Accent, 1.5f, true);

        // A small hexagonal emblem echoes the main silhouette.
        Vector2 centre = new(32f, h * 0.5f);
        float radius = Mathf.Min(15f, h * 0.21f);
        Vector2[] badge = new Vector2[7];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Pi / 3f * i;
            badge[i] = centre + new Vector2(
                Mathf.Cos(a) * radius, Mathf.Sin(a) * radius);
        }
        badge[6] = badge[0];
        DrawPolyline(badge, Accent, 1.4f, true);
    }
    #endregion
}
