// Holds any entity stationary for a recovery or anticipation period.
using Godot;
using System;

[Tool, GlobalClass]
public partial class WaitEnemyAction : EnemyAction
{
    #region Configuration
    [ExportGroup("Timing")]
    [Export] public double Duration { get; set; } = 0.5;
    #endregion

    #region Execution
    // =========================================================
    // Stop movement when the waiting step begins.
    public override EntityActionResult Begin(
        EntitySequence runner, ref EntityActionState state)
    {
        runner.Motor.Stop();
        return EntityActionResult.Running;
    }

    // =========================================================
    // Complete after the configured duration.
    public override EntityActionResult Tick(
        EntitySequence runner, ref EntityActionState state, double delta)
    {
        return state.Elapsed >= Duration
            ? EntityActionResult.Completed : EntityActionResult.Running;
    }

    // =========================================================
    // Allow zero-duration waits but reject invalid durations.
    public override void Validate()
    {
        base.Validate();

        if (!double.IsFinite(Duration) || Duration < 0.0)
            throw new InvalidOperationException(
                "Wait Duration cannot be negative.");
    }
    #endregion
}