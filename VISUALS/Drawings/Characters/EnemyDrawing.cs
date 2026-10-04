// Defines the security drone artwork independently of enemy AI and combat.
// Its image is baked once and reused by all existing enemy instances.
using Godot;

public static class EnemyDrawing
{
    #region Drawing
    // =========================================================
    // Bake the angular red drone with its feet shadow and glowing sensor.
    public static void Draw(CanvasItem canvas)
    {
        DrawingHelpers.Shadow(canvas, 22f, 0.45f, 0.35f);
        canvas.DrawLine(new Vector2(-12, -18), new Vector2(-18, -3), new Color("#472b35"), 6f);
        canvas.DrawLine(new Vector2(12, -18), new Vector2(18, -3), new Color("#472b35"), 6f);

        canvas.DrawColoredPolygon(new Vector2[]
        {
            new(-22, -29), new(-13, -45), new(13, -45),
            new(22, -29), new(12, -13), new(-12, -13)
        }, new Color("#763a49"));

        canvas.DrawPolyline(new Vector2[]
        {
            new(-22, -29), new(-13, -45), new(13, -45),
            new(22, -29), new(12, -13), new(-12, -13), new(-22, -29)
        }, new Color("#ba6472"), 2f, true);

        canvas.DrawRect(new Rect2(-13, -34, 26, 8), new Color("#231d29"));
        canvas.DrawRect(new Rect2(-9, -32, 18, 4), new Color("#ff7164"));
        canvas.DrawLine(new Vector2(-8, -19), new Vector2(8, -19), new Color("#b55362"), 3f);
    }
    #endregion
}