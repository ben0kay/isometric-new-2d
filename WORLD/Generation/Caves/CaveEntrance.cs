// Draws shared doorway artwork for surface and underground layer connections.
// Artwork is separate from traversal, collision and surface clearance.
using Godot;

public partial class CaveEntrance : Node2D
{
    #region Appearance
    [ExportGroup("Appearance")]
    [Export] public float ArchWidth { get; set; } = 124f;
    [Export] public float ArchHeight { get; set; } = 108f;

    [ExportGroup("Debug")]
    [Export] public bool ShowDebugMarker { get; set; } = false;
    #endregion

    #region State
    public CaveWorld World { get; set; }
    public WorldLayerConnection Connection { get; set; }

    private static readonly Color Outline = new("#20252b");
    private static readonly Color Mouth = new("#080c10");
    #endregion

    #region Drawing
    // =========================================================
    // Draw at surface elevation; Godot retains these drawing commands.
    public override void _Draw()
    {
        if (World == null || Connection == null) return;

        Vector2 centre = Vector2.Up * Connection.RimHeight;
        Vector2 direction = IsoGrid.TileToWorld(
            Connection.Direction, World.TileSize).Normalized();

        float width = Mathf.Max(60f, ArchWidth);
        float height = Mathf.Max(50f, ArchHeight);

        DrawApproach(centre, direction, width);
        DrawOpening(centre, width, height);
        DrawArch(centre, width, height);

        if (ShowDebugMarker)
            DrawDebugMarker(centre, direction);
    }

    // =========================================================
    // Shade the entrance approach along its actual descent direction.
    private void DrawApproach(
        Vector2 centre, Vector2 direction, float width)
    {
        Vector2 across = new(-direction.Y, direction.X);
        float halfWidth = width * 0.32f;

        DrawColoredPolygon(new Vector2[]
        {
            centre - direction * 62f - across * halfWidth,
            centre - direction * 62f + across * halfWidth,
            centre + direction * 24f + across * halfWidth * 0.7f,
            centre + direction * 24f - across * halfWidth * 0.7f
        }, new Color("#353a3c"));

        // Bands darken toward the tunnel without adding collision.
        for (int i = 0; i < 4; i++)
        {
            float start = -48f + i * 16f;
            float end = start + 14f;
            float shade = 0.24f - i * 0.04f;

            DrawColoredPolygon(new Vector2[]
            {
                centre + direction * start - across * halfWidth * 0.8f,
                centre + direction * start + across * halfWidth * 0.8f,
                centre + direction * end + across * halfWidth * 0.72f,
                centre + direction * end - across * halfWidth * 0.72f
            }, new Color(shade, shade + 0.015f, shade + 0.02f));
        }
    }

    // =========================================================
    // Fill a doorway-shaped opening instead of a circular surface hole.
    private void DrawOpening(Vector2 centre, float width, float height)
    {
        const int segments = 16;
        Vector2[] opening = new Vector2[segments + 1];

        for (int i = 0; i <= segments; i++)
        {
            float angle = Mathf.Pi * i / segments;
            opening[i] = centre + new Vector2(
                Mathf.Cos(angle) * width * 0.30f,
                -Mathf.Sin(angle) * height * 0.73f);
        }

        DrawColoredPolygon(opening, Mouth);
    }

    // =========================================================
    // Build chunky arch stones with consistent, slightly uneven silhouettes.
    private void DrawArch(Vector2 centre, float width, float height)
    {
        const int stones = 9;

        for (int i = 0; i < stones; i++)
        {
            float start = Mathf.Pi * i / stones + 0.014f;
            float end = Mathf.Pi * (i + 1) / stones - 0.014f;
            float middle = (start + end) * 0.5f;

            float bulge = 1f + 0.035f * Mathf.Sin(i * 2.3f);
            float outerWidth = width * 0.5f * bulge;
            float outerHeight = height * bulge;

            Vector2[] stone = new Vector2[]
            {
                ArchPoint(centre, start, outerWidth, outerHeight),
                ArchPoint(centre, middle, outerWidth + 3f, outerHeight + 2f),
                ArchPoint(centre, end, outerWidth, outerHeight),
                ArchPoint(centre, end, width * 0.30f, height * 0.73f),
                ArchPoint(centre, start, width * 0.30f, height * 0.73f)
            };

            float shade = 0.34f + (i % 3) * 0.035f;
            DrawStone(stone, new Color(shade, shade + 0.025f, shade + 0.035f));

            // A narrow upper edge gives the blocks some visible thickness.
            DrawLine(stone[0], stone[1],
                new Color("#81898b"), 2f, true);
            DrawLine(stone[1], stone[2],
                new Color("#81898b"), 2f, true);
        }

        // Low footing stones ground the arch beside the mouth.
        DrawStone(new Vector2[]
        {
            centre + new Vector2(-width * 0.55f, -8f),
            centre + new Vector2(-width * 0.34f, -12f),
            centre + new Vector2(-width * 0.28f, 8f),
            centre + new Vector2(-width * 0.57f, 12f)
        }, new Color("#50585b"));

        DrawStone(new Vector2[]
        {
            centre + new Vector2(width * 0.33f, -11f),
            centre + new Vector2(width * 0.54f, -6f),
            centre + new Vector2(width * 0.58f, 11f),
            centre + new Vector2(width * 0.29f, 8f)
        }, new Color("#454e52"));
    }

    // =========================================================
    // Return one point on a camera-facing elliptical arch.
    private static Vector2 ArchPoint(
        Vector2 centre, float angle, float halfWidth, float height)
    {
        return centre + new Vector2(
            Mathf.Cos(angle) * halfWidth,
            -Mathf.Sin(angle) * height);
    }

    // =========================================================
    // Fill and outline one stone without introducing physics bodies.
    private void DrawStone(Vector2[] points, Color colour)
    {
        DrawColoredPolygon(points, colour);

        Vector2[] outline = new Vector2[points.Length + 1];
        System.Array.Copy(points, outline, points.Length);
        outline[points.Length] = points[0];

        DrawPolyline(outline, Outline, 2f, true);
    }

    // =========================================================
    // Optionally retain the entrance identity and tunnel direction for testing.
    private void DrawDebugMarker(Vector2 centre, Vector2 direction)
    {
        Color colour = new("#8be4cf");

        DrawLine(centre, centre + direction * 90f, colour, 2f, true);
        DrawCircle(centre + direction * 90f, 4f, colour);

        DrawString(
            ThemeDB.FallbackFont,
            centre + new Vector2(-40f, -ArchHeight - 16f),
            $"TO {WorldConfig.Find(this).GetLayerCatalog().Get(WorldLayerController.Find(this)?.Current == Connection.LowerLayer
                ? Connection.UpperLayer : Connection.LowerLayer).DisplayName}",
            HorizontalAlignment.Left, -1f, 16, colour);
    }
    #endregion
}
