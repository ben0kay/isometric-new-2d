// Culls fixed sun-shadow polygons and attaches their shared eclipse material.
// Visibility checks are staggered; polygon geometry is never rebuilt.
using Godot;

public partial class SunShadow : Node
{
    #region State
    private Polygon2D _polygon;
    private Rect2 _bounds;
    private bool _boundMaterial;
    #endregion

    #region Installation
    // =========================================================
    // Keep the helper separate from the polygon it hides.
    public static void Attach(Node2D owner, Polygon2D polygon)
    {
        owner.AddChild(polygon);
        owner.AddChild(new SunShadow
        {
            Name = "SunShadowCulling",
            _polygon = polygon
        });
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Cache the polygon's local bounds once.
    public override void _Ready()
    {
        Vector2[] points = _polygon.Polygon;
        _bounds = new Rect2(points[0], Vector2.Zero);

        foreach (Vector2 point in points)
            _bounds = _bounds.Expand(point);

        _polygon.Visible = false;
        Refresh();
    }

    // =========================================================
    // Distribute screen checks across existing physics ticks.
    public override void _PhysicsProcess(double delta)
    {
        if (StaggeredUpdate.DueSeconds(this, 0.1, 7))
            Refresh();
    }
    #endregion

    #region Visibility
    // =========================================================
    // Test shadow bounds without applying terrain elevation twice.
    private void Refresh()
    {
        if (!_boundMaterial)
        {
            WorldEclipse eclipse = WorldEclipse.Find(this);
            if (eclipse?.ShadowMaterial != null)
            {
                _polygon.Material = eclipse.ShadowMaterial;
                _boundMaterial = true;
            }
        }

        Node2D owner = GetParent<Node2D>();
        if (!owner.IsVisibleInTree())
        {
            _polygon.Visible = false;
            return;
        }

        Transform2D transform = _polygon.GetGlobalTransformWithCanvas();
        Rect2 screen = new(transform * _bounds.Position, Vector2.Zero);

        screen = screen.Expand(transform *
            new Vector2(_bounds.End.X, _bounds.Position.Y));
        screen = screen.Expand(transform * _bounds.End);
        screen = screen.Expand(transform *
            new Vector2(_bounds.Position.X, _bounds.End.Y));

        _polygon.Visible = screen.Intersects(
            GetViewport().GetVisibleRect().Grow(48f));
    }
    #endregion
}