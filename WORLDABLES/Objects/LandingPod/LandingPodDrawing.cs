// Draws a temporary sci-fi landing pod into its assigned bake viewport.
// The painter is discarded after capture; gameplay uses the cached texture.
using Godot;

public partial class LandingPodDrawing : Node2D
{
    #region Drawing
    // =========================================================
    // Draw separate lit roof and wall surfaces with small mechanical details.
    public override void _Draw()
    {
        Face(new Color("#172631"),
            new(40, 126), new(157, 66), new(281, 128), new(163, 190));
        Face(new Color("#7d9196"),
            new(48, 101), new(157, 45), new(273, 103), new(162, 161));
        Face(new Color("#354953"),
            new(48, 101), new(162, 161), new(162, 195), new(48, 135));
        Face(new Color("#21333e"),
            new(162, 161), new(273, 103), new(273, 138), new(162, 195));

        Face(new Color("#314751"),
            new(84, 83), new(157, 45), new(234, 83), new(161, 121));
        Face(new Color("#132a36"),
            new(113, 80), new(156, 58), new(204, 82), new(161, 103));
        Face(new Color("#43859a"),
            new(122, 79), new(156, 63), new(188, 79), new(157, 95));

        Face(new Color("#12232d"),
            new(74, 123), new(120, 147), new(120, 171), new(74, 147));
        Face(new Color("#152630"),
            new(184, 160), new(246, 128), new(246, 152), new(184, 184));

        Face(new Color("#18252d"),
            new(58, 141), new(78, 151), new(78, 174), new(58, 164));
        Face(new Color("#18252d"),
            new(141, 184), new(160, 194), new(160, 216), new(141, 206));
        Face(new Color("#14212a"),
            new(255, 147), new(273, 138), new(273, 160), new(255, 169));

        DrawLine(new(48, 101), new(162, 161), new Color("#bac4bd"), 3f, true);
        DrawLine(new(84, 83), new(161, 121), new Color("#789398"), 2f, true);
        DrawLine(new(83, 133), new(112, 148), new Color("#68d5e1"), 4f, true);
        DrawLine(new(194, 164), new(235, 143), new Color("#dca95f"), 4f, true);
        DrawLine(new(174, 165), new(265, 118), new Color("#52707a"), 2f, true);
        DrawLine(new(254, 80), new(254, 38), new Color("#789299"), 4f, true);
        DrawCircle(new(254, 38), 4f, new Color("#76deea"));

        SetProcess(false);
    }

    // =========================================================
    // Fill and outline one rigid surface without generating triangle-fan artwork.
    private void Face(Color color, params Vector2[] points)
    {
        DrawColoredPolygon(points, color);
        for (int i = 0; i < points.Length; i++)
            DrawLine(points[i], points[(i + 1) % points.Length],
                new Color("#192a34"), 2f, true);
    }
    #endregion
}