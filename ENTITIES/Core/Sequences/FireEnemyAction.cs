// Fires an optional attack through any entity sequence's weapon.
// Respects weapon cooldown, range and sight.
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
    // Hold position while waiting for this attack step.
    public override EntityActionResult Begin(
        EntitySequence runner, ref EntityActionState state)
    {
        runner.Motor.Stop();
        return EntityActionResult.Running;
    }

    // =========================================================
    // Complete after firing or fail when the maximum wait expires.
    public override EntityActionResult Tick(
        EntitySequence runner, ref EntityActionState state, double delta)
    {
        if (runner.TryFire(Attack)) return EntityActionResult.Completed;

        return state.Elapsed >= MaximumWait
            ? EntityActionResult.Failed : EntityActionResult.Running;
    }

    // =========================================================
    // Require a finite positive timeout.
    public override void Validate()
    {
        base.Validate();

        if (!double.IsFinite(MaximumWait) || MaximumWait <= 0.0)
            throw new InvalidOperationException(
                "Fire MaximumWait must be positive.");
    }
    #endregion
}