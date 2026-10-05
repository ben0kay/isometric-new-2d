// Places a configured body of water at a draggable scene marker.
// Uses normal surface placement validation instead of searching random locations.
using Godot;

public partial class WaterPlacement : Marker2D
{
    #region Configuration
    [Export] public WaterDefinition Definition { get; set; }
    [Export] public float ShapePhase { get; set; } = 1.7f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Wait for shared world systems before evaluating the marker's location.
    public override void _Ready()
    {
        if (Definition == null)
        {
            GD.PushError($"Water marker '{Name}' requires a water definition.");
            SetProcess(false);
        }
    }

    // =========================================================
    // Generate once at the exact marker position and report placement failures.
    public override void _Process(double delta)
    {
        SurfaceWorld surfaces = SurfaceWorld.Find(this);
        if (surfaces == null) return;

        ChunkController chunks =
            surfaces.GetNode<ChunkController>("../ChunkController");
        if (!chunks.WorldReady) return;

        SetProcess(false);

        Vector2 centre = surfaces.WorldToTile(GlobalPosition);
        WaterPatch patch = surfaces.TryPlaceWater(
            Definition, centre, ShapePhase);

        if (patch == null)
        {
            GD.PushWarning(
                $"Water marker '{Name}' rejected at tile {centre}: " +
                "check height variation, chasms, world bounds or overlapping water.");
            return;
        }

        GD.Print($"Water marker '{Name}' placed at tile {centre}, " +
            $"world {GlobalPosition}.");
    }
    #endregion
}