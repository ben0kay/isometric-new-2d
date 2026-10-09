// Adapts existing robot scenes and definitions to shared entity behaviours.
// Species-independent movement and targeting live outside this migration adapter.
using Godot;
using System;

public partial class Enemy : EntityBody
{
    #region Configuration
    [Export] public EnemyDefinition Definition { get; set; }

    [ExportGroup("Group")]
    [Export] public string GroupId { get; set; } = "";
    #endregion

    #region Public State
    public Player Target => _targeting?.Target as Player;
    public bool HasTarget => _targeting?.HasTarget == true;
    public bool HasSight => _targeting?.HasSight == true;
    public Vector2 Home =>
        _wandering?.Home ?? (SpawnHome ?? GlobalPosition);

    public EntityGroupMember Membership { get; private set; }
    public Vector2? SpawnHome { get; set; }
    public ulong RandomSeed { get; set; }
    public bool SpawnPending { get; set; }
    public bool Initialized { get; private set; }
    public bool IsActivated { get; private set; }

    public override double NavigationPathInterval =>
        Definition?.PathInterval ?? 0.45;

    public event Action Died;
    #endregion

    #region Components And Scheduling
    private EnemyCombat _combat;
    private EnemySequence _sequence;
    private EntityTargeting _targeting;
    private EntityWandering _wandering;
    private EntityCombatMovement _combatMovement;
    private readonly RandomNumberGenerator _rng = new();

    private double _targetTimer, _decisionTimer;
    private uint _activeLayer;
    private GlobalConfig _aiConfig;
    private bool _screenSampled, _onScreen;

    public bool IsOnScreenForAI => _onScreen;

    public int AiStaggerTicks => Mathf.Max(1, _onScreen
        ? (_aiConfig?.EnemyOnScreenStaggerTicks ?? 3)
        : (_aiConfig?.EnemyOffScreenStaggerTicks ?? 12));
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply existing robot stats before child components initialize.
    public override void _EnterTree()
    {
        if (Definition == null)
            throw new InvalidOperationException(
                "Enemy requires a Definition.");

        Definition.Validate();
        AddToGroup("enemies");
        MotionMode = MotionModeEnum.Floating;
        SetPhysicsProcess(false);
        _activeLayer = CollisionLayer;

        Health health = GetNode<Health>("Systems/Health");
        health.MaxHealth = Definition.MaxVitality;
        health.Defense = Definition.Defense;

        Weapon weapon = GetNode<Weapon>("Systems/Weapon");
        weapon.Team = CombatTeam.Enemy;
        weapon.Attack =
            (Definition.Combat as RangedCombatSettings)?.Attack;

        if (SpawnPending)
        {
            Hide();
            CollisionLayer = 0;
        }
    }

