// Defines an enemy type without storing targeting, cooldown, or movement state.
// Melee and ranged enemies share these settings and the same runtime scene.
using Godot;
using System;

public enum EnemyCombatStyle { Melee, Ranged }

[Tool, GlobalClass]
public partial class EnemyDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "melee_robot";
    [Export] public string DisplayName { get; set; } = "Melee Robot";
    [Export] public float SpawnWeight { get; set; } = 1f;
    #endregion

    #region Defense
    [ExportGroup("Defense")]
    [Export] public int MaxVitality { get; set; } = 60;
    [Export] public DefenseDefinition Defense { get; set; }
    #endregion

    #region Movement
    [ExportGroup("Movement")]
    [ExportSubgroup("Chasing")]
    [Export] public float MoveSpeed { get; set; } = 170f;
    [Export] public float StopDistance { get; set; } = 28f;
    [Export] public float HomeLeash { get; set; } = 1400f;

    [ExportSubgroup("Wandering")]
    [Export] public bool WanderingEnabled { get; set; } = true;
    [Export] public float WanderSpeed { get; set; } = 45f;
    [Export] public float WanderRadius { get; set; } = 240f;
    [Export] public Vector2 WanderWait { get; set; } = new(2f, 5f);
    #endregion

    #region Awareness
    [ExportGroup("Awareness")]
    [ExportSubgroup("Targeting")]
    [Export] public float DetectionRange { get; set; } = 650f;
    [Export] public float ForgetRange { get; set; } = 1000f;

    [ExportSubgroup("Combat Distances")]
    [Export] public float CombatRange { get; set; } = 34f;
    [Export] public float PreferredRange { get; set; } = 240f;
    [Export] public float BackAwayRange { get; set; } = 120f;
    #endregion

    #region Combat
    [ExportGroup("Combat")]
    [Export] public EnemyCombatStyle CombatStyle { get; set; }

    [ExportSubgroup("Melee")]
    [Export] public int MeleeDamage { get; set; } = 15;
    [Export] public DamageType MeleeDamageType { get; set; } = DamageType.Kinetic;
    [Export] public double MeleeCooldown { get; set; } = 0.8;

    [ExportSubgroup("Ranged")]
    [Export] public AttackDefinition RangedAttack { get; set; }
    #endregion

    #region Updates
    [ExportGroup("Update Intervals")]
    [Export] public double TargetInterval { get; set; } = 0.35;
    [Export] public double DecisionInterval { get; set; } = 0.2;
    [Export] public double PathInterval { get; set; } = 0.45;
    #endregion

    #region Visuals
    [ExportGroup("Visuals")]
    [Export] public VisualDefinition VisualOverride { get; set; }
    [Export] public Color VisualTint { get; set; } = Colors.White;
    [Export] public float VisualScale { get; set; } = 1f;

    // Bounds relative to the elevated visual origin, before VisualScale.
    [Export] public Rect2 SpawnVisualBounds { get; set; } =
        new(-80, -120, 160, 180);
    #endregion

    #region Validation
    // =========================================================
    // Reject contradictory settings before this definition becomes an actor.
    public void Validate()
    {
        if (MaxVitality < 1 || MoveSpeed <= 0f || WanderSpeed <= 0f ||
            DetectionRange <= 0f || ForgetRange < DetectionRange ||
            CombatRange <= 0f || StopDistance < 0f ||
            TargetInterval < 0.1 || DecisionInterval < 0.1 ||
            PathInterval < 0.1 || VisualScale <= 0f ||
            WanderWait.X < 0f || WanderWait.Y < WanderWait.X ||
            WanderRadius <= 0f || HomeLeash < WanderRadius)
            throw new InvalidOperationException($"Enemy '{Id}' has invalid settings.");

        if (CombatStyle == EnemyCombatStyle.Melee && StopDistance > CombatRange)
            throw new InvalidOperationException($"Enemy '{Id}': StopDistance exceeds CombatRange.");

        if (CombatStyle == EnemyCombatStyle.Ranged &&
            (RangedAttack == null || BackAwayRange < 0f ||
             PreferredRange <= BackAwayRange || PreferredRange > CombatRange))
            throw new InvalidOperationException($"Enemy '{Id}' has invalid ranged settings.");
    }
    #endregion
}