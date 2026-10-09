// Defines reusable entity actions and per-actor execution state.
// Shared action resources never store targets, timers or movement progress.
using Godot;
using System;

public enum EntityActionResult { Running, Completed, Failed }
public enum EntityActionFailure { AbortSequence, SkipAction }

public struct EntityActionState
{
    public double Elapsed;
    public Vector2 Destination;
}

[Tool, GlobalClass]
public partial class EntityAction : Resource
{
    #region Failure
    [ExportGroup("Failure")]
    [Export] public EntityActionFailure OnFailure { get; set; }
    #endregion

    #region Execution
    // =========================================================
    // Initialize this action using state owned by the entity's runner.
    public virtual EntityActionResult Begin(
        EntitySequence runner, ref EntityActionState state)
    {
        return EntityActionResult.Running;
    }

    // =========================================================
    // Advance the action; concrete action types supply their behaviour.
    public virtual EntityActionResult Tick(
        EntitySequence runner, ref EntityActionState state, double delta)
    {
        return EntityActionResult.Failed;
    }

    // =========================================================
    // Reject bare action resources and invalid failure settings.
    public virtual void Validate()
    {
        if (GetType() == typeof(EntityAction) ||
            !Enum.IsDefined(typeof(EntityActionFailure), OnFailure))
            throw new InvalidOperationException(
                "A sequence requires concrete actions with valid failure settings.");
    }
    #endregion
}