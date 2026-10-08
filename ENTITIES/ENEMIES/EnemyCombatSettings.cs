// Stores all shared enemy awareness and combat distances in one tuning resource.
// Melee and ranged resources add their relevant attack settings.
using Godot;
using System;

[Tool, GlobalClass]
public partial class EnemyCombatSettings : Resource
{
    #region Ranges
    [ExportGroup("Ranges")]

    // Maximum distance for acquiring a visible target.
    [Export] public float DetectionRange { get; set; } = 650f;

    // Maximum distance for retaining an acquired target.
    [Export] public float ForgetRange { get; set; } = 1000f;

    // Maximum distance at which an attack is allowed.
    [Export] public float AttackRange { get; set; } = 34f;

    // Arrival tolerance used by navigation movement goals.
    [Export] public float StopDistance { get; set; } = 28f;
    #endregion

    #region Validation
    // =========================================================
    // Validate awareness distances and ensure arrival is within attack reach.
    public virtual void Validate(string enemyId)
    {
        if (!float.IsFinite(DetectionRange) ||
            !float.IsFinite(ForgetRange) ||
            DetectionRange <= 0f || ForgetRange < DetectionRange)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}': DetectionRange must be positive " +
                "and ForgetRange must be at least DetectionRange.");

        if (!float.IsFinite(AttackRange) ||
            !float.IsFinite(StopDistance) ||
            AttackRange <= 0f || StopDistance < 1f ||
            StopDistance >= AttackRange)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}': StopDistance must be at least 1 " +
                "and below AttackRange.");
    }
    #endregion
}