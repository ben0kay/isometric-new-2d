// Draws a primitive alien grazer for the shared artwork baker.
// Gameplay never depends on this placeholder's shape.
using Godot;

public partial class TallowbackDrawing : Node2D
{
    // =========================================================
    // Layer distant legs, body, fatty back, head and near legs.
    public override void _Draw()
    {
        Color dark = new("#283a38");
        Color hide = new("#55665b");
        Color light = new("#8a9276");

        DrawColoredPolygon(new Vector2[]
        {
            new(-35,-30), new(-28,-29), new(-28,-3), new(-39,-3)
        }, dark);
        DrawColoredPolygon(new Vector2[]
        {
            new(25,-30), new(33,-27), new(38,-2), new(26,-2)
        }, dark);

        DrawColoredPolygon(new Vector2[]
        {
            new(-38,-35), new(-65,-31), new(-73,-23),
            new(-60,-27), new(-35,-26)
        }, hide);

        Ellipse(new(-7,-34), new(47,24), hide);
        Ellipse(new(-15,-47), new(35,17), light);
        Ellipse(new(-8,-43), new(29,11), new Color("#a0a58a"));

        DrawColoredPolygon(new Vector2[]
        {
            new(22,-41), new(47,-45), new(65,-35),
            new(71,-22), new(48,-17), new(25,-24)
        }, hide);
        DrawColoredPolygon(new Vector2[]
        {
            new(44,-42), new(62,-34), new(55,-28), new(36,-32)
        }, light);

        DrawColoredPolygon(new Vector2[]
        {
            new(-28,-23), new(-15,-22), new(-18,1), new(-33,1)
        }, new Color("#43564e"));
        DrawColoredPolygon(new Vector2[]
        {
            new(22,-23), new(34,-20), new(41,2), new(26,2)
        }, new Color("#43564e"));

        DrawColoredPolygon(new Vector2[]
        {
            new(34,-42), new(37,-57), new(43,-43)
        }, new Color("#adb29b"));
        DrawColoredPolygon(new Vector2[]
        {
            new(-35,-48), new(-31,-60), new(-24,-50)
        }, new Color("#707c6b"));

        DrawCircle(new(55,-33), 3.2f, dark);
        DrawCircle(new(56,-34), 1.3f, new Color("#a9d0c0"));
        DrawLine(new(60,-22), new(69,-23), dark, 2f);
        DrawLine(new(-26,-45), new(-10,-38), new Color("#727d69"), 3f);
        DrawLine(new(-9,-48), new(7,-40), new Color("#727d69"), 3f);
    }

    // =========================================================
    // Approximate an ellipse using a small reusable polygon pattern.
    private void Ellipse(Vector2 centre, Vector2 radius, Color colour)
    {
        Vector2[] points = new Vector2[20];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = Mathf.Tau * i / points.Length;
            points[i] = centre + new Vector2(
                Mathf.Cos(angle) * radius.X,
                Mathf.Sin(angle) * radius.Y);
        }
        DrawColoredPolygon(points, colour);
    }
}