// Coordinates a data-driven enemy's targeting, wandering, and combat positioning.
// Navigation and damage delivery live in separate components under Systems.
using Godot;
using System;

public partial class Enemy : CharacterBody2D
{
    #region Configuration
    [Export] public EnemyDefinition Definition { get; set; }
    #endregion

    #region Public State
    public Health Health { get; private set; }
    public Player Target { get; private set; }
    public Vector2 Home { get; private set; }
    public Vector2? SpawnHome { get; set; }
    public ulong RandomSeed { get; set; }
    public bool SpawnPending { get; set; }
    public bool Initialized { get; private set; }
    public bool IsActivated { get; private set; }
    public bool HasSight { get; private set; }
    public bool HasTarget => IsLiving(Target) && _targetHealth?.IsAlive == true;
    public event Action Died;
    #endregion

#region Private State
private EnemyMotor _motor;
private EnemyCombat _combat;
private Health _targetHealth;
private WorldNavigation _navigation;
private readonly RandomNumberGenerator _rng = new();
private readonly PhysicsRayQueryParameters2D _sightQuery = new();
private readonly Godot.Collections.Array<Rid> _sightExcluded = new();
private double _targetTimer, _decisionTimer, _wanderTimer;
private bool _retreating;
private uint _activeLayer;
private EnemySequence _sequence;
#endregion

    #region Lifecycle
// =========================================================
// Apply shared stats and the selected ranged attack before children initialize.
public override void _EnterTree()
{
    if (Definition == null)
        throw new InvalidOperationException("Enemy requires a Definition.");

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
    weapon.Attack = (Definition.Combat as RangedCombatSettings)?.Attack;

    if (SpawnPending) { Hide(); CollisionLayer = 0; }
}

    // =========================================================
    // Prepare artwork and bind replaceable presentation before activating the enemy.
    public override async void _Ready()
    {
        SetPhysicsProcess(false);

        Health = GetNode<Health>("Systems/Health");
        _motor = GetNode<EnemyMotor>("Systems/Motor");
        _combat = GetNode<EnemyCombat>("Systems/Combat");
        _sequence = GetNode<EnemySequence>("Systems/Sequence");
        Health.Died += OnDeath;
        Home = SpawnHome ?? GlobalPosition;
        _rng.Seed = RandomSeed != 0 ? RandomSeed : GetInstanceId();
        _targetTimer = _rng.Randf() * Definition.TargetInterval;
        _decisionTimer = _rng.Randf() * Definition.DecisionInterval;
        _wanderTimer = _rng.RandfRange(
            Definition.WanderWait.X, Definition.WanderWait.Y);
        _sightQuery.CollisionMask = 1u;
        _sightQuery.CollideWithAreas = false;
        _sightQuery.HitFromInside = true;

        try
        {
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
                presentation = new EnemyPresentation { Name = "Presentation" };
                systems.AddChild(presentation);
            }
            presentation.Bind(this, artwork);

            Initialized = true;
            if (!SpawnPending) Activate();
        }
        catch (Exception error)
        {
            GD.PushError($"Enemy '{Definition.Id}' initialization failed: {error}");
            QueueFree();
        }
    }

// =========================================================
// Remove subscriptions and release reusable awareness resources.
public override void _ExitTree()
{
    if (GodotObject.IsInstanceValid(Health))
        Health.Died -= OnDeath;

    _sightQuery.Dispose();
    _sightExcluded.Clear();
    _rng.Dispose();
}

    // =========================================================
    // Activate only after the population manager accepts the final spawn checks.
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
// Stage awareness, movement decisions, attack execution, then physical movement.
public override void _PhysicsProcess(double delta)
{
    if (!Initialized || !IsActivated || SpawnPending ||
        IsQueuedForDeletion() || Health?.IsAlive != true)
    {
        _sequence?.Cancel();
        SetPhysicsProcess(false);
        return;
    }

    _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
    if (_navigation == null) return;

    _targetTimer -= delta;
    _decisionTimer -= delta;
    _wanderTimer -= delta;

    // Stage 1: periodic awareness and target selection.
    if (_targetTimer <= 0.0)
    {
        _targetTimer = Definition.TargetInterval;
        SelectTarget();
    }

    // Stage 2: refresh sight; ordinary movement yields to an active sequence.
    if (_decisionTimer <= 0.0)
    {
        _decisionTimer = Definition.DecisionInterval;
        if (_sequence.IsRunning)
            HasSight = HasTarget && CanSee(Target.GlobalPosition);
        else
            DecideMovement();
    }

    // Stage 3: advance weapon timing, then the current sequence action.
    _combat.Tick(delta);
    bool wasRunning = _sequence.IsRunning;
    _sequence.Tick(delta);
    if (wasRunning && !_sequence.IsRunning) _decisionTimer = 0.0;

    // Stage 4: execute the goal chosen by the current movement owner.
    _motor.Tick(delta);
}

