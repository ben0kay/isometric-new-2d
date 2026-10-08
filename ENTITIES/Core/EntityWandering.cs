// Owns individual wandering and selects a solo or optional shared group area.
// Group changes are events; ordinary decisions retain the actor's staggered schedule.
using Godot;

public partial class EntityWandering : Node
{
    #region State
    public Vector2 Home { get; private set; }

    private Entity _actor;
    private readonly RandomNumberGenerator _rng = new();
    private double _wait, _travelTime;

    private GroupRoaming SharedArea
    {
        get
        {
            EntityGroup group = _actor.Membership.Group;
            return GodotObject.IsInstanceValid(group) &&
                !group.IsQueuedForDeletion() ? group.Roaming : null;
        }
    }

    public bool UsesSharedArea => GodotObject.IsInstanceValid(SharedArea);
    public Vector2 Centre => UsesSharedArea ? SharedArea.Centre : Home;
    public float Radius => UsesSharedArea
        ? SharedArea.WanderRadius : _actor.Definition.WanderRadius;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind movement and subscribe to generic membership notifications.
    public void Bind(Entity actor)
    {
        _actor = actor;
        Home = actor.GlobalPosition;
        _rng.Randomize();
        actor.Membership.GroupChanged += OnGroupChanged;
        actor.Membership.AreaChanged += OnAreaChanged;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release subscriptions and the native random generator.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Membership))
        {
            _actor.Membership.GroupChanged -= OnGroupChanged;
            _actor.Membership.AreaChanged -= OnAreaChanged;
        }
        _rng.Dispose();
    }
    #endregion

    #region Movement
    // =========================================================
    // Advance smooth movement while grazing and decisions remain separate.
    public void Tick(double delta)
    {
        _wait -= delta;
        if (_actor.Grazing.Tick(delta) || !_actor.Motor.HasGoal) return;

        _travelTime += delta;
        _actor.Motor.Tick(delta);
        if (_actor.HasThreat) return;

        if (_actor.Motor.Arrived)
        {
            _actor.Motor.Stop();
            _travelTime = 0.0;
            if (!_actor.Grazing.BeginEating()) Pause();
        }
        else if (_actor.Motor.IsStuck || _travelTime > 20.0)
        {
            Interrupt();
            _wait = 2.0;
        }
    }

    // =========================================================
    // Choose grass or a reachable destination inside the active wander area.
    public void Decide()
    {
        Vector2 centre = Centre;
        float radius = Radius;

        if (_actor.Grazing.HasTarget)
        {
            if (_actor.Grazing.TargetInside(centre, radius)) return;
            Interrupt();
        }

        if (_actor.Motor.HasGoal) return;

        WorldNavigation navigation = WorldNavigation.For(_actor);
        if (navigation == null) return;

        if (_actor.GlobalPosition.DistanceSquaredTo(centre) > radius * radius)
        {
            ReturnToCentre();
            return;
        }

        if (_wait > 0.0) return;

        if (_actor.Grazing.TryReserve(centre, radius))
        {
            _travelTime = 0.0;
            _actor.Motor.SetGoal(
                _actor.Grazing.TargetPosition,
                _actor.Definition.WanderSpeed, 18f);
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = centre +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
                Mathf.Sqrt(_rng.Randf()) * radius;

            if (!navigation.CanTravelDirectly(_actor.GlobalPosition, point))
                continue;

            _travelTime = 0.0;
            _actor.Motor.SetGoal(point, _actor.Definition.WanderSpeed, 12f);
            return;
        }

        _wait = 2.0;
    }

    // =========================================================
    // Return after disengagement using the current solo or group centre.
    public void ReturnToCentre()
    {
        _travelTime = 0.0;
        _actor.Motor.SetGoal(Centre, _actor.Definition.WanderSpeed, 16f);
    }

    // =========================================================
    // Pause between destinations or after finishing a meal.
    public void Pause()
    {
        _wait = _rng.RandfRange(
            _actor.Definition.WanderWait.X, _actor.Definition.WanderWait.Y);
    }

    // =========================================================
    // Release peaceful activity while optionally preserving an active combat goal.
    public void Interrupt(bool preserveCombat = true)
    {
        _actor.Grazing.Release();
        _travelTime = 0.0;
        _wait = 0.0;

        if (!preserveCombat || !_actor.HasThreat)
            _actor.Motor.Stop();
    }
    #endregion

    #region Group Events
    // =========================================================
    // Establish a new solo anchor when shared group roaming is lost.
    private void OnGroupChanged()
    {
        if (!UsesSharedArea)
            Home = _actor.GlobalPosition;

        Interrupt();
    }

    // =========================================================
    // Retain valid grass or reconsider peaceful movement after a centre step.
    private void OnAreaChanged()
    {
        if (_actor.HasThreat ||
            _actor.Grazing.TargetInside(Centre, Radius))
            return;

        Interrupt();
    }
    #endregion
}