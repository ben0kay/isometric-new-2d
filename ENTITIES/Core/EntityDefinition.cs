// Defines reusable wildlife movement, grazing, threat response and artwork.
// Species resources supply values; the actor contains no species-specific rules.
using Godot;
using System;
using System.Threading.Tasks;

public enum EntityThreatResponse { Ignore, Flee, Defend }
public enum EntityTerritoryPolicy { OtherSpecies, OutsideHerd, Everyone }

[Tool, GlobalClass]
public partial class EntityDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string SpeciesId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Creature";
    [Export] public int MaxHealth { get; set; } = 100;
    #endregion

    #region Movement
    [ExportGroup("Movement")]
    [Export] public float WanderSpeed { get; set; } = 26f;
    [Export] public float ThreatSpeed { get; set; } = 85f;
    [Export] public float WanderRadius { get; set; } = 180f;
    [Export] public Vector2 WanderWait { get; set; } = new(2f, 6f);
    [Export] public double PathInterval { get; set; } = 0.45;
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

    #region Grazing
    [ExportGroup("Grazing")]
    [Export] public bool GrazingEnabled { get; set; } = true;
    [Export] public double GrazingDuration { get; set; } = 15.0;
    [Export] public double FeedingCooldown { get; set; } = 8.0;
    #endregion

    #region Threats
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

    #region Melee
    [ExportGroup("Melee")]
    [Export] public float MeleeRange { get; set; } = 45f;
    [Export] public int MeleeDamage { get; set; } = 18;
    [Export] public double MeleeCooldown { get; set; } = 1.2;
    #endregion

    #region Loot
    [ExportGroup("Death Loot")]
    [Export] public LootTable DeathLoot { get; set; }
    #endregion

    #region Artwork
    [ExportGroup("Artwork")]
    [Export] public VisualDefinition VisualOverride { get; set; }
    [Export] public PackedScene PlaceholderDrawing { get; set; }
    [Export] public string ArtworkRevision { get; set; } = "1";
    [Export] public Vector2I ArtworkSize { get; set; } = new(160, 104);
    [Export] public Vector2 ArtworkOrigin { get; set; } = new(-80, -90);
    [Export] public CombatHitboxDefinition Hitbox { get; set; }

    private Task<ImageTexture> _artwork;
    #endregion

    #region Validation
    // =========================================================
    // Reject unusable settings before creating the creature's components.
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(SpeciesId) ||
            MaxHealth < 1 || WanderSpeed <= 0f || ThreatSpeed <= 0f ||
            WanderRadius <= 0f || WanderWait.X < 0f ||
            WanderWait.Y < WanderWait.X || PathInterval < 0.1 ||
            GrazingDuration <= 0.0 || FeedingCooldown < 0.0 ||
            PersonalSpaceRadius <= 0f ||
            DisengageRange < PersonalSpaceRadius ||
            MaxPursuitDistance < WanderRadius ||
            ThreatMemorySeconds <= 0.0 || MeleeRange <= 0f ||
            MeleeDamage < 0 || MeleeCooldown < 0.1 ||
            PlaceholderDrawing == null || Hitbox == null ||
            ArtworkSize.X <= 0 || ArtworkSize.Y <= 0)
            throw new InvalidOperationException(
                $"Entity '{SpeciesId}' has invalid settings.");

        Hitbox.Validate();
    }

    // =========================================================
    // Share one baked placeholder texture across this species' instances.
    public Task<ImageTexture> GetArtwork(Node host)
    {
        return _artwork ??= ArtworkBaker.LoadOrBake(
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