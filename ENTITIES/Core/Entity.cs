// Runs configurable wandering, grazing, territory response and herd retaliation.
// Uses the existing motor/navigation; species-specific settings remain in resources.
using Godot;
using System;

public partial class Entity : CharacterBody2D
{
    #region Configuration
    [Export] public EntityDefinition Definition { get; set; }
    [Export] public string HerdId { get; set; } = "";
    #endregion

#region Public State
public Health Health { get; private set; }
public EntityHerd Herd { get; private set; }
public Vector2 Home { get; private set; }
public bool HasThreat => Living(_threat);

public bool HasHerd => GodotObject.IsInstanceValid(Herd) &&
    !Herd.IsQueuedForDeletion();

public Vector2 Centre => HasHerd ? Herd.GlobalPosition : Home;
public float ActiveWanderRadius => HasHerd
    ? Herd.WanderRadius : Definition.WanderRadius;
#endregion

    #region State
    private EnemyMotor _motor;
    private GrazingWorld _grazing;
    private TerrainVisual _visual;
    private Grass _food;
    private Node2D _threat;
    private readonly RandomNumberGenerator _rng = new();

    private double _wait, _feedingCooldown, _eatTime;
    private double _threatMemory, _attackCooldown, _travelTime;
    private bool _ready, _eating;
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply species health before the child health component initializes.
    public override void _EnterTree()
    {
        if (Definition == null)
            throw new InvalidOperationException("Entity requires a Definition.");

        Definition.Validate();
        MotionMode = MotionModeEnum.Floating;
        GetNode<Health>("Systems/Health").MaxHealth = Definition.MaxHealth;
        AddToGroup("entities");
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Bind shared systems and replaceable baked or imported artwork.
    public override async void _Ready()
    {
        try
        {
            Home = GlobalPosition;
            _rng.Randomize();
            Health = GetNode<Health>("Systems/Health");
            _motor = GetNode<EnemyMotor>("Systems/Motor");
            _grazing = GrazingWorld.GetOrCreate(this);

            WorldLayerMember.Attach(this, WorldLayer.Surface);
            Herd = EntityHerd.Find(this, HerdId);
            Herd?.Join(this);

            GetNode("Systems").AddChild(
                new EntityDeathLoot { Name = "DeathLoot" });

            Health.Hit += OnHit;
            Health.Died += OnDeath;

            ImageTexture texture = await Definition.GetArtwork(this);
            if (!IsInsideTree() || IsQueuedForDeletion()) return;

            _visual = TerrainVisual.Attach(
                this,
                new Rect2(Vector2.Zero, Definition.ArtworkSize),
                Definition.ArtworkOrigin,
                Vector2.One, true, Definition.VisualOverride, texture);

            _ready = true;
            SetPhysicsProcess(true);
        }
        catch (Exception error)
        {
            GD.PushError($"Entity '{Name}' initialization failed: {error}");
        }
    }

// =========================================================
// Release reservations, herd membership and subscriptions on every exit.
public override void _ExitTree()
{
    ReleaseFood();

    EntityHerd previousHerd = Herd;
    Herd = null;

    if (GodotObject.IsInstanceValid(previousHerd) &&
        !previousHerd.IsQueuedForDeletion())
        previousHerd.Leave(this);

    if (GodotObject.IsInstanceValid(Health))
    {
        Health.Hit -= OnHit;
        Health.Died -= OnDeath;
    }

    _rng.Dispose();
}

    // =========================================================
    // Advance movement and feeding while staggering decisions and threat scans.
    public override void _PhysicsProcess(double delta)
    {
        if (!_ready || !Health.IsAlive) return;

        _wait -= delta;
        _feedingCooldown -= delta;
        _threatMemory -= delta;
        _attackCooldown -= delta;

        if (_food != null &&
            (!GodotObject.IsInstanceValid(_food) ||
             _food.IsQueuedForDeletion()))
            ReleaseFood();

        if (_eating && _food != null)
        {
            _eatTime += delta;
            if (_eatTime >= Definition.GrazingDuration)
            {
                _grazing.Consume(this, _food);
                ReleaseFood();
                _feedingCooldown = Definition.FeedingCooldown;
                _wait = _rng.RandfRange(
                    Definition.WanderWait.X, Definition.WanderWait.Y);
            }
        }
        else if (_motor.HasGoal)
        {
            _travelTime += delta;
            _motor.Tick(delta);

            if (!HasThreat && _motor.Arrived)
            {
                _motor.Stop();
                _travelTime = 0.0;

                if (_food != null)
                {
                    _eating = true;
                    _eatTime = 0.0;
                }
                else
                    _wait = _rng.RandfRange(
                        Definition.WanderWait.X, Definition.WanderWait.Y);
            }

            if (!HasThreat && (_motor.IsStuck || _travelTime > 20.0))
            {
                _motor.Stop();
                ReleaseFood();
                _wait = 2.0;
            }
        }

        if (StaggeredUpdate.DueSeconds(this, 0.35, 23))
            Decide();

        if (Velocity.LengthSquared() > 0.1f)
            _visual.Scale = new Vector2(Velocity.X < 0f ? -1f : 1f, 1f);

        _visual.UpdateHeight();
    }
    #endregion

    #region Decisions
// =========================================================
// Prioritize threats, then use the active solo or shared herd wander area.
private void Decide()
{
    ScanPersonalSpace();

    if (HasThreat)
    {
        if (_threatMemory <= 0.0 ||
            GlobalPosition.DistanceSquaredTo(_threat.GlobalPosition) >
                Definition.DisengageRange * Definition.DisengageRange ||
            GlobalPosition.DistanceSquaredTo(Centre) >
                Definition.MaxPursuitDistance * Definition.MaxPursuitDistance)
        {
            _threat = null;
            _motor.Stop();
            _motor.SetGoal(Centre, Definition.WanderSpeed, 16f);
            return;
        }

        HandleThreat();
        return;
    }

    if (_threat != null)
    {
        _threat = null;
        _motor.Stop();
    }

    Vector2 centre = Centre;
    float radius = ActiveWanderRadius;
    float radiusSquared = radius * radius;

    if (_food != null)
    {
        if (_food.GlobalPosition.DistanceSquaredTo(centre) > radiusSquared)
        {
            ReleaseFood();
            _motor.Stop();
        }
        else
            return;
    }

    if (_motor.HasGoal) return;

    WorldNavigation navigation = WorldNavigation.For(this);
    if (navigation == null) return;

    // Recover after pursuit or a centre shift before ordinary wandering.
    if (GlobalPosition.DistanceSquaredTo(centre) > radiusSquared)
    {
        _travelTime = 0.0;
        _motor.SetGoal(centre, Definition.WanderSpeed, 16f);
        return;
    }

    if (_wait > 0.0) return;

    if (Definition.GrazingEnabled && _feedingCooldown <= 0.0)
    {
        _food = _grazing.Reserve(this, centre, radius);
        if (_food != null)
        {
            _travelTime = 0.0;
            _motor.SetGoal(
                _food.GlobalPosition, Definition.WanderSpeed, 18f);
            return;
        }
    }

    for (int attempt = 0; attempt < 4; attempt++)
    {
        Vector2 point = centre +
            Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
            Mathf.Sqrt(_rng.Randf()) * radius;

        if (!navigation.CanTravelDirectly(GlobalPosition, point))
            continue;

        _travelTime = 0.0;
        _motor.SetGoal(point, Definition.WanderSpeed, 12f);
        return;
    }

    _wait = 2.0;
}

    // =========================================================
    // Respond to nearby actors according to species and herd tolerance.
    private void ScanPersonalSpace()
    {
        if (Definition.ThreatResponse == EntityThreatResponse.Ignore)
            return;

        Node2D nearest = null;
        float distance = Definition.PersonalSpaceRadius *
            Definition.PersonalSpaceRadius;

        foreach (string group in new[] { "players", "enemies", "entities" })
        foreach (Node node in GetTree().GetNodesInGroup(group))
        {
            if (node is not Node2D actor || actor == this ||
                !Living(actor) || !WorldLayerMember.Same(this, actor))
                continue;

            bool sameSpecies = actor is Entity entity &&
                entity.Definition.SpeciesId == Definition.SpeciesId;
            bool sameHerd = actor is Entity herdMember &&
                GodotObject.IsInstanceValid(Herd) && herdMember.Herd == Herd;

            if (Definition.TerritoryPolicy == EntityTerritoryPolicy.OtherSpecies &&
                sameSpecies)
                continue;
            if (Definition.TerritoryPolicy == EntityTerritoryPolicy.OutsideHerd &&
                sameHerd)
                continue;

            float candidate = GlobalPosition.DistanceSquaredTo(
                actor.GlobalPosition);
            if (candidate >= distance) continue;

            distance = candidate;
            nearest = actor;
        }

        if (nearest != null) ReactTo(nearest);
    }

    // =========================================================
    // Chase and strike, or choose a validated escape point.
    private void HandleThreat()
    {
        WorldNavigation navigation = WorldNavigation.For(this);
        if (navigation == null) return;

        if (Definition.ThreatResponse == EntityThreatResponse.Flee)
        {
            if (_motor.HasGoal && !_motor.Arrived && !_motor.IsStuck)
                return;

            Vector2 away = GlobalPosition - _threat.GlobalPosition;
            if (away.LengthSquared() < 0.01f) away = Vector2.Right;
            away = away.Normalized();

            for (int i = 0; i < 4; i++)
            {
                Vector2 point = GlobalPosition +
                    away.Rotated(_rng.RandfRange(-0.65f, 0.65f)) * 140f;

                if (point.DistanceSquaredTo(Centre) >
                    Definition.MaxPursuitDistance * Definition.MaxPursuitDistance ||
                    !navigation.CanTravelDirectly(GlobalPosition, point))
                    continue;

                _motor.SetGoal(point, Definition.ThreatSpeed, 12f);
                return;
            }
            _motor.Stop();
            return;
        }

        float distance = GlobalPosition.DistanceTo(_threat.GlobalPosition);
        if (distance > Definition.MeleeRange)
        {
            _motor.SetGoal(
                _threat.GlobalPosition,
                Definition.ThreatSpeed,
                Definition.MeleeRange * 0.8f);
            return;
        }

        _motor.Stop();
        if (_attackCooldown > 0.0 ||
            !navigation.CanTravelDirectly(
                GlobalPosition, _threat.GlobalPosition))
            return;

        Health victim = _threat.GetNodeOrNull<Health>("Systems/Health");
        _attackCooldown = Definition.MeleeCooldown;
        victim?.Damage(Definition.MeleeDamage, DamageType.Neutral, this);
    }
    #endregion

    #region Events
// =========================================================
// Refresh threat memory without resetting movement for the same attacker.
public void ReactTo(Node2D attacker)
{
    if (Definition.ThreatResponse == EntityThreatResponse.Ignore ||
        !Living(attacker) || attacker == this ||
        !WorldLayerMember.Same(this, attacker))
        return;

    bool changedThreat = _threat != attacker;
    _threat = attacker;
    _threatMemory = Definition.ThreatMemorySeconds;

    if (!changedThreat) return;

    ReleaseFood();
    _motor.Stop();
}

    // =========================================================
    // Accepted combat damage alerts the herd, including a fatal hit.
    private void OnHit()
    {
        Node2D attacker = Health.LastDamageSource;
        ReactTo(attacker);
        if (GodotObject.IsInstanceValid(Herd))
            Herd.Alert(attacker);
    }

// =========================================================
// Deliver the existing death flow and immediately update surviving herd members.
private void OnDeath()
{
    _motor.Stop();
    ReleaseFood();

    EntityHerd previousHerd = Herd;
    Herd = null;
    HerdId = "";

    if (GodotObject.IsInstanceValid(previousHerd) &&
        !previousHerd.IsQueuedForDeletion())
        previousHerd.Leave(this);

    QueueFree();
}

    // =========================================================
    // Clear feeding state and relinquish the target on interruption.
    private void ReleaseFood()
    {
        if (GodotObject.IsInstanceValid(_grazing))
            _grazing.Release(this, _food);
        _food = null;
        _eating = false;
        _eatTime = 0.0;
        _travelTime = 0.0;
    }

    // =========================================================
    // Accept only living, present actors with the shared health component.
    private static bool Living(Node2D actor)
    {
        return GodotObject.IsInstanceValid(actor) &&
            actor.IsInsideTree() && !actor.IsQueuedForDeletion() &&
            actor.GetNodeOrNull<Health>("Systems/Health")?.IsAlive == true;
    }
    #endregion

    // =========================================================
// Adopt a solo anchor at the survivor's current position without cancelling combat.
public void BecomeSolo(EntityHerd previousHerd)
{
    if (Herd != previousHerd) return;

    Herd = null;
    HerdId = "";
    Home = GlobalPosition;
    ReleaseFood();
    _wait = 0.0;

    if (!HasThreat)
        _motor?.Stop();
}

// =========================================================
// Reconsider peaceful movement after the shared centre changes.
public void ReconsiderWanderArea()
{
    if (HasThreat) return;

    // Keep feeding or approaching a tuft still inside the new shared area.
    if (GodotObject.IsInstanceValid(_food) &&
        !_food.IsQueuedForDeletion() &&
        _food.GlobalPosition.DistanceSquaredTo(Centre) <=
            ActiveWanderRadius * ActiveWanderRadius)
        return;

    ReleaseFood();
    _motor?.Stop();
    _wait = 0.0;
}
}