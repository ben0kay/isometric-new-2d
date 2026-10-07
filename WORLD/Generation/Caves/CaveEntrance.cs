// Draws a readable test entrance at the surface's actual elevation.
// Traversal is coordinated centrally by WorldLayerController.
using Godot;

public partial class CaveEntrance : Node2D
{
    #region State
    public CaveWorld World { get; set; }
    #endregion

    #region Drawing
    // =========================================================
    // Mark the mouth and indicate the descending tunnel direction.
    public override void _Draw()
    {
        if (World == null) return;

        Vector2 centre = Vector2.Up * World.RimHeight;
        DrawCircle(centre, 40f, new Color("#080d12"));
        DrawArc(centre, 42f, 0f, Mathf.Tau, 48,
            new Color("#8be4cf"), 3f, true);

        Vector2 direction = IsoGrid.TileToWorld(
            Vector2.Right, World.TileSize).Normalized();

        DrawLine(centre, centre + direction * 90f,
            new Color("#8be4cf"), 4f, true);
        DrawCircle(centre + direction * 90f, 6f,
            new Color("#8be4cf"));
    }
    #endregion
}