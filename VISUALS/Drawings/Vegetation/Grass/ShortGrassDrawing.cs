// Defines low alien grass independently from medium and tall grass.
using Godot;

public static class ShortGrassDrawing
{
    #region Drawing
    // =========================================================
    // Bake a low, fine-bladed tuft.
    public static void Draw(Node2D painter, int variant)
    {
        GrassDrawingHelpers.Draw(painter, variant, 13f, 18);
    }
    #endregion
}