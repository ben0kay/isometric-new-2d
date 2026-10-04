// Draws low spreading alien shrubs with charcoal branches and curled blue foliage.
// Uses fewer crowns so individual leaves remain readable against the ground.
using Godot;

public static class AlienShrubDrawing
{
    #region Drawing
    // =========================================================
    // Build three irregular branching crowns with broad drooping leaves.
    public static void Draw(Node2D painter, int variant)
    {
        using RandomNumberGenerator rng = new();
        rng.Seed = (ulong)(33179 + variant * 1237);

        for (int branch = 0; branch < 3; branch++)
        {
            float side = branch - 1;
            Vector2 crown = new(
                side * rng.RandfRange(33, 48),
                -rng.RandfRange(40, 65));
            Vector2 bend = new(crown.X * 0.45f, crown.Y * 0.6f);

            painter.DrawPolyline(
                new Vector2[] { new(0, 0), bend, crown },
                new Color("#142127"), 5f, true);
            painter.DrawPolyline(new Vector2[]
            {
                new(-1, -1), bend + new Vector2(-1, 0),
                crown + new Vector2(-1, 0)
            }, new Color("#2c3e45"), 1f, true);

            for (int leaf = 0; leaf < 4; leaf++)
            {
                float direction = (leaf - 1.5f) / 1.5f;
                Vector2 start = crown + new Vector2(0, 5);
                Vector2 control = crown + new Vector2(
                    direction * 40f, -rng.RandfRange(37, 57));
                Vector2 end = crown + new Vector2(
                    direction * rng.RandfRange(33, 47),
                    -rng.RandfRange(7, 27));

                Color colour = new Color("#314b59").Lerp(
                    new Color("#527481"), rng.RandfRange(0.1f, 0.65f));
                VegetationDrawingHelpers.Leaf(
                    painter, start, control, end,
                    rng.RandfRange(12, 18), colour,
                    branch * 3f + leaf + variant);
            }
        }

        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(-2, -3),
            new Vector2(-41, -45), new Vector2(-65, -15),
            16f, new Color("#35525f"), variant + 14);
        VegetationDrawingHelpers.Leaf(
            painter, new Vector2(3, -3),
            new Vector2(39, -48), new Vector2(62, -22),
            17f, new Color("#456571"), variant + 18);
    }
    #endregion
}