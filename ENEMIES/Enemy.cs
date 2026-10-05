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
    private double _targetTimer, _decisionTimer, _wanderTimer;
    private bool _retreating;
    private uint _activeLayer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply definition settings before child components initialize.
    public override void _EnterTree()
    {
        if (Definition == null) throw new InvalidOperationException("Enemy requires a Definition.");
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
        weapon.Attack = Definition.RangedAttack;

        if (SpawnPending) { Hide(); CollisionLayer = 0; }
    }

    // =========================================================
    // Prepare runtime state and attach the existing custom-or-baked artwork.
    public override async void _Ready()
    {
        Health = GetNode<Health>("Systems/Health");
        _motor = GetNode<EnemyMotor>("Systems/Motor");
        _combat = GetNode<EnemyCombat>("Systems/Combat");
        Health.Died += OnDeath;
        Home = SpawnHome ?? GlobalPosition;
        _rng.Seed = RandomSeed != 0 ? RandomSeed : GetInstanceId();
        _targetTimer = _rng.Randf() * Definition.TargetInterval;
        _decisionTimer = _rng.Randf() * Definition.DecisionInterval;
        _wanderTimer = _rng.RandfRange(Definition.WanderWait.X, Definition.WanderWait.Y);
        _sightQuery.CollisionMask = 1u;
        _sightQuery.CollideWithAreas = false;
        _sightQuery.HitFromInside = true;

        try
        {
            await PlaceholderAtlas.EnsureReady(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            TerrainVisual visual = TerrainVisual.Attach(
                this, PlaceholderAtlas.EnemyRegion,
                new Vector2(-48, -64), Vector2.One, true, Definition.VisualOverride);
            CanvasItem artwork = visual.GetNode<CanvasItem>("Artwork");
            artwork.Modulate = Definition.VisualTint;
            if (artwork is Node2D node) node.Scale *= Definition.VisualScale;

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
    // Remove subscriptions and dispose this actor's reusable query resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Health)) Health.Died -= OnDeath;
        _sightQuery.Dispose();
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
    // Run cheap physical updates while decimating targeting and decision work.
    public override void _PhysicsProcess(double delta)
    {
        if (!Health.IsAlive) return;
        _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
        if (_navigation == null) return;

        _targetTimer -= delta;
        _decisionTimer -= delta;
        _wanderTimer -= delta;

        if (_targetTimer <= 0.0)
        {
            _targetTimer = Definition.TargetInterval;
            SelectTarget();
        }

        if (_decisionTimer <= 0.0)
        {
            _decisionTimer = Definition.DecisionInterval;
            DecideMovement();
        }

        _motor.Tick(delta);
        _combat.Tick(delta);
    }

    // =========================================================
    // Notify population ownership without duplicating CombatLife death handling.
    private void OnDeath()
    {
        Died?.Invoke();
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
    // Acquire visible players nearby while retaining a target to the forget range.
    private void SelectTarget()
    {
        Player next = HasTarget ? Target : null;
        if (next != null &&
            (GlobalPosition.DistanceSquaredTo(next.GlobalPosition) >
                Definition.ForgetRange * Definition.ForgetRange ||
             Home.DistanceSquaredTo(next.GlobalPosition) >
                Definition.HomeLeash * Definition.HomeLeash))
            next = null;

        float best = next != null
            ? GlobalPosition.DistanceSquaredTo(next.GlobalPosition) * 0.64f
            : Definition.DetectionRange * Definition.DetectionRange;

        foreach (Node node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player player || !IsLiving(player)) continue;
            float distance = GlobalPosition.DistanceSquaredTo(player.GlobalPosition);
            if (distance > Definition.DetectionRange * Definition.DetectionRange ||
                distance >= best ||
                Home.DistanceSquaredTo(player.GlobalPosition) >
                    Definition.HomeLeash * Definition.HomeLeash ||
                !CanSee(player.GlobalPosition)) continue;
            next = player;
            best = distance;
        }

        if (next == Target) return;
        Target = next;
        _targetHealth = next?.GetNodeOrNull<Health>("Systems/Health");
        _retreating = false;
        _motor.Stop();
        _decisionTimer = 0.0;
    }

    // =========================================================
    // Test sight against solid obstacles; ranged shots may cross open chasms.
    private bool CanSee(Vector2 point)
    {
        _sightQuery.From = GlobalPosition;
        _sightQuery.To = point;
        return GetWorld2D().DirectSpaceState.IntersectRay(_sightQuery).Count == 0;
    }
    #endregion

    #region Decisions
    // =========================================================
    // Choose wandering, chasing, holding range, or retreating.
    private void DecideMovement()
    {
        HasSight = HasTarget && CanSee(Target.GlobalPosition);
        if (!HasTarget) { DecideWandering(); return; }

        Vector2 point = Target.GlobalPosition;
        float distance = GlobalPosition.DistanceTo(point);

        if (Definition.CombatStyle == EnemyCombatStyle.Melee)
        {
            _motor.SetGoal(point, Definition.MoveSpeed, Definition.StopDistance);
            return;
        }

        if (distance < Definition.BackAwayRange) _retreating = true;
        if (distance >= Definition.PreferredRange) _retreating = false;

        if (_retreating) { DecideRetreat(point); return; }
        if (HasSight && distance <= Definition.PreferredRange)
        {
            _motor.Stop();
            return;
        }
        _motor.SetGoal(point, Definition.MoveSpeed, Definition.StopDistance);
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
    // Wander between valid home-zone points with pauses and stuck recovery.
    private void DecideWandering()
    {
        if (!Definition.WanderingEnabled) { _motor.Stop(); return; }
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