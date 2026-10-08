// Draws the cube's world artwork and provides its shared inventory icon.
// Inspector-assigned item textures still override the generated icon.
using Godot;
using System;

public partial class TestCubeDrawing : Node2D
{
    #region Configuration
    [Export] public Vector2 BaseSize { get; set; } = new(128f, 64f);
    [Export] public float CubeHeight { get; set; } = 96f;
    #endregion

    #region Artwork
    private const string LeftColour = "#587880";
    private const string RightColour = "#344d58";
    private const string TopColour = "#9bb7b8";
    private const string EdgeColour = "#20333d";

    private static ImageTexture _icon;
    #endregion

    #region World Drawing
    // =========================================================
    // Draw the world cube with its bottom centred on the placement cell.
    public override void _Draw()
    {
        Vector2 back = new(0f, -BaseSize.Y * 0.5f);
        Vector2 right = new(BaseSize.X * 0.5f, 0f);
        Vector2 front = new(0f, BaseSize.Y * 0.5f);
        Vector2 left = new(-BaseSize.X * 0.5f, 0f);
        Vector2 lift = Vector2.Up * CubeHeight;

        DrawColoredPolygon(
            new[] { left, front, front + lift, left + lift },
            new Color(LeftColour));

        DrawColoredPolygon(
            new[] { front, right, right + lift, front + lift },
            new Color(RightColour));

        DrawColoredPolygon(
            new[] { back + lift, right + lift, front + lift, left + lift },
            new Color(TopColour));

        Color edge = new(EdgeColour);

        DrawPolyline(
            new[] { left, front, right, right + lift, back + lift,
                left + lift, left },
            edge, 2f, true);

        DrawLine(left + lift, front + lift, edge, 2f, true);
        DrawLine(front + lift, right + lift, edge, 2f, true);
        DrawLine(front, front + lift, edge, 2f, true);
    }
    #endregion

    #region Inventory Drawing
    // =========================================================
    // Generate and cache a small icon without creating a world artwork node.
    public static Texture2D GetIconTexture()
    {
        if (GodotObject.IsInstanceValid(_icon))
            return _icon;

        string svg =
            "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'>" +
            $"<g stroke='{EdgeColour}' stroke-width='1.5' stroke-linejoin='round'>" +
            $"<path d='M5 16 L24 6 L43 16 L24 26Z' fill='{TopColour}'/>" +
            $"<path d='M5 16 L24 26 L24 43 L5 33Z' fill='{LeftColour}'/>" +
            $"<path d='M24 26 L43 16 L43 33 L24 43Z' fill='{RightColour}'/>" +
            "</g></svg>";

        using Image image = new();
        Error error = image.LoadSvgFromString(svg);

        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Unable to generate Test Cube icon: {error}");

        _icon = ImageTexture.CreateFromImage(image);
        return _icon;
    }
    #endregion
}