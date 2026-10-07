// Creates a placed object's solid footprint, terrain-adjusted artwork, and health.
// Uses the existing Obstacle family so navigation reacts to placement and removal.
using Godot;

public partial class PlacedObject : Obstacle
{
    #region State
    public Health ObjectHealth { get; private set; }

    private PlacementWorld _world;
    private PlaceableDefinition _definition;
    private Vector2I _anchor;
    #endregion

    #region Setup
    // =========================================================
    // Configure footprint dimensions before navigation discovers this object.
    public void Configure(
        PlacementWorld world, PlaceableDefinition definition, Vector2I anchor)
    {
        _world = world;
        _definition = definition;
        _anchor = anchor;
        Height = definition.CoverHeight;

        Vector2[] corners = world.Grid.Corners(anchor, definition.Cells);
        Vector2 low = corners[0], high = corners[0];

        foreach (Vector2 corner in corners)
        {
            low = new Vector2(
                Mathf.Min(low.X, corner.X), Mathf.Min(low.Y, corner.Y));
            high = new Vector2(
                Mathf.Max(high.X, corner.X), Mathf.Max(high.Y, corner.Y));
        }

        Footprint = high - low;
    }

    // =========================================================
    // Build the full cell footprint and attach artwork without atlas baking.
    public override void _Ready()
    {
        if (_world == null || _definition == null)
        {
            GD.PushError("PlacedObject must be configured by PlacementWorld.");
            QueueFree();
            return;
        }

        CollisionLayer = 1;
        CollisionMask = 0;

        Vector2 centre = _world.Grid.Centre(_anchor, _definition.Cells);
        Vector2[] points = _world.Grid.Corners(_anchor, _definition.Cells);
        for (int i = 0; i < points.Length; i++)
            points[i] = (points[i] - centre) * 0.98f;

        AddChild(new CollisionShape2D
        {
            Name = "Footprint",
            Shape = new ConvexPolygonShape2D { Points = points }
        });

        Node2D artwork = _definition.ArtworkScene.Instantiate<Node2D>();
        artwork.Name = "Visual";
        artwork.Position = Vector2.Up * _world.HeightAt(GlobalPosition);
        artwork.Scale *= _definition.ArtworkScale;
        AddChild(artwork);

        ObjectHealth = GetNode<Health>("Systems/Health");
        ObjectHealth.Died += OnDestroyed;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remove the object once health reaches zero.
    private void OnDestroyed()
    {
        QueueFree();
    }

    // =========================================================
    // Free occupied cells and disconnect health signals.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(ObjectHealth))
            ObjectHealth.Died -= OnDestroyed;

        if (GodotObject.IsInstanceValid(_world) && _definition != null)
            _world.Unregister(this, _anchor, _definition.Cells);
    }
    #endregion
}