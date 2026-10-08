// Follows cached routes while requesting replacements through shared navigation.
// Staggers planning and delays failed retries without delaying ordinary movement.
using Godot;
using System;

public partial class EnemyMotor : Node
{
    #region State
    private Enemy _actor;
    private GlobalConfig _config;
    private WorldNavigation _navigation;

    private Vector2 _goal, _requestGoal;
    private Vector2[] _path = Array.Empty<Vector2>();
    private int _pathIndex;
    private double _retryTimer;
    private float _speed, _stopDistance, _stalled;
    private bool _direct, _urgent, _failed;

    public bool HasGoal { get; private set; }
    public bool Arrived => HasGoal &&
        _actor.GlobalPosition.DistanceSquaredTo(_goal) <=
        _stopDistance * _stopDistance;
    public bool IsStuck => _stalled >= 2f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the actor and shared configuration.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Enemy>();
        _config = WorldConfig.Find(this);
    }

    // =========================================================
    // Remove queued work when this component leaves the scene.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_navigation))
            _navigation.Cancel(this);
    }
    #endregion

    #region Goals
    // =========================================================
    // Preserve routes while flagging significant goal changes for refresh.
    public void SetGoal(Vector2 goal, float speed, float stopDistance)
    {
        float threshold = Mathf.Max(16, _config.NavigationCellSize);
        if (!HasGoal ||
            _requestGoal.DistanceSquaredTo(goal) >= threshold * threshold)
            _urgent = true;

        HasGoal = true;
        _goal = goal;
        _speed = Mathf.Max(0f, speed);
        _stopDistance = Mathf.Max(1f, stopDistance);
    }

    // =========================================================
    // Clear movement and release outstanding navigation work.
    public void Stop()
    {
        if (GodotObject.IsInstanceValid(_navigation))
            _navigation.Cancel(this);

        HasGoal = false;
        _path = Array.Empty<Vector2>();
        _pathIndex = 0;
        _retryTimer = 0.0;
        _stalled = 0f;
        _direct = _urgent = _failed = false;

        if (GodotObject.IsInstanceValid(_actor))
            _actor.Velocity = Vector2.Zero;
    }
    #endregion

    #region Movement
    // =========================================================
    // Follow cached movement every physics tick while planning runs separately.
    public void Tick(double delta)
    {
        if (!HasGoal || delta <= 0.0) return;

        WorldNavigation navigation = WorldNavigation.For(_actor);
        if (navigation != _navigation)
        {
            if (GodotObject.IsInstanceValid(_navigation))
                _navigation.Cancel(this);

            _navigation = navigation;
            _path = Array.Empty<Vector2>();
            _pathIndex = 0;
            _retryTimer = 0.0;
            _direct = _failed = false;
            _urgent = true;
            _stalled = 0f;
        }

        if (_navigation == null || Arrived)
        {
            _actor.Velocity = Vector2.Zero;
            if (_navigation != null) _navigation.Cancel(this);
            return;
        }

        _retryTimer = Math.Max(0.0, _retryTimer - delta);
        TakeCompletedRoute();
        RequestRouteIfDue();

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
                _urgent = true;
                if (!_navigation.HasPending(this))
                    _stalled += (float)delta;
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
        {
            _urgent = true;
            if (!_navigation.HasPending(this))
                _stalled += (float)delta;
        }
        else
            _stalled = 0f;

        if (_actor.GetSlideCollisionCount() > 0)
            _urgent = true;
    }
    #endregion

    #region Route Requests
    // =========================================================
    // Adopt finished routes without performing physics queries or A* here.
    private void TakeCompletedRoute()
    {
        if (!_navigation.TryTakeRoute(
            this, out WorldNavigation.RouteResult result)) return;

        // A substantially different destination needs another request.
        if (result.Goal.DistanceSquaredTo(_goal) > 96f * 96f)
        {
            _urgent = true;
            return;
        }

        if (result.NoRoute)
        {
            _direct = false;
            _failed = true;
            _retryTimer = Math.Max(
                0.25, _config.NavigationFailedRetrySeconds);
            return;
        }

        _failed = false;
        _retryTimer = 0.0;
        _direct = result.Direct;
        _path = result.Path;
        _pathIndex = 0;
        _stalled = 0f;
    }

    // =========================================================
    // Stagger requests and avoid rebuilding failed routes every frame.
    private void RequestRouteIfDue()
    {
        if (_navigation.HasPending(this)) return;

        float threshold = _navigation.CellSize;
        bool movedGoal = _requestGoal.DistanceSquaredTo(_goal) >=
            threshold * threshold;
        bool changedArea = _navigation.FailedAreaChanged(this);

        if (_failed && _retryTimer > 0.0 &&
            !movedGoal && !changedArea)
            return;

        if (movedGoal || changedArea ||
            (_failed && _retryTimer <= 0.0))
            _urgent = true;

        bool due = _urgent
            ? StaggeredUpdate.Due(
                this, _config.NavigationStaggerTicks, 1)
            : StaggeredUpdate.DueSeconds(
                this, _actor.Definition.PathInterval, 1);

        if (!due) return;

        _requestGoal = _goal;
        _urgent = false;
        _navigation.RequestRoute(this, _actor, _goal);
    }
    #endregion
}