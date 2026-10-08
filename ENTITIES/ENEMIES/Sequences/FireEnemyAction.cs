// Fires one attack through the existing Weapon component.
// Waits briefly for cooldown, range, and sight rather than bypassing weapon rules.
using Godot;
using System;

[Tool, GlobalClass]
public partial class FireEnemyAction : EnemyAction
{
    #region Configuration
    [ExportGroup("Attack")]
    [Export] public AttackDefinition Attack { get; set; }

    [ExportGroup("Timing")]
    [Export] public double MaximumWait { get; set; } = 2.0;
    #endregion

    #region Execution
    // =========================================================
    // Hold position while waiting for this firing step.
    public override EnemyActionResult Begin(
        EnemySequence runner, ref EnemyActionState state)
    {
        runner.Motor.Stop();
        return EnemyActionResult.Running;
    }

    // =========================================================
    // Fire when eligible or fail if the step waits too long.
    public override EnemyActionResult Tick(
        EnemySequence runner, ref EnemyActionState state, double delta)
    {
        if (runner.TryFire(Attack)) return EnemyActionResult.Completed;
        return state.Elapsed >= MaximumWait
            ? EnemyActionResult.Failed : EnemyActionResult.Running;
    }

    // =========================================================
    // Require a finite timeout for blocked or cooling-down attacks.
    public override void Validate()
    {
        base.Validate();
        if (!double.IsFinite(MaximumWait) || MaximumWait <= 0.0)
            throw new InvalidOperationException("Fire MaximumWait must be positive.");
    }
    #endregion
}