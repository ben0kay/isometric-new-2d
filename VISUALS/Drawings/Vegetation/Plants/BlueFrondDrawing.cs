// Draws tall slate-blue fronds with charcoal stems and asymmetric curved blades.
// Each variant is deterministic and captured once by VegetationAtlas.
using Godot;

public static class BlueFrondDrawing
{
    #region Drawing
    // =========================================================
    // Build a layered fan of broad alien foliage around a grounded crown.
    public static void Draw(Node2D painter, int variant)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(17011 + variant * 977);

        for (int i = 0; i < 11; i++)
        {
            float spread = (i - 5) / 5f;
            Vector2 start = new(rng.RandfRange(-7, 7), rng.RandfRange(-6, 0));
            Vector2 end = new(
                spread * rng.RandfRange(70, 94),
                -rng.RandfRange(100, 182) + Mathf.Abs(spread) * 48f);
            Vector2 control = new(
                end.X * rng.RandfRange(0.2f, 0.6f),
                end.Y * rng.RandfRange(0.7f, 1.05f));

            painter.DrawLine(start, control * 0.45f,
                new Color("#18272e"), 3f, true);

            Color colour = new Color("#385e73").Lerp(
                new Color("#7193a4"), rng.RandfRange(0.05f, 0.5f));
            if (i < 4) colour = colour.Darkened(0.18f);

            VegetationDrawingHelpers.Leaf(
                painter, start, control, end,
                rng.RandfRange(9f, 16f), colour, i + variant * 2f);
        }

        for (int i = 0; i < 4; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            VegetationDrawingHelpers.Leaf(
                painter, new Vector2(side * 3, -2),
                new Vector2(side * 43, -48),
                new Vector2(side * (48 + i * 8), -24 - i * 9),
                10f, new Color("#456778"), i * 1.7f);
        }

        painter.DrawCircle(new Vector2(0, -3), 5f, new Color("#1c2c32"));
    }
    #endregion
}