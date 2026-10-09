// Preserves existing action script references during migration.
// Shared execution state and behaviour contract live in EntityAction.
using Godot;
using System;

[Tool, GlobalClass]
public partial class EnemyAction : EntityAction
{
    #region Validation
    // =========================================================
    // Prevent the compatibility base from being used as a concrete action.
    public override void Validate()
    {
        base.Validate();

        if (GetType() == typeof(EnemyAction))
            throw new InvalidOperationException(
                "A sequence requires a concrete action.");
    }
    #endregion
}