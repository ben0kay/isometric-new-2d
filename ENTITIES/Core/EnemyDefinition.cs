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

      #region Herd
    [ExportGroup("Herd")]

    [Export(PropertyHint.Range, "1,64,1")]
    public int HerdSizeMin { get; set; } = 3;

    [Export(PropertyHint.Range, "1,64,1")]
    public int HerdSizeMax { get; set; } = 3;

    [Export] public float HerdWanderRadius { get; set; } = 300f;
    [Export] public float HerdRoamRadius { get; set; } = 500f;
    [Export] public float HerdCentreStepDistance { get; set; } = 60f;

    [Export(PropertyHint.Range, "1,600,1,or_greater")]
    public double HerdCentreIntervalSeconds { get; set; } = 60.0;
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
    // Reject unusable species settings before creating components.
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SpeciesId) ||
            MaxHealth < 1 || WanderSpeed <= 0f || ThreatSpeed <= 0f ||
            !float.IsFinite(WanderRadius) || WanderRadius <= 0f ||
            WanderWait.X < 0f || WanderWait.Y < WanderWait.X ||
            PathInterval < 0.1 ||
            HerdSizeMin < 1 || HerdSizeMax < HerdSizeMin ||
            HerdSizeMax > 64 ||
            !float.IsFinite(HerdWanderRadius) || HerdWanderRadius <= 0f ||
            !float.IsFinite(HerdRoamRadius) || HerdRoamRadius < 0f ||
            !float.IsFinite(HerdCentreStepDistance) ||
            HerdCentreStepDistance < 0f ||
            !double.IsFinite(HerdCentreIntervalSeconds) ||
            HerdCentreIntervalSeconds < 1.0 ||
            GrazingDuration <= 0.0 || FeedingCooldown < 0.0 ||
            PersonalSpaceRadius <= 0f ||
            DisengageRange < PersonalSpaceRadius ||
            MaxPursuitDistance < Mathf.Max(WanderRadius, HerdWanderRadius) ||
            ThreatMemorySeconds <= 0.0 || MeleeRange <= 0f ||
            MeleeDamage < 0 || MeleeCooldown < 0.1 ||
            PlaceholderDrawing == null || Hitbox == null ||
            ArtworkSize.X <= 0 || ArtworkSize.Y <= 0)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid settings.");

        Hitbox.Validate();
    }
    #endregion
}