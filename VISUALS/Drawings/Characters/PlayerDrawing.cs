// Defines the placeholder player artwork independently of movement and combat.
// The right-facing baked image is mirrored by the existing player visual.
using Godot;

public static class PlayerDrawing
{
    #region Drawing
    // =========================================================
    // Bake the player body, feet shadow and cyan visor.
    public static void Draw(CanvasItem canvas)
    {
        DrawingHelpers.Shadow(canvas, 19f, 0.45f, 0.35f);
        canvas.DrawLine(new Vector2(-7, -15), new Vector2(-7, -3), new Color("#24313e"), 7f);
        canvas.DrawLine(new Vector2(7, -15), new Vector2(7, -3), new Color("#24313e"), 7f);
        canvas.DrawRect(new Rect2(-13, -39, 26, 27), new Color("#465d70"));
        canvas.DrawRect(new Rect2(-13, -39, 26, 27), new Color("#8398a6"), false, 2f);
        canvas.DrawCircle(new Vector2(0, -46), 12f, new Color("#708697"));
        canvas.DrawRect(new Rect2(-4, -50, 12, 7), new Color("#77e5ee"));
        canvas.DrawLine(new Vector2(13, -31), new Vector2(23, -24), new Color("#354b5c"), 6f);
        canvas.DrawRect(new Rect2(-5, -34, 10, 4), new Color("#77e5ee"));
    }
    #endregion
}