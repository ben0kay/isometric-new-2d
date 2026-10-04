// Displays a baked atlas sprite at the terrain's visual height.
// Moving artwork updates only when its owner's world position changes.
using Godot;

public partial class TerrainVisual : Sprite2D
{
    #region State
    private TerrainElevation _elevation;
    private Node2D _host;
    private Vector2 _lastPosition;
    private bool _sampled;
    public bool FollowMovement { get; set; }
    #endregion

    #region Creation
    // =========================================================
    // Attach one reusable visual without moving the owner's collision body.
    public static TerrainVisual Attach(
        Node2D host, Rect2 region, Vector2 origin, Vector2 scale, bool followMovement)
    {
        TerrainVisual visual = new()
        {
            Name = "Visual",
            Texture = new AtlasTexture
            {
                Atlas = PlaceholderAtlas.Texture,
                Region = region
            },
            Centered = false,
            Offset = origin,
            Scale = scale,
            Material = PlaceholderAtlas.BakedMaterial,
            TextureFilter = TextureFilterEnum.Nearest,
            FollowMovement = followMovement
        };
        host.AddChild(visual);
        return visual;
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the shared height service and position the artwork once.
    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _elevation = GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
        UpdateHeight();
        SetProcess(FollowMovement);
    }

    // =========================================================
    // Follow movement without rebuilding sprites or redrawing primitives.
    public override void _Process(double delta)
    {
        UpdateHeight();
    }

    // =========================================================
    // Skip height sampling when the owner has not moved.
    private void UpdateHeight()
    {
        Vector2 point = _host.GlobalPosition;
        if (_sampled && point == _lastPosition) return;

        _sampled = true;
        _lastPosition = point;
        float height = _elevation != null ? _elevation.SampleWorldHeight(point) : 0f;
        Position = new Vector2(0f, -height);
    }
    #endregion
}