// Defines shared entity capabilities, tuning and artwork.
// Species resources select behaviour without requiring a separate actor class.
using Godot;
using System;
using System.Threading.Tasks;

public enum EntityAwareness { Reactive, Proactive }
public enum EntityThreatResponse { Ignore, Flee, Defend }
public enum EntityTerritoryPolicy { OtherSpecies, OutsideHerd, Everyone }
public enum EntityDeathDelivery { GroundDrops, RobotWreck }

[Tool, GlobalClass]
public partial class EntityDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string SpeciesId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Entity";
    [Export] public float SpawnWeight { get; set; } = 1f;
    #endregion

    #region Defense
    [ExportGroup("Defense")]
    [Export] public int MaxHealth { get; set; } = 100;
    [Export] public DefenseDefinition Defense { get; set; }

    [Export(PropertyHint.Layers2DPhysics)]
    public uint BodyCollisionMask { get; set; } = 13;
    #endregion

    #region Movement
    [ExportGroup("Movement")]
    [Export] public float MoveSpeed { get; set; } = 85f;

    [ExportSubgroup("Wandering")]
    [Export] public bool WanderingEnabled { get; set; } = true;
    [Export] public float WanderSpeed { get; set; } = 26f;
    [Export] public float WanderRadius { get; set; } = 180f;
    [Export] public float HomeLeash { get; set; } = 0f;
    [Export] public Vector2 WanderWait { get; set; } = new(2f, 6f);
    [Export] public float WanderArrivalDistance { get; set; } = 12f;
    [Export] public float WanderReturnDistance { get; set; } = 16f;
    [Export] public bool RequireDirectWanderPath { get; set; } = true;
    [Export] public bool ReturnBeforeWaiting { get; set; } = true;
    [Export] public bool InitialWanderPause { get; set; } = false;
    #endregion

    #region Groups
    [ExportGroup("Group Formation")]

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

    #region Grazing
    [ExportGroup("Grazing")]
    [Export] public bool GrazingEnabled { get; set; } = false;
    [Export] public double GrazingDuration { get; set; } = 15.0;
    [Export] public double FeedingCooldown { get; set; } = 8.0;
    #endregion

    #region Awareness And Combat
    [ExportGroup("Awareness")]
    [Export] public EntityAwareness Awareness { get; set; } =
        EntityAwareness.Reactive;

    [Export] public string[] TargetGroups { get; set; } =
        new[] { "players" };

    [ExportGroup("Combat")]
    [Export] public EntityCombatSettings Combat { get; set; }
    [Export] public EntitySequenceDefinition Sequence { get; set; }

    public float DetectionRange => Combat?.DetectionRange ?? 0f;
    public float ForgetRange => Combat?.ForgetRange ?? 0f;
    #endregion

    #region Threat Response
    [ExportGroup("Threat Response")]
    [Export] public EntityThreatResponse ThreatResponse { get; set; } =
        EntityThreatResponse.Defend;

    [Export] public EntityTerritoryPolicy TerritoryPolicy { get; set; } =
        EntityTerritoryPolicy.OtherSpecies;

    [Export] public float PersonalSpaceRadius { get; set; } = 100f;
    [Export] public float DisengageRange { get; set; } = 360f;
    [Export] public float MaxPursuitDistance { get; set; } = 700f;
    [Export] public double ThreatMemorySeconds { get; set; } = 5.0;
    #endregion

    #region Updates
    [ExportGroup("Update Intervals")]
    [Export] public double TargetInterval { get; set; } = 0.35;
    [Export] public double DecisionInterval { get; set; } = 0.35;
    [Export] public double PathInterval { get; set; } = 0.45;
    #endregion

    #region Death
    [ExportGroup("Death")]
    [Export] public EntityDeathDelivery DeathDelivery { get; set; }
    [Export] public LootTable DeathLoot { get; set; }
    #endregion

    #region Visual Feedback
    [ExportGroup("Visual Feedback")]
    [Export] public HealthBarSettings HealthBarOverride { get; set; }
    [Export] public DamageNumberSettings DamageNumberOverride { get; set; }
    #endregion

    #region Artwork
    [ExportGroup("Artwork")]
    [Export] public VisualDefinition VisualOverride { get; set; }
    [Export] public PackedScene PlaceholderDrawing { get; set; }
    [Export] public string ArtworkRevision { get; set; } = "1";
    [Export] public Vector2I ArtworkSize { get; set; } = new(160, 104);
    [Export] public Vector2 ArtworkOrigin { get; set; } = new(-80, -90);

    // Used when selecting a region from the existing shared placeholder atlas.
    [Export] public Rect2 AtlasRegion { get; set; }

    [Export] public Color VisualTint { get; set; } = Colors.White;
    [Export] public float VisualScale { get; set; } = 1f;
    [Export] public bool FaceMovement { get; set; } = true;

    [Export] public Rect2 SpawnVisualBounds { get; set; } =
        new(-80, -120, 160, 180);

    [Export] public CombatHitboxDefinition Hitbox { get; set; }

    private Task<ImageTexture> _artwork;
    #endregion

    #region Validation
    // =========================================================
    // Validate shared settings and only require enabled capabilities.
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SpeciesId) ||
            !float.IsFinite(SpawnWeight) || SpawnWeight < 0f ||
            MaxHealth < 1 ||
            !float.IsFinite(MoveSpeed) || MoveSpeed <= 0f ||
            !float.IsFinite(WanderSpeed) || WanderSpeed <= 0f ||
            !float.IsFinite(WanderRadius) || WanderRadius <= 0f ||
            !float.IsFinite(HomeLeash) || HomeLeash < 0f ||
            !float.IsFinite(WanderWait.X) ||
            !float.IsFinite(WanderWait.Y) ||
            WanderWait.X < 0f || WanderWait.Y < WanderWait.X ||
            !float.IsFinite(WanderArrivalDistance) ||
            WanderArrivalDistance < 1f ||
            !float.IsFinite(WanderReturnDistance) ||
            WanderReturnDistance < 1f ||
            !double.IsFinite(TargetInterval) || TargetInterval < 0.1 ||
            !double.IsFinite(DecisionInterval) || DecisionInterval < 0.1 ||
            !double.IsFinite(PathInterval) || PathInterval < 0.1 ||
            !float.IsFinite(VisualScale) || VisualScale <= 0f ||
            ArtworkSize.X <= 0 || ArtworkSize.Y <= 0 ||
            Hitbox == null ||
            !Enum.IsDefined(typeof(EntityAwareness), Awareness) ||
            !Enum.IsDefined(typeof(EntityThreatResponse), ThreatResponse) ||
            !Enum.IsDefined(typeof(EntityTerritoryPolicy), TerritoryPolicy) ||
            !Enum.IsDefined(typeof(EntityDeathDelivery), DeathDelivery))
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid shared settings.");

        if (PlaceholderDrawing == null &&
            (AtlasRegion.Size.X <= 0f || AtlasRegion.Size.Y <= 0f))
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' requires a drawing or atlas region.");

        if (GrazingEnabled &&
            (!double.IsFinite(GrazingDuration) || GrazingDuration <= 0.0 ||
             !double.IsFinite(FeedingCooldown) || FeedingCooldown < 0.0))
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid grazing settings.");

        if (Awareness == EntityAwareness.Proactive && Combat == null)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' requires proactive combat settings.");

        if (Awareness == EntityAwareness.Reactive &&
            ThreatResponse == EntityThreatResponse.Defend &&
            Combat is not MeleeCombatSettings)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' requires melee settings to defend.");

        if (Awareness == EntityAwareness.Reactive &&
            (!float.IsFinite(PersonalSpaceRadius) ||
             PersonalSpaceRadius <= 0f ||
             !float.IsFinite(DisengageRange) ||
             DisengageRange < PersonalSpaceRadius ||
             !float.IsFinite(MaxPursuitDistance) ||
             MaxPursuitDistance < WanderRadius ||
             !double.IsFinite(ThreatMemorySeconds) ||
             ThreatMemorySeconds <= 0.0))
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid threat settings.");

        if (Sequence != null && Awareness != EntityAwareness.Proactive)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}': sequences currently require proactive awareness.");

        if (HerdSizeMin < 1 || HerdSizeMax < HerdSizeMin ||
            !float.IsFinite(HerdWanderRadius) || HerdWanderRadius <= 0f ||
            !float.IsFinite(HerdRoamRadius) || HerdRoamRadius <= 0f ||
            !float.IsFinite(HerdCentreStepDistance) ||
            HerdCentreStepDistance <= 0f ||
            !double.IsFinite(HerdCentreIntervalSeconds) ||
            HerdCentreIntervalSeconds <= 0.0)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid group formation settings.");

        Combat?.Validate(SpeciesId);
        Sequence?.Validate();
        Hitbox.Validate();
    }

    // =========================================================
    // Reuse one artwork task for every instance of this definition.
    public Task<ImageTexture> GetArtwork(Node host)
    {
        return _artwork ??= LoadArtwork(host);
    }

    // =========================================================
    // Bake a species drawing or reuse the existing shared placeholder atlas.
    private async Task<ImageTexture> LoadArtwork(Node host)
    {
        if (PlaceholderDrawing == null)
        {
            await PlaceholderAtlas.EnsureReady(host);
            return PlaceholderAtlas.Texture;
        }

        return await ArtworkBaker.LoadOrBake(
            host, $"entity_{SpeciesId}", ArtworkRevision, ArtworkSize,
            () =>
            {
                Node2D drawing = PlaceholderDrawing.Instantiate<Node2D>();
                drawing.Position = -ArtworkOrigin;
                return drawing;
            });
    }
    #endregion
}