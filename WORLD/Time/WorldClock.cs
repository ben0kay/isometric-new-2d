// Tracks the repeating twilight/eclipse cycle independently of world layers.
// Cycle durations come from the world's shared CONFIG node.
using Godot;
using System;

public partial class WorldClock : Node
{
    #region State
    public double ElapsedSeconds { get; private set; }
    public double CycleSeconds { get; private set; }
    public float Phase { get; private set; }
    public float EclipseFraction { get; private set; }

    private WorldConfig _config;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the shared settings without depending on sibling ready order.
    public override void _Ready()
    {
        _config = WorldConfig.Find(this);
        UpdateCycle();
    }

    // =========================================================
    // Keep time advancing underground, while respecting normal game pause.
    public override void _Process(double delta)
    {
        ElapsedSeconds += Math.Max(0.0, delta);
        UpdateCycle();
    }
    #endregion

    #region Timing
    // =========================================================
    // Derive both phases from the two global duration settings.
    private void UpdateCycle()
    {
        double daylight = Math.Max(0.1, _config.DayDurationMinutes) * 60.0;
        double eclipse = Math.Max(0.0, _config.EclipseDurationMinutes) * 60.0;

        CycleSeconds = daylight + eclipse;
        Phase = (float)((ElapsedSeconds % CycleSeconds) / CycleSeconds);
        EclipseFraction = (float)(eclipse / CycleSeconds);
    }
    #endregion
}