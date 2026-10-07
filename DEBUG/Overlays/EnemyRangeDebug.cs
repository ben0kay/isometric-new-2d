// Retains one enemy's range drawings and follows its logical ground position.
// Circle geometry rebuilds only when configured ranges change.
using Godot;

public partial class EnemyRangeDebug : Node2D
{
    #region State
    private Enemy _enemy;
    private Vector3 _ranges = new(-1f, -1f, -1f);
    private Vector2[] _detection, _attack, _forget;
    private bool _enabled;

    private const int Segments = 64;
    private static readonly Color DetectionColor = new("#65e58b");
    private static readonly Color AttackColor = new("#ff7272");
    private static readonly Color ForgetColor = new("#e8bd68");
    #endregion

    #region Lifecycle
    // =========================================================
    // Draw independently of actor scale and above ordinary world artwork.
    public override void _Ready()
    {
        TopLevel = true;
        ZIndex = 4094;
        ZAsRelative = false;
        Visible = false;
        SetProcess(false);
    }

    // =========================================================
    // Move retained drawings without rebuilding their geometry or draw commands.
    public override void _Process(double delta)
    {
        UpdatePresentation();
    }
    #endregion

    #region Configuration
    // =========================================================
    // Cache changed radii while preserving existing drawings during movement.
    public void Configure(Enemy enemy, bool enabled)
    {
        _enemy = enemy;

        Vector3 ranges = new(
            enemy.Definition.DetectionRange,
            enemy.Definition.Combat?.AttackRange ?? 0f,
            enemy.Definition.ForgetRange);

        if (ranges != _ranges)
        {
            _ranges = ranges;
            _detection = MakeCircle(ranges.X);
            _attack = MakeCircle(ranges.Y);
            _forget = MakeCircle(ranges.Z);
            QueueRedraw();
        }

        SetEnabled(enabled);
    }

    // =========================================================
    // Stop debug updates while disabled without discarding cached geometry.
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        SetProcess(enabled);
        UpdatePresentation();
    }

    // =========================================================
    // Show only living, activated enemies in the player's current world layer.
    private void UpdatePresentation()
    {
        if (!_enabled || !GodotObject.IsInstanceValid(_enemy) ||
            _enemy.IsQueuedForDeletion() || !_enemy.IsInsideTree())
        {
            Visible = false;
            return;
        }

        WorldLayer current =
            WorldLayerController.Find(this)?.Current ?? WorldLayer.Surface;

        Visible = _enemy.IsActivated && !_enemy.SpawnPending &&
            _enemy.Health?.IsAlive == true &&
            _enemy.IsVisibleInTree() &&
            WorldLayerMember.For(_enemy) == current;

        GlobalTransform = new Transform2D(0f, _enemy.GlobalPosition);
    }
    #endregion

    #region Drawing
    // =========================================================
    // Build a closed circle only when its radius changes.
    private static Vector2[] MakeCircle(float radius)
    {
        if (!float.IsFinite(radius) || radius <= 0f)
            return System.Array.Empty<Vector2>();

        Vector2[] points = new Vector2[Segments + 1];

        for (int i = 0; i < Segments; i++)
            points[i] = Vector2.FromAngle(
                Mathf.Tau * i / Segments) * radius;

        points[Segments] = points[0];
        return points;
    }

    // =========================================================
    // Record cached outlines; movement does not request another redraw.
    public override void _Draw()
    {
        if (_forget?.Length > 1)
            DrawPolyline(_forget, ForgetColor, 1f, true);

        if (_detection?.Length > 1)
            DrawPolyline(_detection, DetectionColor, 1.5f, true);

        if (_attack?.Length > 1)
            DrawPolyline(_attack, AttackColor, 2f, true);
    }
    #endregion
}