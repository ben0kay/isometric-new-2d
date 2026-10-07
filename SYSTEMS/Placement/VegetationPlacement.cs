// Gives walkable vegetation a ground footprint specifically for building placement.
// Plants block building; grass is removable without blocking player movement.
using Godot;

public partial class VegetationPlacement : Area2D
{
    #region Layers
    public const uint PlantLayer = 1u << 8;
    public const uint GrassLayer = 1u << 9;
    #endregion

    #region State
    public Node2D Host { get; private set; }
    public bool IsGrass { get; private set; }
    private Vector2 _size;
    #endregion

    #region Attachment
    // =========================================================
    // Add one non-processing placement footprint to a vegetation instance.
    public static void Attach(Node2D host, Vector2 size, bool grass)
    {
        if (host.GetNodeOrNull<VegetationPlacement>("PlacementFootprint") != null)
            return;

        host.AddChild(new VegetationPlacement
        {
            Name = "PlacementFootprint",
            Host = host,
            IsGrass = grass,
            _size = new Vector2(
                Mathf.Max(2f, size.X), Mathf.Max(2f, size.Y))
        });
    }

    // =========================================================
    // Create an elliptical ground footprint independent of visible artwork.
    public override void _Ready()
    {
        CollisionLayer = IsGrass ? GrassLayer : PlantLayer;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;

        Vector2[] points = new Vector2[16];
        for (int i = 0; i < points.Length; i++)
        {
            float angle = Mathf.Tau * i / points.Length;
            points[i] = new Vector2(
                Mathf.Cos(angle) * _size.X * 0.5f,
                Mathf.Sin(angle) * _size.Y * 0.5f);
        }

        AddChild(new CollisionShape2D
        {
            Shape = new ConvexPolygonShape2D { Points = points }
        });

        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion
}