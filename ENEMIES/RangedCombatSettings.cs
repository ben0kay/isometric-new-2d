// Defines ranged spacing and the attack resource used by the shared Weapon component.
// Retreat settings exist only on this ranged resource.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RangedCombatSettings : EnemyCombatSettings
{
    #region Positioning
    [ExportGroup("Positioning")]
    [Export] public float PreferredRange { get; set; } = 240f;
    [Export] public float BackAwayRange { get; set; } = 120f;
    #endregion

    #region Attack
    [ExportGroup("Attack")]
    [Export] public AttackDefinition Attack { get; set; }
    #endregion

    #region Initialization And Validation
    // =========================================================
    // Supply sensible ranged defaults for newly created resources.
    public RangedCombatSettings()
    {
        AttackRange = 360f;
        StopDistance = 24f;
    }

    // =========================================================
    // Validate the attack and enforce sensible retreat and firing distances.
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