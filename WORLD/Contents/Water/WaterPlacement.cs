// Describes a water basin at an editor marker before terrain generation starts.
using Godot;

public partial class WaterPlacement : Marker2D
{
    #region Configuration
    [Export] public WaterDefinition Definition { get; set; }
    [Export] public float ShapePhase { get; set; } = 1.7f;
    [Export(PropertyHint.Range, "0,1,0.01")]
    public float InitialFill { get; set; } = 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Make this marker discoverable before the world generator initializes.
    public override void _EnterTree()
    {
        AddToGroup("water_placements");
        SetProcess(false);
    }

    // =========================================================
    // Report incomplete markers without starting a late placement search.
    public override void _Ready()
    {
        if (Definition == null)
            GD.PushError($"Basin marker '{Name}' requires a water definition.");
        SetProcess(false);
    }
    #endregion
}