// Draws dense alien shrubs with curled blue-grey leaves and dark woody stems.
// Layered branches create a different silhouette from the taller fronds.
using Godot;

public static class AlienShrubDrawing
{
    #region Drawing
    // =========================================================
    // Build several branching crowns with overlapping curved leaves.
    public static void Draw(Node2D painter, int variant)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(33179 + variant * 1237);

        for (int branch = 0; branch < 5; branch++)
        {
            float side = (branch - 2) / 2f;
            Vector2 crown = new(
                side * rng.RandfRange(32, 52),
                -rng.RandfRange(45, 100));
            Vector2 bend = new(crown.X * 0.3f, crown.Y * 0.65f);

            painter.DrawPolyline(new Vector2[]
            {
                new(0, 0), bend, crown
            }, new Color("#172329"), 5f, true);

            painter.DrawPolyline(new Vector2[]
            {
                new(-1, -1), bend + new Vector2(-1, 0),
                crown + new Vector2(-1, 0)
            }, new Color("#34454d"), 1.2f, true);

            for (int leaf = 0; leaf < 5; leaf++)
            {
                float direction = (leaf - 2) / 2f;
                Vector2 start = crown + new Vector2(0, rng.RandfRange(0, 12));
                Vector2 end = crown + new Vector2(
                    direction * rng.RandfRange(25, 43),
                    -rng.RandfRange(20, 53));
                Vector2 control = crown + new Vector2(
                    direction * 42f, -rng.RandfRange(48, 66));

                Color colour = new Color("#304b5d").Lerp(
                    new Color("#688796"), rng.RandfRange(0.1f, 0.65f));
                VegetationDrawingHelpers.Leaf(
                    painter, start, control, end,
                    rng.RandfRange(8, 13), colour, branch * 3f + leaf);

                if (leaf == 2)
                    painter.DrawLine(end, end.Lerp(control, 0.12f),
                        new Color("#84b8bf"), 1f, true);
            }
        }

        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(-2, -2), new Vector2(-35, -40),
            new Vector2(-57, -18), 12f, new Color("#456172"), variant);
        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(3, -3), new Vector2(32, -46),
            new Vector2(55, -25), 13f, new Color("#577887"), variant + 1);
    }
    #endregion
}