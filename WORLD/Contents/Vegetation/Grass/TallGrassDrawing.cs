// Defines tall alien grass, available for future world generation.
using Godot;

public static class TallGrassDrawing
{
    #region Drawing
    // =========================================================
    // Bake a taller tuft with sweeping blades.
    public static void Draw(Node2D painter, int variant)
    {
        GrassDrawingHelpers.Draw(painter, variant, 46f, 24);
    }
    #endregion
}