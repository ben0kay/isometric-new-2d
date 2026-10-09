// Creates a placed object's solid footprint, terrain-adjusted artwork, and health.
// Uses the existing Obstacle family so navigation reacts to placement and removal.
using Godot;

public partial class PlacedObject : Obstacle
{
    #region State
    public Health ObjectHealth { get; private set; }
    public string PersistentId { get; private set; }
    public ItemDefinition SourceItem { get; private set; }
    public PlaceableDefinition Definition => _definition;

    private PlacementWorld _world;
    private PlaceableDefinition _definition;
    private Vector2I _anchor;
    #endregion

    #region Setup
    // =========================================================
    // Configure footprint dimensions before navigation discovers this object.
    public void Configure(
        PlacementWorld world, PlaceableDefinition definition, Vector2I anchor,
        ItemDefinition sourceItem = null, string persistentId = null)
    {
        PersistentId = persistentId ?? System.Guid.NewGuid().ToString("N");
        SourceItem = sourceItem;
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
// Build placement collision, lit artwork, obstruction fading and health.
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

    CreateShadowAsync(points);

    Node2D artwork = _definition.ArtworkScene.Instantiate<Node2D>();
    artwork.Name = "Visual";
    artwork.Position = Vector2.Up * _world.HeightAt(GlobalPosition);
    artwork.Scale *= _definition.ArtworkScale;
    AddChild(artwork);

    Rect2? drawingBounds = null;
    Vector2[] outline = _definition.ObstructionOutline;

    if (outline != null && outline.Length > 0)
    {
        Rect2 bounds = new(outline[0], Vector2.Zero);
        foreach (Vector2 point in outline)
            bounds = bounds.Expand(point);

        if (bounds.Size.X > 0f && bounds.Size.Y > 0f)
            drawingBounds = bounds;
    }

    WorldLightingMaterials.Attach(this, artwork, null, drawingBounds);

    PlayerObstructionFade.Attach(
        this, artwork, _definition.ObstructionOutline);

    ObjectHealth = GetNode<Health>("Systems/Health");
    ObjectHealth.Died += OnDestroyed;

    SetProcess(false);
    SetPhysicsProcess(false);
}

    // =========================================================
    // Capture the source item's recipe, occupied cells and current building health.
    public StructureSaveData CaptureSave()
    {
        if (SourceItem == null)
            throw new System.IO.InvalidDataException("Placed object lacks its source item identity.");
        return new StructureSaveData
        {
            Id = PersistentId, Item = SourceItem.Id, X = _anchor.X, Y = _anchor.Y,
            Width = _definition.Cells.X, Depth = _definition.Cells.Y,
            WorldScene = _definition.WorldScene.ResourcePath,
            ArtworkScene = _definition.ArtworkScene.ResourcePath,
            Health = ObjectHealth.Current
        };
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

        // =========================================================
    // Wait for atmosphere initialization, then create one cached ground shadow.
    private async void CreateShadowAsync(Vector2[] footprint)
    {
        try
        {
            WorldAtmosphere atmosphere =
                GetTree().GetFirstNodeInGroup("world_atmosphere")
                as WorldAtmosphere;

            if (!GodotObject.IsInstanceValid(atmosphere)) return;

            Node systems = atmosphere.GetParent();

            if (!systems.IsNodeReady())
                await ToSignal(systems, Node.SignalName.Ready);

            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(atmosphere))
                return;

            atmosphere.CreateObstacleShadow(this, footprint);
        }
        catch (System.Exception error)
        {
            GD.PushError(
                $"Placed object '{Name}' shadow failed: {error}");
        }
    }
    #endregion
}
