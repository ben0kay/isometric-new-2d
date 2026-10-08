// Defines ranged distance bands and the attack resource.
// Each enemy caches its own chosen distance within the band.
using Godot;
using System;

[Tool, GlobalClass]
public partial class RangedCombatSettings : EnemyCombatSettings
{
    #region Ranges
    [ExportGroup("Ranges")]

    // Minimum distance in the preferred positioning band.
    [Export] public float PreferredRange { get; set; } = 240f;

    // Begin retreating below this distance.
    [Export] public float BackAwayRange { get; set; } = 120f;

    // Higher values favour PreferredRange over AttackRange.
    [Export(PropertyHint.Range, "1,8,0.25,or_greater")]
    public float RangeBias { get; set; } = 3f;
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
    // Validate the attack, distance band and positioning bias.
    public override void Validate(string enemyId)
    {
        base.Validate(enemyId);

        if (Attack == null || !float.IsFinite(BackAwayRange) ||
            !float.IsFinite(PreferredRange) || BackAwayRange < 0f ||
            PreferredRange <= BackAwayRange || PreferredRange > AttackRange ||
            !float.IsFinite(RangeBias) || RangeBias < 1f)
            throw new InvalidOperationException(
                $"Enemy '{enemyId}' requires an attack, " +
                "0 <= BackAwayRange < PreferredRange <= AttackRange, " +
                "and RangeBias >= 1.");
    }
    #endregion
}