    // =========================================================
    // Bind shared behaviours, optional membership and existing presentation.
    public override async void _Ready()
    {
        SetPhysicsProcess(false);

        try
        {
            BindSharedComponents();
            _combat = GetNode<EnemyCombat>("Systems/Combat");
            _sequence = GetNode<EnemySequence>("Systems/Sequence");
            Health.Died += OnDeath;

            _rng.Seed = RandomSeed != 0
                ? RandomSeed : GetInstanceId();

            _targetTimer = _rng.Randf() * Definition.TargetInterval;
            _decisionTimer = _rng.Randf() * Definition.DecisionInterval;

            Node systems = GetNode("Systems");

            Membership =
                systems.GetNodeOrNull<EntityGroupMember>("Group");

            if (Membership == null)
            {
                Membership = new EntityGroupMember { Name = "Group" };
                systems.AddChild(Membership);
            }

            if (!string.IsNullOrWhiteSpace(GroupId))
                Membership.GroupId = GroupId;

            // Existing robot combat targets players; shared targeting is generic.
            _targeting = new EntityTargeting(
                this, new[] { "players" }, candidate => candidate is Player);

            Membership.ThreatActive = () => HasTarget;

            _wandering =
                systems.GetNodeOrNull<EntityWandering>("Wandering");

            if (_wandering == null)
            {
                _wandering = new EntityWandering { Name = "Wandering" };
                systems.AddChild(_wandering);
            }

            _wandering.Bind(this, new EntityWanderSettings
            {
                Enabled = Definition.WanderingEnabled,
                Speed = Definition.WanderSpeed,
                Radius = Definition.WanderRadius,
                HomeLeash = Definition.HomeLeash,
                Wait = Definition.WanderWait,
                ArrivalDistance = 8f,
                ReturnDistance = 8f,
                RequireDirectPath = false,
                TickMotor = false
            }, SpawnHome ?? GlobalPosition, Membership,
                hasThreat: () => HasTarget,
                random: _rng, initialPause: true);

            _combatMovement = new EntityCombatMovement(
                this, _wandering, _rng);

            Membership.JoinAssignedGroup();

            await PlaceholderAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            TerrainVisual visual = TerrainVisual.Attach(
                this, PlaceholderAtlas.EnemyRegion,
                new Vector2(-48, -64), Vector2.One, true,
                Definition.VisualOverride);

            CanvasItem artwork = visual.GetNode<CanvasItem>("Artwork");
            artwork.Modulate = Definition.VisualTint;

            if (artwork is Node2D node)
                node.Scale *= Definition.VisualScale;

            EnemyPresentation presentation =
                systems.GetNodeOrNull<EnemyPresentation>("Presentation");

            if (presentation == null)
            {
                presentation = new EnemyPresentation
                {
                    Name = "Presentation"
                };
                systems.AddChild(presentation);
            }

            presentation.Bind(this, artwork);
            Initialized = true;
            if (!SpawnPending) Activate();
        }
        catch (Exception error)
        {
            GD.PushError(
                $"Enemy '{Definition.Id}' initialization failed: {error}");
            QueueFree();
        }
    }

    // =========================================================
    // Release shared targeting and the adapter's owned random stream.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health))
            Health.Died -= OnDeath;

        if (GodotObject.IsInstanceValid(Membership))
            Membership.ThreatActive = null;

