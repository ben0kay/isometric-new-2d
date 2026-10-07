// Adds ranged spacing to the shared Ranges group.
// The attack resource controls the weapon independently of movement.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RangedCombatSettings : EnemyCombatSettings
{
    #region Ranges
    [ExportGroup("Ranges")]

    // Stop approaching at this distance when sight is clear.
    [Export] public float PreferredRange { get; set; } = 240f;

    // Begin retreating below this distance.
    [Export] public float BackAwayRange { get; set; } = 120f;
    #endregion

    #region Attack
    [ExportGroup("Attack")]
    [Export] public AttackDefinition Attack { get; set; }
    #endregion

    #region Initialization And Validation
    // =========================================================
    // Supply the existing ranged defaults for newly created resources.
    public RangedCombatSettings()
    {
        AttackRange = 360f;
        StopDistance = 24f;
    }

    // =========================================================
    // Require an attack and sensible retreat and preferred distances.
    public override void Validate(string enemyId)
    {
        base.Validate(enemyId);

        if (Attack == null || !float.IsFinite(BackAwayRange) ||
            !float.IsFinite(PreferredRange) || BackAwayRange < 0f ||
            PreferredRange <= BackAwayRange || PreferredRange > AttackRange)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}' requires an attack and " +
                "0 <= BackAwayRange < PreferredRange <= AttackRange.");
    }
    #endregion
}