// Defines shared attack distances without storing runtime combat state.
// Concrete melee and ranged resources add only their relevant settings.
using Godot;
using System;

[Tool, GlobalClass]
public partial class EnemyCombatSettings : Resource
{
    #region Distances
    [ExportGroup("Distances")]
    [Export] public float AttackRange { get; set; } = 34f;
    [Export] public float StopDistance { get; set; } = 28f;
    #endregion

    #region Validation
    // =========================================================
    // Ensure an enemy stops within its attack range.
    public virtual void Validate(string enemyId)
    {
        if (!float.IsFinite(AttackRange) || !float.IsFinite(StopDistance) ||
            AttackRange <= 0f || StopDistance < 1f || StopDistance >= AttackRange)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}': StopDistance must be at least 1 and below AttackRange.");
    }
    #endregion
}