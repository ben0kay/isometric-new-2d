// Coordinates shared entity lifecycle, capabilities and one movement tick.
// Species definitions select proactive combat or reactive threat behaviour.
using Godot;
using System;

public partial class Entity : EntityBody
{
    #region Configuration
    [Export] public EntityDefinition Definition { get; set; }

    [ExportGroup("Group")]
    [Export] public string GroupId { get; set; } = "";
    #endregion

    #region Public State
    public EntityGroupMember Membership { get; private set; }
    public EntityWandering Wandering { get; private set; }
    public EntityGrazing Grazing { get; private set; }
    public EntityThreatResponseComponent Threats { get; private set; }

    public Node2D Target => _targeting?.Target ?? Threats?.Target;
    public bool HasTarget =>
        _targeting?.HasTarget == true || Threats?.HasThreat == true;
    public bool HasThreat => HasTarget;
    public bool HasSight => _targeting?.HasSight == true;
    public Vector2 Home => Wandering?.Home ?? (SpawnHome ?? GlobalPosition);

    // Assigned by the owning population or detached scene; unchanged by layer transfers.
    public string PersistentId { get; set; } = "";
    public EntitySaveData PendingSave { get; set; }
    public bool StreamingRetirement { get; private set; }
    public Vector2? SpawnHome { get; set; }
    public ulong RandomSeed { get; set; }
    public bool SpawnPending { get; set; }
    public bool Initialized { get; private set; }
    public bool IsActivated { get; private set; }
    public bool IsOnScreenForAI => _onScreen;

    public override double NavigationPathInterval =>
        Definition?.PathInterval ?? 0.45;

    public int AiStaggerTicks => Mathf.Max(1, _onScreen
        ? (_config?.EnemyOnScreenStaggerTicks ?? 3)
        : (_config?.EnemyOffScreenStaggerTicks ?? 12));

    public event Action Died;
    #endregion

    #region Components And Scheduling
    private EntityTargeting _targeting;
    private EntityCombatMovement _movement;
    private EntitySequence _sequence;
    private EntityCombatController _combat;
    private TerrainVisual _visual;
    private WorldFeedback _feedback;
    private GlobalConfig _config;
    private readonly RandomNumberGenerator _rng = new();

    private double _targetTimer, _decisionTimer;
    private uint _activeLayer;
    private bool _screenSampled, _onScreen;

    private bool Proactive =>
        Definition.Awareness == EntityAwareness.Proactive;
    #endregion

    #region Persistence
    // =========================================================
    // Snapshot durable state while leaving targets, paths and in-flight combat transient.
    public EntitySaveData CaptureSave(EntitySaveData state)
    {
        state.Id = PersistentId; state.Scene = SceneFilePath; state.Definition = Definition.ResourcePath;
        state.Layer = WorldLayerMember.For(this); state.X = GlobalPosition.X; state.Y = GlobalPosition.Y;
        state.HomeX = Home.X; state.HomeY = Home.Y; state.Health = Health.Current;
        state.RandomSeed = RandomSeed; state.RandomState = _rng.State; state.HasRuntimeState = true;
        state.TargetTimer = Math.Max(0, _targetTimer); state.DecisionTimer = Math.Max(0, _decisionTimer);
        state.GroupId = Membership?.Group != null ? Membership.GroupId : "";
        Wandering?.CaptureSave(state); Grazing?.CaptureSave(state);
        return state;
    }

