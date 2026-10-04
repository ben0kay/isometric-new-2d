// Draws a clustered iron outcrop for one-time artwork baking.
// Broad irregular faces and mineral flecks avoid a radial triangle pattern.
using Godot;

public partial class IronOreDrawing : Node2D
{
    #region Drawing
    // =========================================================
    // Draw a soft footprint shadow and overlapping mineral-bearing stones.
    public override void _Draw()
    {
        DrawSetTransform(new Vector2(0, 8), 0f, new Vector2(1f, 0.32f));
        DrawCircle(Vector2.Zero, 62f, new Color(0.02f, 0.03f, 0.04f, 0.32f));
        DrawSetTransform(Vector2.Zero);

        DrawStone(new Vector2(-27, -7), 0.86f, new Color("#52616b"), 21);
        DrawStone(new Vector2(23, -4), 1.04f, new Color("#68737a"), 47);
        DrawStone(new Vector2(-1, 10), 0.76f, new Color("#475963"), 93);
        DrawStone(new Vector2(-48, 15), 0.28f, new Color("#56636a"), 12);
        DrawStone(new Vector2(45, 17), 0.32f, new Color("#5b666b"), 76);

        DrawSetTransform(Vector2.Zero);
    }

    // =========================================================
    // Draw an irregular block with broad faces, cracks, and baked mineral texture.
    private void DrawStone(Vector2 position, float size, Color color, ulong seed)
    {
        DrawSetTransform(position, 0f, Vector2.One * size);

        Vector2[] outline =
        {
            new(-30, -8), new(-19, -37), new(7, -44), new(31, -27),
            new(34, -1), new(18, 13), new(-13, 17), new(-31, 5)
        };
        Vector2[] top =
        {
            new(-30, -8), new(-19, -37), new(7, -44),
            new(31, -27), new(8, -9), new(-12, -4)
        };

        DrawColoredPolygon(outline, color.Darkened(0.32f));
        DrawColoredPolygon(top, color.Lightened(0.12f));
        DrawColoredPolygon(new Vector2[]
        {
            new(-30, -8), new(-12, -4), new(-13, 17), new(-31, 5)
        }, color.Darkened(0.15f));
        DrawColoredPolygon(new Vector2[]
        {
            new(-12, -4), new(8, -9), new(31, -27),
            new(34, -1), new(18, 13), new(-13, 17)
        }, color.Darkened(0.4f));

        using RandomNumberGenerator rng = new();
        rng.Seed = seed;

        for (int i = 0; i < 150; i++)
        {
            Vector2 point = new(
                rng.RandfRange(-30, 31), rng.RandfRange(-44, 0));
            if (!Geometry2D.IsPointInPolygon(point, top)) continue;

            Color fleck = i % 3 == 0
                ? new Color("#b17b4c") : color.Lightened(rng.RandfRange(0f, 0.3f));
            fleck.A = rng.RandfRange(0.25f, 0.8f);
            DrawCircle(point, rng.RandfRange(0.5f, 1.5f), fleck);
        }

        DrawPolyline(new Vector2[]
        {
            new(-17, -31), new(-7, -23), new(-11, -15),
            new(2, -10), new(4, 4)
        }, new Color("#b48255"), 2.1f, true);

        DrawPolyline(new Vector2[]
        {
            new(17, -29), new(11, -22), new(18, -14)
        }, new Color("#c39a6b"), 1.3f, true);

        DrawPolyline(new Vector2[]
        {
            new(-27, -7), new(-12, -3), new(-15, 11)
        }, new Color(0.06f, 0.08f, 0.09f, 0.65f), 1.2f, true);
    }
    #endregion
}