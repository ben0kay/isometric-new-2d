// Registers an optional world marker for the debug map.
// Positions use logical world coordinates without artwork height offsets.
using Godot;

public enum DebugMapPoiKind
{
    CaveEntrance,
    Landmark,
    Resource,
    Settlement
}

public partial class DebugMapPoi : Node2D
{
    #region Configuration
    [Export] public string DisplayName { get; set; } = "Point of interest";
    [Export] public DebugMapPoiKind Kind { get; set; }
        = DebugMapPoiKind.Landmark;
    #endregion

    #region Lifecycle
    // =========================================================
    // Make this marker available to nearby debug-map queries.
    public override void _EnterTree()
    {
        AddToGroup("debug_map_poi");
    }

    // =========================================================
    // Keep markers passive.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion
}