        _targeting?.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Activate only after population placement checks accept the actor.
    public void Activate()
    {
        if (!Initialized || !Health.IsAlive || IsActivated) return;

        SpawnPending = false;
        IsActivated = true;
        CollisionLayer = _activeLayer;
        Show();
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Drive shared helpers through the existing single staggered actor loop.
    public override void _PhysicsProcess(double delta)
    {
        if (!Initialized || !IsActivated || SpawnPending ||
            IsQueuedForDeletion() || Health?.IsAlive != true)
        {
            _sequence?.Cancel();
            SetPhysicsProcess(false);
            return;
        }

        UpdateScreenState();
        if (Navigation == null) return;

        if (HasTarget && !WorldLayerMember.Same(this, Target))
        {
            _sequence.Cancel();
            _targeting.ClearSight();
        }

        _targetTimer -= delta;
        _decisionTimer -= delta;
        _wandering.Tick(delta);

        int stagger = AiStaggerTicks;

        if (_targetTimer <= 0.0 &&
            StaggeredUpdate.Due(this, stagger, 11))
        {
            _targetTimer = _onScreen
                ? Definition.TargetInterval
                : Math.Max(Definition.TargetInterval,
                    _aiConfig.EnemyOffScreenTargetInterval);

            if (_targeting.SelectTarget(
                Definition.DetectionRange, Definition.ForgetRange))
            {
                _sequence.Cancel();
                _combatMovement.Reset();
                Motor.Stop();
                _decisionTimer = 0.0;
            }
        }

        if (_decisionTimer <= 0.0 &&
            StaggeredUpdate.Due(this, stagger, 12))
        {
            _decisionTimer = _onScreen
                ? Definition.DecisionInterval
                : Math.Max(Definition.DecisionInterval,
                    _aiConfig.EnemyOffScreenDecisionInterval);

            _targeting.RefreshSight();

            if (!_sequence.IsRunning)
                _combatMovement.Decide(
                    HasTarget ? _targeting.Target : null,
                    HasSight, GetMovementSettings(), GetPursuitGoal());
        }

        _combat.Tick(delta);

        bool wasRunning = _sequence.IsRunning;
        _sequence.Tick(delta);

        if (wasRunning && !_sequence.IsRunning)
            _decisionTimer = 0.0;

        Motor.Tick(delta);
    }

    // =========================================================
    // Preserve existing robot wreckage and population notifications.
    private void OnDeath()
    {
        if (IsQueuedForDeletion()) return;

        _sequence?.Cancel();
        Motor?.Stop();
        Velocity = Vector2.Zero;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;

        WorldLayer layer = WorldLayerMember.For(this);
        ulong identity = RandomSeed != 0
            ? RandomSeed : GetInstanceId();

        string wreckId =
            $"{layer}:dead_robot:{Definition.Id}:{identity}";

        try
        {
            LootWorld.GetOrCreate(this).RecordRobotDeath(
                wreckId, GlobalPosition, layer);
        }
        catch (Exception error)
        {
            GD.PushError($"Unable to create robot wreck: {error}");
        }

        Died?.Invoke();
        Hide();
        QueueFree();
    }
    #endregion

    #region Definition Adapter
    // =========================================================
    // Translate legacy robot resources into species-independent positioning.
    private EntityCombatMovementSettings GetMovementSettings()
    {
        EnemyCombatSettings combat = Definition.Combat;

        EntityCombatMovementSettings settings = new()
        {
            Speed = Definition.MoveSpeed,
            StopDistance = combat.StopDistance,
            Positioning = combat is MeleeCombatSettings
                ? EntityCombatPositioning.Chase
                : EntityCombatPositioning.None
        };

        if (combat is RangedCombatSettings ranged)
        {
            settings.Positioning = EntityCombatPositioning.KeepDistance;
            settings.PreferredRange = ranged.PreferredRange;
            settings.AttackRange = ranged.AttackRange;
            settings.BackAwayRange = ranged.BackAwayRange;
            settings.RangeBias = ranged.RangeBias;
        }

        return settings;
    }

    // =========================================================
    // Keep legacy cave pursuit outside shared combat movement.
    private Vector2? GetPursuitGoal()
    {
        if (!HasTarget || WorldLayerMember.Same(this, Target))
            return null;

        CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);
        return pursuit != null &&
            pursuit.TryGetGoal(this, out Vector2 mouth) ? mouth : null;
    }
    #endregion

    #region Cave Pursuit
    // =========================================================
    // Clear transient combat movement before pursuing an entrance.
    public void ResetPursuitMovement()
    {
        _sequence?.Cancel();
        Motor?.Stop();
        _targeting?.ClearSight();
        _combatMovement?.Reset();
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Transfer layers and establish the new solo home position.
    public void CrossWorldLayer(WorldLayer layer, Vector2 position)
    {
        ResetPursuitMovement();
        WorldLayerMember.Attach(
            this, WorldLayerMember.For(this)).SetLayer(layer);

        GlobalPosition = position;
        Velocity = Vector2.Zero;
        _wandering?.SetHome(position);
        _targetTimer = 0.0;
    }
    #endregion

    #region Screen Scheduling
    // =========================================================
    // Retain staggered visibility checks and faster decisions on screen entry.
    private void UpdateScreenState()
    {
        _aiConfig ??= WorldConfig.Find(this);

        if (_screenSampled && !StaggeredUpdate.Due(
            this, _aiConfig.EnemyScreenCheckTicks, 10))
            return;

        bool wasOnScreen = _onScreen;
        _onScreen = ScreenVisibility.Intersects(
            this, Definition.SpawnVisualBounds,
            Definition.VisualScale, _aiConfig.EnemyScreenMarginPixels);
        _screenSampled = true;

        if (_onScreen && !wasOnScreen)
        {
            _targetTimer = 0.0;
            _decisionTimer = 0.0;
        }
    }
    #endregion
}