// Draws one paired surface hole and its descending direction.
// The layer controller handles traversal and destination readiness.
using Godot;

public partial class CaveEntrance : Node2D
{
    #region State
    public CaveWorld World { get; set; }
    public CaveHole Hole { get; set; }
    #endregion

    #region Drawing
    // =========================================================
    // Mark the opening at its own surface elevation.
    public override void _Draw()
    {
        if (World == null || Hole == null) return;

        Color colour = new("#8be4cf");
        Vector2 centre = Vector2.Up * Hole.RimHeight;
        DrawCircle(centre, 40f, new Color("#080d12"));
        DrawArc(centre, 42f, 0f, Mathf.Tau, 48, colour, 3f, true);

        Vector2 direction = IsoGrid.TileToWorld(
            Hole.Direction, World.TileSize).Normalized();

        DrawLine(centre, centre + direction * 90f, colour, 4f, true);
        DrawCircle(centre + direction * 90f, 6f, colour);

        DrawString(
            ThemeDB.FallbackFont, centre + new Vector2(-32, -52),
            $"HOLE {Hole.Id}", HorizontalAlignment.Left, -1f, 18, colour);
    }
    #endregion
}