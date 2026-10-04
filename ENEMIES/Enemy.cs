// Runs a solo enemy's nearest-player targeting and obstacle-aware chase.
// Rendering reuses the baked atlas; world navigation is supplied by a shared service.
using Godot;

public partial class Enemy : CharacterBody2D
{
    #region Configuration
    [Export] public float MoveSpeed { get; set; } = 170f;
    [Export] public float DetectionRange { get; set; } = 900f;
    [Export] public float StopDistance { get; set; } = 30f;
    [Export] public double TargetInterval { get; set; } = 0.35;
    [Export] public double PathInterval { get; set; } = 0.4;
    #endregion

    #region State
    private WorldNavigation _navigation;
    private Player _target;
    private Vector2[] _path = System.Array.Empty<Vector2>();
    private int _pathIndex;
    private double _targetTimer, _pathTimer;
    private bool _direct;
    #endregion

    #region Lifecycle
// =========================================================
// Prepare collision movement and attach terrain-adjusted drone artwork.
public override async void _Ready()
{
    MotionMode = MotionModeEnum.Floating;
    SetPhysicsProcess(false);

    try
    {
        await PlaceholderAtlas.EnsureReady(this);
        if (!IsInsideTree()) return;

        TerrainVisual.Attach(
            this, PlaceholderAtlas.EnemyRegion,
            new Vector2(-48, -64), Vector2.One, true
        );
        SetPhysicsProcess(true);
    }
    catch (System.Exception error)
    {
        GD.PushError($"Enemy initialization failed: {error}");
    }
}

    // =========================================================
// Refresh targeting and follow reachable waypoints without backtracking
// toward the starting grid cell whenever the route is rebuilt.
public override void _PhysicsProcess(double delta)
{
    if (_navigation == null)
        _navigation = GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
    if (_navigation == null || delta <= 0.0) return;

    _targetTimer -= delta;
    _pathTimer -= delta;

    if (_targetTimer <= 0.0 || !IsValidTarget(_target))
    {
        _targetTimer = System.Math.Max(0.1, TargetInterval);
        SelectTarget();
    }
    if (!IsValidTarget(_target)) { Velocity = Vector2.Zero; return; }

    Vector2 position = GlobalPosition;
    Vector2 targetPoint = _target.GlobalPosition;
    if (position.DistanceSquaredTo(targetPoint) <= StopDistance * StopDistance)
    { Velocity = Vector2.Zero; return; }

    if (_pathTimer <= 0.0)
    {
        _pathTimer = System.Math.Max(0.1, PathInterval);
        _direct = _navigation.CanTravelDirectly(position, targetPoint);
        _path = _direct
            ? System.Array.Empty<Vector2>()
            : _navigation.FindPath(position, targetPoint);
        _pathIndex = 0;

        // Skip the starting cell when a later waypoint is directly reachable.
        // Limit the checks to keep route refreshes inexpensive.
        if (!_direct && _path.Length > 1)
        {
            int lastCandidate = System.Math.Min(_path.Length - 1, 6);
            for (int i = 1; i <= lastCandidate; i++)
            {
                if (!_navigation.CanTravelDirectly(position, _path[i])) break;
                _pathIndex = i;
            }
        }
    }

    Vector2 destination;
    if (_direct) destination = targetPoint;
    else
    {
        // Advance past reached waypoints; 16 is a squared 4-pixel distance.
        while (_pathIndex < _path.Length &&
            position.DistanceSquaredTo(_path[_pathIndex]) < 16f)
            _pathIndex++;

        if (_pathIndex >= _path.Length) { Velocity = Vector2.Zero; return; }
        destination = _path[_pathIndex];
    }

    Vector2 difference = destination - position;
    float distance = difference.Length();
    float speed = Mathf.Min(MoveSpeed, distance / (float)delta);
    if (_direct)
        speed = Mathf.Min(speed, Mathf.Max(0f, distance - StopDistance) / (float)delta);

    Velocity = distance > 0.001f ? difference / distance * speed : Vector2.Zero;
    MoveAndSlide();

    // Request an earlier retry without rebuilding the route every physics tick.
    if (GetSlideCollisionCount() > 0)
        _pathTimer = System.Math.Min(_pathTimer, 0.1);
}
    #endregion

    #region Targeting

// =========================================================
// Accept only living players within detection range.
private bool IsValidTarget(Player player)
{
    if (!GodotObject.IsInstanceValid(player) || !player.IsInsideTree() ||
        player.IsQueuedForDeletion()) return false;

    Health health = player.GetNodeOrNull<Health>("Systems/Health");
    return health != null && health.IsAlive &&
        GlobalPosition.DistanceSquaredTo(player.GlobalPosition) <= DetectionRange * DetectionRange;
}

    // =========================================================
    // Pick the nearest player while applying a preference for the current target.
    private void SelectTarget()
    {
        Player next = IsValidTarget(_target) ? _target : null;
        float bestScore = next != null
            ? GlobalPosition.DistanceSquaredTo(next.GlobalPosition) * 0.64f
            : DetectionRange * DetectionRange;

        foreach (Node node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player player || !IsValidTarget(player)) continue;
            float score = GlobalPosition.DistanceSquaredTo(player.GlobalPosition);
            if (score >= bestScore) continue;
            next = player;
            bestScore = score;
        }

        if (next == _target) return;
        _target = next;
        _pathTimer = 0.0;
        _path = System.Array.Empty<Vector2>();
    }
    #endregion

}