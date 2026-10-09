// Stores an ordered action sequence that any entity can use.
// Each entity's runner owns its progress and repeat cooldown.
using Godot;
using System;

[Tool, GlobalClass]
public partial class EntitySequenceDefinition : Resource
{
    #region Configuration
    [ExportGroup("Sequence")]
    [Export] public Godot.Collections.Array<EntityAction> Actions
        { get; set; } = new();

    [ExportGroup("Timing")]
    [Export] public double Cooldown { get; set; } = 0.75;
    #endregion

    #region Validation
    // =========================================================
    // Validate the sequence and every action before execution.
    public void Validate()
    {
        if (Actions == null || Actions.Count == 0 ||
            !double.IsFinite(Cooldown) || Cooldown < 0.1)
            throw new InvalidOperationException(
                "A sequence requires actions and a cooldown of at least 0.1 seconds.");

        foreach (EntityAction action in Actions)
        {
            if (action == null)
                throw new InvalidOperationException(
                    "Sequence actions cannot be empty.");

            action.Validate();
        }
    }
    #endregion
}