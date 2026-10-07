// Follows logical ground goals through the shared world navigation service.
// Preserves an existing route while navigation rebuilds and avoids waypoint backtracking.
using Godot;
using System;

public partial class EnemyMotor : Node
{
    #region State
    private Enemy _actor;
    private WorldNavigation _navigation;
    private Vector2 _goal;
    private Vector2[] _path = Array.Empty<Vector2>();
    private int _pathIndex;
    private double _pathTimer;
    private float _speed, _stopDistance, _stalled;
    private bool _direct;

    public bool HasGoal { get; private set; }
    public bool Arrived => HasGoal &&
        _actor.GlobalPosition.DistanceSquaredTo(_goal) <= _stopDistance * _stopDistance;
    public bool IsStuck => _stalled >= 2f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the owning actor; the actor drives this component's updates.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Enemy>();
    }
    #endregion

    #region Goals
    // =========================================================
    // Update a goal without forcing a rebuild for every small target movement.
    public void SetGoal(Vector2 goal, float speed, float stopDistance)
    {
        if (!HasGoal || _goal.DistanceSquaredTo(goal) > 96f * 96f)
            _pathTimer = 0.0;

        HasGoal = true;
        _goal = goal;
        _speed = speed;
        _stopDistance = Mathf.Max(1f, stopDistance);
    }

    // =========================================================
    // Clear movement when the brain wants to idle or hold attack distance.
    public void Stop()
    {
        HasGoal = false;
        _path = Array.Empty<Vector2>();
        _pathIndex = 0;
        _stalled = 0f;
        _direct = false;
        _actor.Velocity = Vector2.Zero;
    }
    #endregion

    #region Movement
// =========================================================
// Follow the actor's layer navigation and constrain movement to loaded ground.
public void Tick(double delta)
{
    if (!HasGoal || delta <= 0.0) return;

    WorldNavigation navigation = WorldNavigation.For(_actor);
    if (navigation != _navigation)
    {
        _navigation = navigation;
        _path = Array.Empty<Vector2>();
        _pathIndex = 0;
        _pathTimer = 0.0;
        _direct = false;
        _stalled = 0f;
    }

    if (_navigation == null || Arrived)
    {
        _actor.Velocity = Vector2.Zero;
        return;
    }

    _pathTimer -= delta;
    if (_pathTimer <= 0.0) RefreshPath();

    Vector2 position = _actor.GlobalPosition;
    Vector2 destination = _goal;

    if (!_direct)
    {
        while (_pathIndex < _path.Length &&
            position.DistanceSquaredTo(_path[_pathIndex]) < 16f)
            _pathIndex++;

        if (_pathIndex >= _path.Length)
        {
            _actor.Velocity = Vector2.Zero;
            if (!_navigation.IsBuilding) _stalled += (float)delta;
            return;
        }

        destination = _path[_pathIndex];
    }

    Vector2 difference = destination - position;
    float distance = difference.Length();
    float remaining = _direct
        ? Mathf.Max(0f, distance - _stopDistance) : distance;
    float speed = Mathf.Min(_speed, remaining / (float)delta);

    Vector2 velocity = distance > 0.001f
        ? difference / distance * speed : Vector2.Zero;
    _actor.Velocity = _navigation.ConstrainVelocity(
        position, velocity, delta);
    _actor.MoveAndSlide();

    if (_actor.GlobalPosition.DistanceSquaredTo(position) < 0.01f)
        _stalled += (float)delta;
    else
        _stalled = 0f;

    if (_actor.GetSlideCollisionCount() > 0)
        _pathTimer = Math.Min(_pathTimer, 0.1);
}

    // =========================================================
    // Refresh direct travel or A* while retaining routes during incremental builds.
    private void RefreshPath()
    {
        _pathTimer = _actor.Definition.PathInterval;
        Vector2 position = _actor.GlobalPosition;
        _direct = _navigation.CanTravelDirectly(position, _goal);

        if (_direct)
        {
            _path = Array.Empty<Vector2>();
            _pathIndex = 0;
            return;
        }

        Vector2[] next = _navigation.FindPath(position, _goal);
        if (_navigation.IsBuilding) { _pathTimer = 0.1; return; }

        _path = next;
        _pathIndex = 0;
        int last = Math.Min(_path.Length - 1, 6);
        for (int i = 1; i <= last; i++)
        {
            if (!_navigation.CanTravelDirectly(position, _path[i])) break;
            _pathIndex = i;
        }
    }
    #endregion
}