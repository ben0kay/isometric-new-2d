// Coordinates robot lifecycle, presentation and staggered behaviour updates.
// Targeting, wandering, combat positioning and attacks have separate owners.
using Godot;
using System;

public partial class Enemy : EntityBody
{
    #region Configuration
    [Export] public EnemyDefinition Definition { get; set; }
    #endregion

    #region Public State
    public Player Target => _targeting?.Target;
    public bool HasTarget => _targeting?.HasTarget == true;
    public bool HasSight => _targeting?.HasSight == true;

    public Vector2 Home =>
        _wandering?.Home ?? (SpawnHome ?? GlobalPosition);

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
    private EnemyTargeting _targeting;
    private EnemyWandering _wandering;
    private EnemyCombatMovement _combatMovement;
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
    // Apply stats and attack settings before child components initialize.
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
    // Bind helpers once, then initialize artwork and event-driven presentation.
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

            _targeting = new EnemyTargeting(this);
            _wandering = new EnemyWandering(
                this, _rng, SpawnHome ?? GlobalPosition);
            _combatMovement = new EnemyCombatMovement(
                this, _wandering, _rng);

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

            Node systems = GetNode("Systems");
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
    // Disconnect health and dispose the actor's reusable native resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health))
            Health.Died -= OnDeath;

        _targeting?.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Activate after the population manager accepts the final spawn checks.
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
    // Run one actor tick with the existing staggered decision schedule.
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

            if (_targeting.SelectTarget())
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
                _combatMovement.Decide();
        }

        _combat.Tick(delta);

        bool wasRunning = _sequence.IsRunning;
        _sequence.Tick(delta);

        if (wasRunning && !_sequence.IsRunning)
            _decisionTimer = 0.0;

        Motor.Tick(delta);
    }

    // =========================================================
    // Create wreckage on the death layer before notifying population listeners.
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
        Vector2 deathPosition = GlobalPosition;
        ulong identity = RandomSeed != 0
            ? RandomSeed : GetInstanceId();

        string wreckId =
            $"{layer}:dead_robot:{Definition.Id}:{identity}";

        try
        {
            LootWorld.GetOrCreate(this).RecordRobotDeath(
                wreckId, deathPosition, layer);
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

    #region Cave Pursuit
    // =========================================================
    // Release sequence movement and invalidate sight before entrance pursuit.
    public void ResetPursuitMovement()
    {
        _sequence?.Cancel();
        Motor?.Stop();
        _targeting?.ClearSight();
        _combatMovement?.Reset();
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Transfer layers; shared navigation refreshes from the changed member.
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
    // Sample visibility on staggered ticks and refresh decisions on entry.
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