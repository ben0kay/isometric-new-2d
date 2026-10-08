// Defines melee reach, damage, and attack cooldown for any melee enemy.
// Each enemy definition can supply different values, including larger attack reach.
using Godot;
using System;

[Tool, GlobalClass]
public partial class MeleeCombatSettings : EnemyCombatSettings
{
    #region Damage
    [ExportGroup("Damage")]
    [Export] public int Damage { get; set; } = 15;
    [Export] public DamageType DamageType { get; set; } = global::DamageType.Kinetic;
    #endregion

    #region Timing
    [ExportGroup("Timing")]
    [Export] public double Cooldown { get; set; } = 0.8;
    #endregion

    #region Validation
    // =========================================================
    // Validate shared distances and melee damage settings.
    public override void Validate(string enemyId)
    {
        base.Validate(enemyId);
        if (Damage < 0 || !double.IsFinite(Cooldown) || Cooldown < 0.1)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}' has invalid melee damage or cooldown.");
    }
    #endregion
}