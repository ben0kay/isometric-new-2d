// Draws the armoured crate's placeholder artwork for a shared cached bake.
// Capacity and interaction are handled by the separate storage component.
using Godot;

public partial class ArmouredCrateDrawing : Node2D
{
    #region Drawing
    // =========================================================
    // Draw rigid armour panels, corner reinforcement and status lights.
    public override void _Draw()
    {
        Face(new Color("#7e9295"),
            new(20, 51), new(79, 22), new(140, 52), new(81, 83));
        Face(new Color("#354853"),
            new(20, 51), new(81, 83), new(81, 114), new(20, 82));
        Face(new Color("#233640"),
            new(81, 83), new(140, 52), new(140, 83), new(81, 114));

        Face(new Color("#425b62"),
            new(35, 47), new(79, 26), new(123, 48), new(80, 70));
        Face(new Color("#20323d"),
            new(30, 66), new(69, 86), new(69, 101), new(30, 81));
        Face(new Color("#152934"),
            new(91, 87), new(130, 67), new(130, 83), new(91, 103));

        Face(new Color("#647e83"),
            new(20, 51), new(32, 57), new(32, 88), new(20, 82));
        Face(new Color("#526f79"),
            new(70, 77), new(81, 83), new(81, 114), new(70, 108));
        Face(new Color("#405d69"),
            new(130, 57), new(140, 52), new(140, 83), new(130, 88));
        Face(new Color("#253c47"),
            new(78, 38), new(96, 47), new(84, 53), new(66, 44));

        DrawLine(new(42, 43), new(83, 64), new Color("#a1b4b0"), 3f, true);
        DrawLine(new(43, 77), new(57, 84), new Color("#68d4df"), 4f, true);
        DrawLine(new(102, 83), new(120, 74), new Color("#dca85c"), 4f, true);
        SetProcess(false);
    }

    // =========================================================
    // Fill and outline an individual armour surface.
    private void Face(Color color, params Vector2[] points)
    {
        DrawColoredPolygon(points, color);
        for (int i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length],
                new Color("#172a35"), 2f, true);
    }
    #endregion
}