    // =========================================================
    // Retirement reserves logical group membership rather than removing a living member.
    public void RetireForStreaming()
    {
        StreamingRetirement = true;
        Membership?.SuspendForStreaming();
        CollisionLayer = 0; CollisionMask = 0; Hide(); SetPhysicsProcess(false); QueueFree();
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply shared health, collision and optional weapon settings.
    public override void _EnterTree()
    {
        if (Definition == null)
            throw new InvalidOperationException(
                "Entity requires a Definition.");

        Definition.Validate();
        AddToGroup("entities");

        // Existing population, pursuit and debug services still use this group.
        if (Proactive) AddToGroup("enemies");

        MotionMode = MotionModeEnum.Floating;
        CollisionMask = Definition.BodyCollisionMask;
        _activeLayer = CollisionLayer;
        SetPhysicsProcess(false);

        Health health = GetNode<Health>("Systems/Health");
        health.MaxHealth = Definition.MaxHealth;
        health.Defense = Definition.Defense;

        if (Proactive)
        {
            Weapon weapon = Component<Weapon>("Weapon");
            weapon.Team = CombatTeam.Enemy;
            weapon.Attack =
                (Definition.Combat as RangedCombatSettings)?.Attack;
        }

        if (SpawnPending)
        {
            Hide();
            CollisionLayer = 0;
        }
    }

    // =========================================================
    // Bind shared capabilities and complete artwork before activation.
    public override async void _Ready()
    {
        try
        {
            BindSharedComponents();
            _config = WorldConfig.Find(this);
            _rng.Seed = RandomSeed != 0 ? RandomSeed : GetInstanceId();

            _targetTimer = _rng.Randf() * Definition.TargetInterval;
            _decisionTimer = _rng.Randf() * Definition.DecisionInterval;

            Membership = GetNode<EntityGroupMember>("Systems/Group");

            if (!string.IsNullOrWhiteSpace(GroupId))
                Membership.GroupId = GroupId;

            Wandering = Component<EntityWandering>("Wandering");

            if (Definition.GrazingEnabled)
            {
                Grazing = Component<EntityGrazing>("Grazing");
                Grazing.Bind(this);
            }

            if (Proactive)
            {
                _targeting = new EntityTargeting(
                    this, Definition.TargetGroups, IsEligibleTarget);

                Membership.ThreatActive = () => HasTarget;
                Membership.ThreatReceived += ReactToThreat;

                _sequence = Component<EntitySequence>("Sequence");
                _sequence.Bind(new EntitySequenceBinding
                {
                    Actor = this,
                    Weapon = GetNode<Weapon>("Systems/Weapon"),
                    RandomSeed = RandomSeed,
                    GetDefinition = () => Definition.Sequence,
                    GetTarget = () => Target,
                    IsActive = () =>
                        Initialized && IsActivated && !SpawnPending,
                    HasSight = () => HasSight,
                    GetMoveSpeed = () => Definition.MoveSpeed,
                    GetAttackRange = () => Definition.Combat.AttackRange,
                    GetForgetRange = () => Definition.ForgetRange
                });

                _combat =
                    Component<EntityCombatController>("Combat");

                _movement =
                    new EntityCombatMovement(this, Wandering, _rng);
            }
            else if (Definition.ThreatResponse != EntityThreatResponse.Ignore)
            {
                Threats =
                    Component<EntityThreatResponseComponent>("Threats");
                Threats.Bind(this);
            }

            Wandering.Bind(this, new EntityWanderSettings
            {
                Enabled = Definition.WanderingEnabled,
                Speed = Definition.WanderSpeed,
                Radius = Definition.WanderRadius,
                HomeLeash = Definition.HomeLeash,
                Wait = Definition.WanderWait,
                ArrivalDistance = Definition.WanderArrivalDistance,
                ReturnDistance = Definition.WanderReturnDistance,
                RequireDirectPath = Definition.RequireDirectWanderPath,
                ReturnBeforeWaiting = Definition.ReturnBeforeWaiting
            }, SpawnHome ?? GlobalPosition, Membership, Grazing,
                () => HasThreat, _rng, Definition.InitialWanderPause);

            if (PendingSave != null) Membership.GroupId = PendingSave.GroupId;
            Membership.JoinAssignedGroup();
            if (PendingSave != null)
            {
                Health.RestoreState(PendingSave.Health);
                if (PendingSave.HasRuntimeState)
                {
                    _rng.State = PendingSave.RandomState;
                    _targetTimer = PendingSave.TargetTimer; _decisionTimer = PendingSave.DecisionTimer;
                    Wandering.RestoreSave(PendingSave);
                    Grazing?.RestoreSave(PendingSave);
                }
                PendingSave = null;
            }

            Component<EntityDeathLoot>("DeathLoot");
            Health.Died += OnDeath;

            ImageTexture texture = await Definition.GetArtwork(this);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

            Rect2 region = Definition.PlaceholderDrawing != null
                ? new Rect2(Vector2.Zero, Definition.ArtworkSize)
                : Definition.AtlasRegion;

            _visual = TerrainVisual.Attach(
                this, region, Definition.ArtworkOrigin, Vector2.One,
                true, Definition.VisualOverride, texture);

            CanvasItem artwork = _visual.GetNode<CanvasItem>("Artwork");
            artwork.Modulate = Definition.VisualTint;

            if (artwork is Node2D node)
                node.Scale *= Definition.VisualScale;

            // The presentation component supports optional combat helpers.
            Component<EntityPresentation>("Presentation").Bind(this, artwork);

            // Draw shared feedback alongside artwork so terrain elevation is inherited.
            _feedback = Component<WorldFeedback>("WorldFeedback");
            _feedback.Bind(Health, _visual, Definition.SpawnVisualBounds,
                Definition.VisualScale, Definition.HealthBarOverride);

            Initialized = true;
            if (!SpawnPending) Activate();
        }
        catch (Exception error)
        {
            GD.PushError(
                $"Entity '{Definition.SpeciesId}' initialization failed: {error}");
            QueueFree();
        }
    }

    // =========================================================
    // Reuse a scene component or create one optional capability under Systems.
    private T Component<T>(string name) where T : Node, new()
    {
        Node systems = GetNode("Systems");
        T component = systems.GetNodeOrNull<T>(name);

        if (component != null) return component;

        component = new T { Name = name };
        systems.AddChild(component);
        return component;
    }

    // =========================================================
    // Disconnect actor-owned subscriptions and random resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health))
            Health.Died -= OnDeath;

        if (Proactive && GodotObject.IsInstanceValid(Membership))
        {
            Membership.ThreatReceived -= ReactToThreat;
            Membership.ThreatActive = null;
        }

        _targeting?.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Activate only when initialization and placement checks are complete.
    public void Activate()
    {
        if (!Initialized || !Health.IsAlive || IsActivated) return;

        SpawnPending = false;
        IsActivated = true;
        CollisionLayer = _activeLayer;
        Show();
        SetPhysicsProcess(true);
    }
    #endregion

    #region Updates
    // =========================================================
    // Coordinate behaviour before ticking movement exactly once.
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

        _targetTimer -= delta;
        _decisionTimer -= delta;

        Threats?.Tick(delta);
        Wandering.Tick(delta);

        if (Proactive)
            TickProactive(delta);
        else if (_decisionTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 23))
        {
            _decisionTimer = DecisionInterval();

            if (Threats?.Decide() != true)
                Wandering.Decide();
        }

