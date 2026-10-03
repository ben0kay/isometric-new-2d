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
    // Configure collision movement and wait for the shared artwork bake.
    public override async void _Ready()
    {
        MotionMode = MotionModeEnum.Floating;
        Material = PlaceholderAtlas.BakedMaterial;
        TextureFilter = TextureFilterEnum.Nearest;
        SetPhysicsProcess(false);

        try
        {
            await PlaceholderAtlas.EnsureReady(this);
            if (!IsInsideTree()) return;
            QueueRedraw();
            SetPhysicsProcess(true);
        }
        catch (System.Exception error)
        {
            GD.PushError($"Enemy initialization failed: {error}");
        }
    }

    // =========================================================
    // Refresh targeting and navigation periodically, then follow the current route.
    public override void _PhysicsProcess(double delta)
    {
        if (_navigation == null)
            _navigation = GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
        if (_navigation == null) return;

        _targetTimer -= delta;
        _pathTimer -= delta;
        if (_targetTimer <= 0.0 || !IsValidTarget(_target))
        {
            _targetTimer = System.Math.Max(0.1, TargetInterval);
            SelectTarget();
        }

        if (!IsValidTarget(_target))
        {
            Velocity = Vector2.Zero;
            return;
        }

        Vector2 targetPoint = _target.GlobalPosition;
        if (GlobalPosition.DistanceSquaredTo(targetPoint) <= StopDistance * StopDistance)
        {
            Velocity = Vector2.Zero;
            return;
        }

        if (_pathTimer <= 0.0)
        {
            _pathTimer = System.Math.Max(0.1, PathInterval);
            _direct = _navigation.CanTravelDirectly(GlobalPosition, targetPoint);
            _path = _direct ? System.Array.Empty<Vector2>() : _navigation.FindPath(GlobalPosition, targetPoint);
            _pathIndex = 0;
        }

        Vector2 destination;
        if (_direct)
        {
            destination = targetPoint;
        }
        else
        {
            while (_pathIndex < _path.Length && GlobalPosition.DistanceSquaredTo(_path[_pathIndex]) < 16f)
                _pathIndex++;
            if (_pathIndex >= _path.Length)
            {
                Velocity = Vector2.Zero;
                return;
            }
            destination = _path[_pathIndex];
        }

        Vector2 difference = destination - GlobalPosition;
        float distance = difference.Length();
        float speed = Mathf.Min(MoveSpeed, distance / (float)delta);
        if (_direct)
            speed = Mathf.Min(speed, Mathf.Max(0f, distance - StopDistance) / (float)delta);

        Velocity = distance > 0.001f ? difference / distance * speed : Vector2.Zero;
        MoveAndSlide();

        // A collision requests a fresh route on the next physics tick.
        if (GetSlideCollisionCount() > 0) _pathTimer = 0.0;
    }
    #endregion

    #region Targeting
    // =========================================================
    // Validate a registered player without assuming a fixed scene-tree path.
    private bool IsValidTarget(Player player)
    {
        return GodotObject.IsInstanceValid(player) && player.IsInsideTree() &&
            !player.IsQueuedForDeletion() &&
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

    #region Drawing
    // =========================================================
    // Draw one baked drone texture with its ground origin at the node position.
    public override void _Draw()
    {
        if (PlaceholderAtlas.Texture == null) return;
        DrawTextureRectRegion(
            PlaceholderAtlas.Texture, new Rect2(-48, -64, 96, 96),
            PlaceholderAtlas.EnemyRegion
        );
    }
    #endregion
}