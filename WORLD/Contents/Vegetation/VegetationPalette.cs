// Defines coherent vegetation palettes independently of plant geometry.
// Each palette mixes related leaf colours rather than random rainbow shades.
using Godot;

public static class VegetationPalette
{
    #region Configuration
    public const int ShapeCount = 4;
    public const int PaletteCount = 4;

    private static readonly Color[] Main =
    {
        new("#426779"), // Slate blue.
        new("#595078"), // Indigo-purple.
        new("#436f76"), // Muted cyan.
        new("#554369")  // Plum.
    };

    private static readonly Color[] Secondary =
    {
        new("#514b70"),
        new("#3c6578"),
        new("#455773"),
        new("#4c647d")
    };

    private static readonly Color[] Accent =
    {
        new("#83aeb8"),
        new("#9c89af"),
        new("#78b4b4"),
        new("#899bb6")
    };
    #endregion

    #region Selection
    // =========================================================
    // Pick related shades with occasional restrained accent leaves.
    public static Color GetLeaf(int palette, float mixture, bool accent = false)
    {
        palette = Mathf.Clamp(palette, 0, PaletteCount - 1);
        Color colour = Main[palette].Lerp(
            Secondary[palette], Mathf.Clamp(mixture, 0f, 1f));
        return accent ? colour.Lerp(Accent[palette], 0.45f) : colour;
    }
    #endregion
}