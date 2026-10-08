// Defines an ordered reusable action sequence and its repeat cooldown.
// Per-enemy progress and cooldowns live in EnemySequence.
using Godot;
using System;

[Tool, GlobalClass]
public partial class EnemySequenceDefinition : Resource
{
    #region Configuration
    [ExportGroup("Sequence")]
    [Export] public Godot.Collections.Array<EnemyAction> Actions
        { get; set; } = new();

    [ExportGroup("Timing")]
    [Export] public double Cooldown { get; set; } = 0.75;
    #endregion

    #region Validation
    // =========================================================
    // Validate all actions before the enemy becomes active.
    public void Validate()
    {
        if (Actions == null || Actions.Count == 0 ||
            !double.IsFinite(Cooldown) || Cooldown < 0.1)
            throw new InvalidOperationException(
                "A sequence requires actions and a cooldown of at least 0.1 seconds.");

        foreach (EnemyAction action in Actions)
        {
            if (action == null)
                throw new InvalidOperationException("Sequence actions cannot be empty.");
            action.Validate();
        }
    }
    #endregion
}