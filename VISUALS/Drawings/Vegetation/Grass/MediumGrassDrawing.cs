// Defines medium-height alien grass using the shared blade drawing code.
using Godot;

public static class MediumGrassDrawing
{
    #region Drawing
    // =========================================================
    // Bake a slightly fuller tuft with longer blades.
    public static void Draw(Node2D painter, int variant)
    {
        GrassDrawingHelpers.Draw(painter, variant, 28f, 21);
    }
    #endregion
}