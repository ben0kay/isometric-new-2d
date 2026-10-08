// Shares screen culling and eclipse fading across polygon and sprite shadows.
// Contact shadows remain visible underground; sunlight shadows do not.
using Godot;

public partial class SunShadow : Node
{
    #region State
    private Node2D _drawing;
    private Rect2 _bounds;
    private bool _sunlight = true;
    private bool _boundMaterial;
    private WorldLayerController _layers;

    private static readonly CanvasItemMaterial ContactMaterial = new()
    {
        LightMode = CanvasItemMaterial.LightModeEnum.Unshaded
    };
    #endregion

    #region Installation
    // =========================================================
    // Preserve the existing polygon-shadow attachment API.
    public static void Attach(Node2D owner, Polygon2D polygon)
    {
        Rect2 bounds = new(polygon.Polygon[0], Vector2.Zero);
        foreach (Vector2 point in polygon.Polygon)
            bounds = bounds.Expand(point);

        Install(owner, polygon, bounds, true);
    }

    // =========================================================
    // Attach a projected silhouette or soft contact shadow.
    public static void Attach(
        Node2D owner, Sprite2D sprite, bool sunlight = true)
    {
        Install(owner, sprite, sprite.GetRect(), sunlight);
    }

    // =========================================================
    // Keep visibility processing separate from the drawing it hides.
    private static void Install(
        Node2D owner, Node2D drawing, Rect2 bounds, bool sunlight)
    {
        owner.AddChild(drawing);
        owner.AddChild(new SunShadow
        {
            Name = drawing.Name + "Culling",
            _drawing = drawing,
            _bounds = bounds,
            _sunlight = sunlight
        });
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Hide the drawing until its visibility and material are resolved.
    public override void _Ready()
    {
        SetProcess(false);
        _drawing.Visible = false;
        Refresh();
    }

    // =========================================================
    // Spread visibility checks across existing physics ticks.
    public override void _PhysicsProcess(double delta)
    {
        if (StaggeredUpdate.DueSeconds(this, 0.1, 7))
            Refresh();
    }
    #endregion

    #region Visibility
    // =========================================================
    // Cull by the shadow's bounds, independently of the artwork's bounds.
    private void Refresh()
    {
        if (!_boundMaterial)
        {
            if (!_sunlight)
            {
                _drawing.Material = ContactMaterial;
                _boundMaterial = true;
            }
            else
            {
                WorldEclipse eclipse = WorldEclipse.Find(this);
                if (eclipse?.ShadowMaterial != null)
                {
                    _drawing.Material = eclipse.ShadowMaterial;
                    _boundMaterial = true;
                }
            }
        }

        _layers ??= WorldLayerController.Find(this);

        if (!GetParent<Node2D>().IsVisibleInTree() ||
            (_sunlight && _layers != null &&
             _layers.Current != WorldLayer.Surface))
        {
            _drawing.Visible = false;
            return;
        }

        Rect2 bounds = _drawing is Sprite2D sprite
            ? sprite.GetRect() : _bounds;

        Transform2D transform = _drawing.GetGlobalTransformWithCanvas();
        Rect2 screen = new(transform * bounds.Position, Vector2.Zero);

        screen = screen.Expand(transform *
            new Vector2(bounds.End.X, bounds.Position.Y));
        screen = screen.Expand(transform * bounds.End);
        screen = screen.Expand(transform *
            new Vector2(bounds.Position.X, bounds.End.Y));

        _drawing.Visible = screen.Intersects(
            GetViewport().GetVisibleRect().Grow(48f));
    }
    #endregion
}