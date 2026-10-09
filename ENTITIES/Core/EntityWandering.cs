// Shares wandering between robots, wildlife and future entity types.
// Grazing and group roaming are optional; membership alone does not move an anchor.
using Godot;
using System;

public partial class EntityWandering : Node
{
    #region State
    public Vector2 Home { get; private set; }

    private EntityBody _actor;
    private EntityGroupMember _membership;
    private EntityGrazing _grazing;
    private Func<bool> _hasThreat;
    private RandomNumberGenerator _rng;
    private bool _ownsRandom;
    private EntityWanderSettings _settings;
    private double _wait, _travelTime;

    private GroupRoaming SharedArea
    {
        get
        {
            EntityGroup group = _membership?.Group;
            return GodotObject.IsInstanceValid(group) &&
                !group.IsQueuedForDeletion() ? group.Roaming : null;
        }
    }

    public bool UsesSharedArea => GodotObject.IsInstanceValid(SharedArea);
    public Vector2 Centre => UsesSharedArea ? SharedArea.Centre : Home;
    public float Radius => UsesSharedArea
        ? SharedArea.WanderRadius : _settings.Radius;

    private bool HasThreat => _hasThreat?.Invoke() == true;
    #endregion

    #region Lifecycle
    // =========================================================
    // Adapt the existing wildlife definition to the shared movement behaviour.
    public void Bind(Entity actor)
    {
        EntityDefinition definition = actor.Definition;

        Bind(actor, new EntityWanderSettings
        {
            Enabled = true,
            Speed = definition.WanderSpeed,
            Radius = definition.WanderRadius,
            Wait = definition.WanderWait,
            ArrivalDistance = 12f,
            ReturnDistance = 16f,
            RequireDirectPath = true,
            TickMotor = true
        }, actor.GlobalPosition, actor.Membership, actor.Grazing,
            () => actor.HasThreat);
    }

    // =========================================================
    // Bind any shared actor with optional grouping, grazing and threat policy.
    public void Bind(
        EntityBody actor, EntityWanderSettings settings, Vector2 home,
        EntityGroupMember membership = null,
        EntityGrazing grazing = null,
        Func<bool> hasThreat = null,
        RandomNumberGenerator random = null,
        bool initialPause = false)
    {
        _actor = actor;
        _settings = settings;
        Home = home;
        _membership = membership;
        _grazing = grazing;
        _hasThreat = hasThreat;
        _rng = random;
        _ownsRandom = random == null;

        if (_ownsRandom)
        {
            _rng = new RandomNumberGenerator();
            _rng.Randomize();
        }

        if (_membership != null)
        {
            _membership.GroupChanged += OnGroupChanged;
            _membership.AreaChanged += OnAreaChanged;
        }

        if (initialPause) Pause();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release optional subscriptions and owned random resources.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_membership))
        {
            _membership.GroupChanged -= OnGroupChanged;
            _membership.AreaChanged -= OnAreaChanged;
        }

        if (_ownsRandom) _rng?.Dispose();
    }

    // =========================================================
    // Move the solo home anchor when the actor changes world layers.
    public void SetHome(Vector2 position)
    {
        Home = position;
    }
    #endregion

    #region Movement
    // =========================================================
    // Advance waiting and optionally drive wildlife movement and feeding.
    public void Tick(double delta)
    {
        _wait -= delta;

        // Robot coordination already ticks its motor after combat sequences.
        if (!_settings.TickMotor) return;

        if (_grazing?.Tick(delta) == true || !_actor.Motor.HasGoal)
            return;

        _travelTime += delta;
        _actor.Motor.Tick(delta);
        if (HasThreat) return;

        if (_actor.Motor.Arrived)
        {
            _actor.Motor.Stop();
            _travelTime = 0.0;
            if (_grazing?.BeginEating() != true) Pause();
        }
        else if (_actor.Motor.IsStuck || _travelTime > 20.0)
        {
            Interrupt();
            _wait = 2.0;
        }
    }

    // =========================================================
    // Choose optional forage or a destination within the current wander area.
    public void Decide()
    {
        EnemyMotor motor = _actor.Motor;

        if (!_settings.Enabled)
        {
            motor.Stop();
            return;
        }

        Vector2 centre = Centre;
        float radius = Radius;
        Vector2 position = _actor.GlobalPosition;

        if (_settings.HomeLeash > 0f &&
            position.DistanceSquaredTo(centre) >
                _settings.HomeLeash * _settings.HomeLeash)
        {
            _wait = 0.0;
            ReturnToCentre();
            return;
        }

        if (_grazing?.HasTarget == true)
        {
            if (_grazing.TargetInside(centre, radius)) return;
            Interrupt();
        }

        if (motor.HasGoal)
        {
            if (_settings.TickMotor ||
                (!motor.Arrived && !motor.IsStuck))
                return;

            motor.Stop();
            Pause();
        }

        // Wildlife returns to its area before waiting; robots preserve their wait.
        if (_settings.TickMotor &&
            position.DistanceSquaredTo(centre) > radius * radius)
        {
            ReturnToCentre();
            return;
        }

        if (_wait > 0.0) return;

        if (position.DistanceSquaredTo(centre) > radius * radius)
        {
            ReturnToCentre();
            return;
        }

        WorldNavigation navigation = _actor.Navigation;
        if (navigation == null) return;

        if (_grazing?.TryReserve(centre, radius) == true)
        {
            _travelTime = 0.0;
            motor.SetGoal(_grazing.TargetPosition, _settings.Speed, 18f);
            return;
        }

        for (int attempt = 0; attempt < 4; attempt++)
        {
            Vector2 point = centre +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
                Mathf.Sqrt(_rng.Randf()) * radius;

            Vector2 start = _settings.RequireDirectPath ? position : point;
            if (!navigation.CanTravelDirectly(start, point))
                continue;

            _travelTime = 0.0;
            motor.SetGoal(
                point, _settings.Speed, _settings.ArrivalDistance);
            return;
        }

        _wait = _settings.TickMotor ? 2.0 : 1.0;
    }

    // =========================================================
    // Return to the solo home or optional shared roaming centre.
    public void ReturnToCentre()
    {
        _travelTime = 0.0;
        _actor.Motor.SetGoal(
            Centre, _settings.Speed, _settings.ReturnDistance);
    }

    // =========================================================
    // Pause between destinations or after an optional meal.
    public void Pause()
    {
        _wait = _rng.RandfRange(_settings.Wait.X, _settings.Wait.Y);
    }

    // =========================================================
    // Release peaceful activity while optionally preserving combat movement.
    public void Interrupt(bool preserveCombat = true)
    {
        _grazing?.Release();
        _travelTime = 0.0;
        _wait = 0.0;

        if (!preserveCombat || !HasThreat)
            _actor.Motor.Stop();
    }
    #endregion

    #region Group Events
    // =========================================================
    // Establish a solo anchor when a shared roaming area is lost.
    private void OnGroupChanged()
    {
        if (!UsesSharedArea)
            Home = _actor.GlobalPosition;

        Interrupt();
    }

    // =========================================================
    // Reconsider peaceful movement when an optional group anchor changes.
    private void OnAreaChanged()
    {
        if (HasThreat || _grazing?.TargetInside(Centre, Radius) == true)
            return;

        Interrupt();
    }
    #endregion
}