// =========================================================
// Replace a dead robot with a lootable wreck and notify population ownership.
private void OnDeath()
{
    if (IsQueuedForDeletion()) return;

    _sequence?.Cancel();
    _motor?.Stop();
    Velocity = Vector2.Zero;
    SetPhysicsProcess(false);
    CollisionLayer = 0;
    CollisionMask = 0;

    Vector2 deathPosition = GlobalPosition;
    ulong identity = RandomSeed != 0 ? RandomSeed : GetInstanceId();
    string wreckId = $"dead_robot:{Definition.Id}:{identity}";

    try
    {
        LootWorld.GetOrCreate(this).RecordRobotDeath(wreckId, deathPosition);
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

    #region Targeting
    // =========================================================
    // Reject freed, departing, and dead players.
    private static bool IsLiving(Player player)
    {
        return GodotObject.IsInstanceValid(player) && player.IsInsideTree() &&
            !player.IsQueuedForDeletion() &&
            player.GetNodeOrNull<Health>("Systems/Health")?.IsAlive == true;
    }

// =========================================================
// Require sight for acquisition while retaining tracked targets behind cover.
private void SelectTarget()
{
    Player next = HasTarget ? Target : null;

    if (next != null &&
        GlobalPosition.DistanceSquaredTo(next.GlobalPosition) >
            Definition.ForgetRange * Definition.ForgetRange)
        next = null;

    float best = next != null
        ? GlobalPosition.DistanceSquaredTo(next.GlobalPosition) * 0.64f
        : Definition.DetectionRange * Definition.DetectionRange;

    foreach (Node node in GetTree().GetNodesInGroup("players"))
    {
        if (node is not Player player || !IsLiving(player) ||
            !WorldLayerMember.Same(this, player))
            continue;

        float distance =
            GlobalPosition.DistanceSquaredTo(player.GlobalPosition);

        if (distance > Definition.DetectionRange * Definition.DetectionRange ||
            distance >= best ||
            !CanSee(player.GlobalPosition, player))
            continue;

        next = player;
        best = distance;
    }

    if (next == Target) return;

    _sequence.Cancel();
    Target = next;
    _targetHealth = next?.GetNodeOrNull<Health>("Systems/Health");
    HasSight = false;
    _retreating = false;
    _motor.Stop();
    _decisionTimer = 0.0;
}

// =========================================================
// Check same-layer sight against tall obstacles and tree trunk footprints.
private bool CanSee(Vector2 point, Player player = null)
{
    player ??= Target;

    if (!IsLiving(player) || !WorldLayerMember.Same(this, player))
        return false;

    return CombatCover.FindHit(
        GetWorld2D().DirectSpaceState,
        _sightQuery, _sightExcluded,
        GlobalPosition, point,
        CombatCover.HeightFor(player),
        WorldLayerMember.For(this)).Count == 0;
}
    #endregion

    #region Decisions
// =========================================================
// Approach for melee; use preferred spacing and retreat only for ranged enemies.
private void DecideMovement()
{
    HasSight = HasTarget && CanSee(Target.GlobalPosition);
    if (!HasTarget) { DecideWandering(); return; }

    Vector2 point = Target.GlobalPosition;
    EnemyCombatSettings combat = Definition.Combat;

    if (combat is MeleeCombatSettings)
    {
        _motor.SetGoal(point, Definition.MoveSpeed, combat.StopDistance);
        return;
    }

    if (combat is not RangedCombatSettings ranged)
    {
        _motor.Stop();
        return;
    }

    float distance = GlobalPosition.DistanceTo(point);
    if (distance < ranged.BackAwayRange) _retreating = true;
    if (distance >= ranged.PreferredRange) _retreating = false;

    if (_retreating) { DecideRetreat(point); return; }
    if (HasSight && distance <= ranged.PreferredRange)
    {
        _motor.Stop();
        return;
    }

    _motor.SetGoal(point, Definition.MoveSpeed, ranged.StopDistance);
}

    // =========================================================
    // Find a short clear retreat, trying nearby directions around obstacles.
    private void DecideRetreat(Vector2 targetPoint)
    {
        Vector2 away = GlobalPosition - targetPoint;
        away = away.LengthSquared() > 0.001f ? away.Normalized() : Vector2.Right;

        for (int i = 0; i < 5; i++)
        {
            float angle = i == 0 ? 0f
                : (i % 2 == 1 ? 1f : -1f) * ((i + 1) / 2) * Mathf.Pi / 4f;
            Vector2 point = GlobalPosition + away.Rotated(angle) * 80f;
            if (!_navigation.CanTravelDirectly(GlobalPosition, point)) continue;
            _motor.SetGoal(point, Definition.MoveSpeed, 6f);
            return;
        }
        _motor.Stop();
    }

// =========================================================
// Use HomeLeash only for idle return; choose wandering destinations within WanderRadius.
private void DecideWandering()
{
    if (!Definition.WanderingEnabled) { _motor.Stop(); return; }

    // After a long chase, return toward home before resuming normal wandering.
    if (GlobalPosition.DistanceSquaredTo(Home) >
        Definition.HomeLeash * Definition.HomeLeash)
    {
        _wanderTimer = 0.0;
        _motor.SetGoal(Home, Definition.WanderSpeed, 8f);
        return;
    }

    if (_motor.HasGoal && !_motor.Arrived && !_motor.IsStuck) return;

    if (_motor.HasGoal)
    {
        _motor.Stop();
        _wanderTimer = _rng.RandfRange(Definition.WanderWait.X, Definition.WanderWait.Y);
    }
    if (_wanderTimer > 0.0) return;

    if (GlobalPosition.DistanceSquaredTo(Home) >
        Definition.WanderRadius * Definition.WanderRadius)
    {
        _motor.SetGoal(Home, Definition.WanderSpeed, 8f);
        return;
    }

    for (int i = 0; i < 4; i++)
    {
        float angle = _rng.Randf() * Mathf.Tau;
        float radius = Mathf.Sqrt(_rng.Randf()) * Definition.WanderRadius;
        Vector2 point = Home + Vector2.Right.Rotated(angle) * radius;
        if (!_navigation.CanTravelDirectly(point, point)) continue;

        _motor.SetGoal(point, Definition.WanderSpeed, 8f);
        return;
    }
    _wanderTimer = 1.0;
}
    #endregion
}