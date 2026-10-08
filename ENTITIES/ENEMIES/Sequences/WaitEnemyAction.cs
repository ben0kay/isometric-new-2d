// Holds an enemy stationary for a configurable recovery or anticipation period.
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
    // Stop movement when this waiting step begins.
    public override EnemyActionResult Begin(
        EnemySequence runner, ref EnemyActionState state)
    {
        runner.Motor.Stop();
        return EnemyActionResult.Running;
    }

    // =========================================================
    // Complete after the configured duration.
    public override EnemyActionResult Tick(
        EnemySequence runner, ref EnemyActionState state, double delta)
    {
        return state.Elapsed >= Duration
            ? EnemyActionResult.Completed : EnemyActionResult.Running;
    }

    // =========================================================
    // Allow zero-duration waits but reject invalid durations.
    public override void Validate()
    {
        base.Validate();
        if (!double.IsFinite(Duration) || Duration < 0.0)
            throw new InvalidOperationException("Wait Duration cannot be negative.");
    }
    #endregion
}