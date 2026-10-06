// Displays a cached robot wreck sprite on the existing terrain elevation system.
// The temporary painter draws once; wreck instances have no blocking collision.
using Godot;
using System.Threading.Tasks;

public partial class BasicRobotWreckDrawing : Node2D
{
    #region Configuration
    [Export] public int ArtworkRevision { get; set; } = 1;
    private static Task<ImageTexture> _texture;
    private static int _revision = -1;
    #endregion

    #region Artwork
    // =========================================================
    // Bake once and attach shared artwork to this walkable wreck.
    public override async void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);

        int revision = ArtworkRevision;
        if (_texture == null || _revision != revision)
        {
            _revision = revision;
            _texture = ArtworkBaker.LoadOrBake(
                GetTree().Root, "basic_robot_wreck",
                $"basic_robot_wreck:{revision}",
                new Vector2I(192, 144),
                () => new BasicRobotWreckPainter());
        }

        try
        {
            ImageTexture texture = await _texture;
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            TerrainVisual.Attach(
                this, new Rect2(0, 0, 192, 144),
                new Vector2(-96, -108), Vector2.One,
                false, null, texture);
        }
        catch (System.Exception error)
        {
            _texture = null;
            GD.PushError($"Robot wreck artwork failed: {error}");
        }
    }
    #endregion
}

public partial class BasicRobotWreckPainter : Node2D
{
    #region Painting
    // =========================================================
    // Draw a collapsed chassis, detached head and exposed wiring.
    public override void _Draw()
    {
        Color outline = new("#17262c");
        Color top = new("#697e81");
        Color side = new("#344851");
        Color dark = new("#24353d");

        Face(side, new(38, 83), new(96, 107), new(96, 123), new(38, 98));
        Face(dark, new(96, 107), new(139, 83), new(139, 101), new(96, 123));
        Face(top, new(38, 83), new(80, 61), new(139, 83), new(96, 107));

        Face(new Color("#465b60"),
            new(58, 82), new(85, 69), new(115, 83), new(88, 98));
        Face(outline, new(72, 83), new(87, 75), new(105, 84), new(88, 92));

        DrawLine(new(82, 81), new(100, 85), new Color("#bd804d"), 3f, true);
        DrawLine(new(85, 84), new(96, 91), new Color("#6ab8af"), 2f, true);
        DrawLine(new(91, 78), new(88, 91), new Color("#62717a"), 2f, true);

        Face(top, new(130, 56), new(150, 46), new(173, 57), new(152, 68));
        Face(side, new(130, 56), new(152, 68), new(152, 87), new(130, 75));
        Face(dark, new(152, 68), new(173, 57), new(173, 76), new(152, 87));
        DrawLine(new(156, 72), new(166, 67), new Color("#4f8f99"), 3f, true);

        DrawLine(new(57, 74), new(29, 62), outline, 15f, true);
        DrawLine(new(57, 72), new(29, 60), top, 8f, true);
        DrawLine(new(29, 60), new(15, 77), side, 10f, true);
        DrawCircle(new(29, 61), 7f, dark);

        DrawLine(new(121, 96), new(153, 109), outline, 16f, true);
        DrawLine(new(121, 94), new(153, 107), side, 10f, true);
        DrawLine(new(153, 107), new(179, 94), top, 8f, true);
        DrawCircle(new(153, 108), 7f, dark);

        Face(new Color("#556a70"),
            new(23, 104), new(35, 97), new(50, 104), new(38, 112));
        DrawLine(new(58, 91), new(70, 97), new Color("#9b734e"), 3f, true);
        DrawLine(new(107, 102), new(117, 97), new Color("#779292"), 2f, true);
    }

    // =========================================================
    // Paint one armour face and its dark perimeter.
    private void Face(Color color, params Vector2[] points)
    {
        DrawColoredPolygon(points, color);
        for (int i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length],
                new Color("#17262c"), 2f, true);
    }
    #endregion
}