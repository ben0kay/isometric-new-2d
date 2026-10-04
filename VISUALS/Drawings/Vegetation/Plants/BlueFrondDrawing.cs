// Draws broad alien fronds with dark rear leaves and curved blue-grey foreground blades.
// Variant-specific proportions are captured once in the vegetation atlas.
using Godot;

public static class BlueFrondDrawing
{
    #region Drawing
    // =========================================================
    // Build an asymmetric layered crown with space between its main leaves.
    public static void Draw(Node2D painter, int variant)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(17011 + variant * 977);
        float lean = rng.RandfRange(-12f, 12f);

        for (int i = 0; i < 7; i++)
        {
            float spread = (i - 3) / 3f;
            Vector2 start = new(rng.RandfRange(-5, 5), -3);
            Vector2 end = new(
                spread * rng.RandfRange(65, 87) + lean,
                -rng.RandfRange(125, 180) + Mathf.Abs(spread) * 46f);
            Vector2 control = new(
                end.X * 0.3f + lean,
                end.Y * rng.RandfRange(0.78f, 1.02f));

            Color colour = new Color("#385c6c").Lerp(
                new Color("#587b88"), rng.RandfRange(0.1f, 0.6f));
            if (i < 2) colour = colour.Darkened(0.22f);

            painter.DrawLine(start, control * 0.4f,
                new Color("#17242b"), 3f, true);
            VegetationDrawingHelpers.Leaf(
                painter, start, control, end,
                rng.RandfRange(13, 20), colour, i + variant * 2f);
        }

        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(-3, -2),
            new Vector2(-51, -73), new Vector2(-78, -35),
            17f, new Color("#365563"), variant + 9f);
        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(4, -3),
            new Vector2(49, -84), new Vector2(72, -48),
            18f, new Color("#476a77"), variant + 12f);

        painter.DrawCircle(new Vector2(0, -3), 4f, new Color("#17252a"));
    }
    #endregion
}