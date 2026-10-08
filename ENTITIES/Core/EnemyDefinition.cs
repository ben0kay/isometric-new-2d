// Defines enemy identity, movement, defense, presentation and combat resources.
// Awareness and combat ranges are configured together inside Combat.
using Godot;
using System;

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

    [ExportSubgroup("Wandering")]
    [Export] public bool WanderingEnabled { get; set; } = true;
    [Export] public float WanderSpeed { get; set; } = 45f;
    [Export] public float WanderRadius { get; set; } = 240f;
    [Export] public float HomeLeash { get; set; } = 1400f;
    [Export] public Vector2 WanderWait { get; set; } = new(2f, 5f);
    #endregion

    #region Combat
    [ExportGroup("Combat")]
    [Export] public EnemyCombatSettings Combat { get; set; }
    [Export] public EnemySequenceDefinition Sequence { get; set; }

    // Existing consumers read the single source of range settings.
    public float DetectionRange => Combat?.DetectionRange ?? 0f;
    public float ForgetRange => Combat?.ForgetRange ?? 0f;
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
    // Validate definition settings, combat configuration and optional sequences.
    public void Validate()
    {
        if (MaxVitality < 1 || MoveSpeed <= 0f || WanderSpeed <= 0f ||
            TargetInterval < 0.1 || DecisionInterval < 0.1 ||
            PathInterval < 0.1 || VisualScale <= 0f ||
            WanderWait.X < 0f || WanderWait.Y < WanderWait.X ||
            WanderRadius <= 0f || HomeLeash < WanderRadius)
            throw new InvalidOperationException(
                $"Enemy '{Id}' has invalid settings.");

        if (Combat is not MeleeCombatSettings &&
            Combat is not RangedCombatSettings)
            throw new InvalidOperationException(
                $"Enemy '{Id}' requires MeleeCombatSettings " +
                "or RangedCombatSettings.");

        Combat.Validate(Id);
        Sequence?.Validate();

        // Existing Fire sequence actions require a ranged weapon.
        if (Sequence != null && Combat is not RangedCombatSettings)
            throw new InvalidOperationException(
                $"Enemy '{Id}': this sequence pass requires ranged combat settings.");
    }
    #endregion
}