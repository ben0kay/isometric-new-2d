// Shows a local building grid and artwork-only placement preview.
// Reads centralized player controls and consumes items through inventory transactions.
using Godot;
using System.Collections.Generic;

public partial class PlayerPlacement : Node2D
{
    #region Configuration
    [ExportGroup("Placement")]
    [Export(PropertyHint.Range, "64,1024,16")]
    public float PlacementRange { get; set; } = 320f;

    [Export(PropertyHint.Range, "1,6,1")]
    public int GridRadiusCells { get; set; } = 4;

    [Export(PropertyHint.Range, "0.05,0.5,0.05")]
    public double PreviewInterval { get; set; } = 0.1;

    [ExportGroup("Testing")]
    [Export] public ItemDefinition StartingItem { get; set; }
    [Export] public string StartingItemId { get; set; } = "";
    [Export(PropertyHint.Range, "0,100,1")]
    public int StartingCount { get; set; } = 12;
    #endregion

    #region State
    private Player _player;
    private PlayerHotbar _hotbar;
    private Health _health;
    private InventoryHud _hud;
    private PlacementWorld _world;

    private PlaceableDefinition _definition;
    private Node2D _preview;
    private Vector2I _cell;
    private bool _valid;
    private double _refresh;
    private readonly List<Vector2[]> _gridLines = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve existing components without adding another processing loop.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        _hotbar = GetNode<PlayerHotbar>("../Hotbar");
        _health = GetNode<Health>("../Health");
        _hud = _player.GetNode<InventoryHud>("InventoryHud");
        ZIndex = 1;

        SetProcess(false);
        SetPhysicsProcess(false);

        // Deferred so inventory and hotbar finish their initial setup first.
        Callable.From(GiveStartingItems).CallDeferred();
    }

// =========================================================
// Resolve a starting item through the catalog or use an explicit resource.
private void GiveStartingItems()
{
    if (StartingCount <= 0) return;

    ItemDefinition item = StartingItem;
    if (item == null && !string.IsNullOrWhiteSpace(StartingItemId))
    {
        ItemCatalog catalog = GD.Load<ItemCatalog>(
            "res://ITEMS/ItemCatalog.tres");

        if (catalog == null)
        {
            GD.PushError("Starting items require the master ItemCatalog.");
            return;
        }

        catalog.Initialize();
        item = catalog.Get(StartingItemId);

        if (item == null)
        {
            GD.PushError($"Unknown starting item: '{StartingItemId}'.");
            return;
        }
    }

    if (item != null)
        GetNode<PlayerInventory>("../Inventory")
            .TryCollect(item, StartingCount);
}
    #endregion

    #region Preview And Placement
    // =========================================================
    // Update preview state and place once per valid LMB press.
    public void Tick(double delta)
    {
        ItemDefinition item = _hotbar.CurrentItem;
        PlaceableDefinition definition = item?.Placeable;

        bool allowed = definition != null && _health.IsAlive &&
            InputModes.For(_player).GameplayAllowed &&
            !_hud.BlocksWorldMovement && !_hud.BlocksWorldAttack() &&
            WorldLayerMember.For(_player) == WorldLayer.Surface;

        if (!allowed)
        {
            ClearPreview();
            return;
        }

        _world ??= PlacementWorld.Ensure(this);
        if (_world == null) return;

        if (_definition != definition)
        {
            ClearPreview();
            definition.Validate();
            _definition = definition;
            _preview = definition.ArtworkScene.Instantiate<Node2D>();
            _preview.Name = "PlacementPreview";
            _preview.TopLevel = true;
            _preview.Scale *= definition.ArtworkScale;
            AddChild(_preview);
            _refresh = 0.0;
        }

        _refresh -= delta;
        bool pressed = _player.Controls.UsePressed;

        if (_refresh <= 0.0 || pressed)
        {
            _refresh = System.Math.Max(0.05, PreviewInterval);
            RefreshPreview();
        }

        if (!pressed || !_valid) return;

        InventoryAddress? address = _hotbar.GetAddress(_hotbar.SelectedSlot);
        if (!address.HasValue) return;

        if (_world.TryPlace(
            _player, item, address.Value, _cell, PlacementRange))
        {
            _refresh = 0.0;
            _valid = false;
            if (_preview != null)
                _preview.Modulate = new Color(1f, 0.2f, 0.2f, 0.55f);
        }
    }

    // =========================================================
    // Snap the cursor, validate, tint artwork, and refresh nearby grid lines.
    private void RefreshPreview()
    {
        bool targeted = _world.TryCursorCell(
            _player.GetGlobalMousePosition(), out _cell);

        _valid = targeted && !_player.IsAirborne &&
            _world.CanPlace(
                _player, _definition, _cell, PlacementRange, out _);

        Vector2 centre = _world.Grid.Centre(_cell, _definition.Cells);
        _preview.GlobalPosition =
            centre + Vector2.Up * _world.HeightAt(centre);

        _preview.Modulate = _valid
            ? new Color(0.3f, 1f, 0.5f, 0.55f)
            : new Color(1f, 0.2f, 0.2f, 0.55f);

        _gridLines.Clear();
        Vector2I playerCell = _world.Grid.CellAt(_player.GlobalPosition);
        int radius = Mathf.Clamp(GridRadiusCells, 1, 6);

        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
        {
            if (x * x + y * y > radius * radius) continue;

            Vector2I cell = playerCell + new Vector2I(x, y);
            Vector2[] corners = _world.Grid.Corners(cell, Vector2I.One);
            Vector2[] outline = new Vector2[5];

            for (int i = 0; i < 4; i++)
                outline[i] = corners[i] +
                    Vector2.Up * _world.HeightAt(corners[i]);

            outline[4] = outline[0];
            _gridLines.Add(outline);
        }

        QueueRedraw();
    }

    // =========================================================
    // Hide placement artwork and grid when another item is selected.
    private void ClearPreview()
    {
        if (_preview != null)
        {
            _preview.Hide();
            _preview.QueueFree();
            _preview = null;
        }

        _definition = null;
        if (_gridLines.Count == 0) return;
        _gridLines.Clear();
        QueueRedraw();
    }

    // =========================================================
    // Convert cached world-space grid lines into the player's local drawing space.
    public override void _Draw()
    {
        foreach (Vector2[] worldPoints in _gridLines)
        {
            Vector2[] points = new Vector2[worldPoints.Length];
            for (int i = 0; i < points.Length; i++)
                points[i] = ToLocal(worldPoints[i]);

            DrawPolyline(points, new Color(0.65f, 0.9f, 1f, 0.12f), 1f, true);
        }
    }
    #endregion
}