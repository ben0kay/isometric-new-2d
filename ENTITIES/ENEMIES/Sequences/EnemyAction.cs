// Defines a reusable sequence action and its per-instance execution state.
// Shared resources never store timers, targets, or movement progress.
using Godot;
using System;

public enum EnemyActionResult { Running, Completed, Failed }
public enum EnemyActionFailure { AbortSequence, SkipAction }

public struct EnemyActionState
{
    public double Elapsed;
    public Vector2 Destination;
}

[Tool, GlobalClass]
public partial class EnemyAction : Resource
{
    #region Failure
    [ExportGroup("Failure")]
    [Export] public EnemyActionFailure OnFailure { get; set; }
    #endregion

    #region Execution
    // =========================================================
    // Initialize an action using state owned by the sequence runner.
    public virtual EnemyActionResult Begin(
        EnemySequence runner, ref EnemyActionState state)
    {
        return EnemyActionResult.Running;
    }

    // =========================================================
    // Advance one action; concrete action types supply their behaviour.
    public virtual EnemyActionResult Tick(
        EnemySequence runner, ref EnemyActionState state, double delta)
    {
        return EnemyActionResult.Failed;
    }

    // =========================================================
    // Reject a bare base resource or an invalid failure setting.
    public virtual void Validate()
    {
        if (GetType() == typeof(EnemyAction) ||
            !Enum.IsDefined(typeof(EnemyActionFailure), OnFailure))
            throw new InvalidOperationException(
                "A sequence requires concrete actions with valid failure settings.");
    }
    #endregion
}