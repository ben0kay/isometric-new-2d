// Draws temporary cube artwork with its bottom centred on an isometric cell.
// Both the placed object and preview use the same artwork scene.
using Godot;

public partial class TestCubeDrawing : Node2D
{
    #region Configuration
    [Export] public Vector2 BaseSize { get; set; } = new(128f, 64f);
    [Export] public float CubeHeight { get; set; } = 96f;
    #endregion

    // =========================================================
    // Draw a simple three-face cube without physics or gameplay behaviour.
    public override void _Draw()
    {
        Vector2 back = new(0f, -BaseSize.Y * 0.5f);
        Vector2 right = new(BaseSize.X * 0.5f, 0f);
        Vector2 front = new(0f, BaseSize.Y * 0.5f);
        Vector2 left = new(-BaseSize.X * 0.5f, 0f);
        Vector2 lift = Vector2.Up * CubeHeight;

        DrawColoredPolygon(
            new[] { left, front, front + lift, left + lift },
            new Color("#587880"));

        DrawColoredPolygon(
            new[] { front, right, right + lift, front + lift },
            new Color("#344d58"));

        DrawColoredPolygon(
            new[] { back + lift, right + lift, front + lift, left + lift },
            new Color("#9bb7b8"));

        Color edge = new("#20333d");
        DrawPolyline(
            new[] { left, front, right, right + lift, back + lift,
                left + lift, left },
            edge, 2f, true);

        DrawLine(left + lift, front + lift, edge, 2f, true);
        DrawLine(front + lift, right + lift, edge, 2f, true);
        DrawLine(front, front + lift, edge, 2f, true);
    }
}