        Motor.Tick(delta);

        if (Definition.FaceMovement &&
            Velocity.LengthSquared() > 0.1f &&
            GodotObject.IsInstanceValid(_visual))
        {
            float facing = Velocity.X < 0f ? -1f : 1f;
            if (!Mathf.IsEqualApprox(_visual.Scale.X, facing))
            {
                _visual.Scale = new Vector2(facing, 1f);
                _feedback?.SetFacing(facing);
            }
        }
    }

    // =========================================================
    // Preserve proactive awareness, combat spacing and sequence execution.
    private void TickProactive(double delta)
    {
        if (HasTarget && !WorldLayerMember.Same(this, Target))
        {
            _sequence.Cancel();
            _targeting.ClearSight();
        }

        if (_targetTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 11))
        {
            _targetTimer = _onScreen
                ? Definition.TargetInterval
                : Math.Max(Definition.TargetInterval,
                    _config.EnemyOffScreenTargetInterval);

            if (_targeting.SelectTarget(
                Definition.DetectionRange, Definition.ForgetRange))
            {
                _sequence.Cancel();
                _movement.Reset();
                Motor.Stop();
                _decisionTimer = 0.0;
            }
        }

        if (_decisionTimer <= 0.0 &&
            StaggeredUpdate.Due(this, AiStaggerTicks, 12))
        {
            _decisionTimer = DecisionInterval();
            _targeting.RefreshSight();

            if (!_sequence.IsRunning)
                _movement.Decide(
                    HasTarget ? Target : null, HasSight,
                    GetMovementSettings(), GetPursuitGoal());
        }

        _combat.Tick(delta);

        bool wasRunning = _sequence.IsRunning;
        _sequence.Tick(delta);

        if (wasRunning && !_sequence.IsRunning)
            _decisionTimer = 0.0;
    }

    // =========================================================
    // Use the configured decision interval with existing offscreen throttling.
    private double DecisionInterval()
    {
        return _onScreen
            ? Definition.DecisionInterval
            : Math.Max(Definition.DecisionInterval,
                _config.EnemyOffScreenDecisionInterval);
    }
    #endregion

    #region Targeting And Positioning
    // =========================================================
    // Accept configured target groups without assuming a particular species.
    private bool IsEligibleTarget(Node2D candidate)
    {
        if (Definition.TargetGroups == null) return false;

        foreach (string group in Definition.TargetGroups)
            if (!string.IsNullOrWhiteSpace(group) &&
                candidate.IsInGroup(group))
                return true;

        return false;
    }

    // =========================================================
    // Accept damage and group alerts for the proactive targeting behaviour.
    private void ReactToThreat(Node2D attacker)
    {
        if (!Initialized || !IsActivated || SpawnPending ||
            IsQueuedForDeletion() || Health?.IsAlive != true ||
            !EntityCombat.IsLiving(attacker) ||
            !IsEligibleTarget(attacker) ||
            !WorldLayerMember.Same(this, attacker) ||
            GlobalPosition.DistanceSquaredTo(attacker.GlobalPosition) >
                Definition.ForgetRange * Definition.ForgetRange)
            return;

        if (_targeting.SetTarget(attacker))
        {
            _sequence.Cancel();
            _movement.Reset();
            Wandering.Interrupt(false);
        }

        _targetTimer = Definition.TargetInterval;
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Supply shared chase or ranged spacing settings from the definition.
    private EntityCombatMovementSettings GetMovementSettings()
    {
        EntityCombatSettings combat = Definition.Combat;

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
    #endregion

    #region Layer Changes
    // =========================================================
    // Ask the existing cave pursuit service for an entrance approach.
    private Vector2? GetPursuitGoal()
    {
        if (!HasTarget || WorldLayerMember.Same(this, Target))
            return null;

        WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);
        return pursuit != null &&
            pursuit.TryGetGoal(this, out Vector2 mouth) ? mouth : null;
    }

    // =========================================================
    // Clear transient movement before changing a pursuit route or world layer.
    public void ResetPursuitMovement()
    {
        _sequence?.Cancel();
        Motor?.Stop();
        Grazing?.Release();
        _targeting?.ClearSight();
        _movement?.Reset();
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Transfer layers and establish the new solo home position.
    public void CrossWorldLayer(string layer, Vector2 position)
    {
        ResetPursuitMovement();
        if (Membership?.Group != null && WorldLayerMember.For(Membership.Group) != layer) Membership.LeaveGroup();

        WorldLayerMember.Attach(
            this, WorldLayerMember.For(this)).SetLayer(layer);

        GlobalPosition = position;
        Velocity = Vector2.Zero;
        Wandering?.SetHome(position);
        _targetTimer = 0.0;
    }
    #endregion

    #region Death And Visibility
    // =========================================================
    // Stop shared behaviour; the death delivery component owns loot and remains.
    private void OnDeath()
    {
        if (IsQueuedForDeletion()) return;

        _sequence?.Cancel();
        Motor?.Stop();
        Grazing?.Release();
        Velocity = Vector2.Zero;
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;

        EntityDeaths.Find(this)?.Record(this);
        Died?.Invoke();
        Hide();
        QueueFree();
    }

    // =========================================================
    // Retain staggered visibility checks and prompt decisions on screen entry.
    private void UpdateScreenState()
    {
        if (_screenSampled && !StaggeredUpdate.Due(
            this, _config.EnemyScreenCheckTicks, 10))
            return;

        bool wasOnScreen = _onScreen;

        _onScreen = ScreenVisibility.Intersects(
            this, Definition.SpawnVisualBounds,
            Definition.VisualScale, _config.EnemyScreenMarginPixels);

        _screenSampled = true;

        if (_onScreen && !wasOnScreen)
        {
            _targetTimer = 0.0;
            _decisionTimer = 0.0;
        }
    }
    #endregion
}
