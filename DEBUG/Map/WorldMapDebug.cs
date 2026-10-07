// Previews seeded biome identity around the player without loading world content.
// Uses the same isometric axes and proportions as the gameplay terrain.
using Godot;
using System;
using System.Diagnostics;

public partial class WorldMapDebug : Control
{
    #region Configuration
    [ExportGroup("Window")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Vector2 PanelSize { get; set; } = new(980, 820);

    [ExportGroup("Preview")]
    [Export] public int Resolution { get; set; } = 256;
    [Export] public double BuildBudgetMs { get; set; } = 2.0;
    #endregion

    #region State
    private InputModes _modes;
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private WorldConfig _config;
    private Node2D _ground;
    private Player _player;

    private Image _image;
    private ImageTexture _texture;
    private string[] _ids;

    private Vector2 _snapshotTile, _snapshotPlane, _viewCentre;
    private float _tileRadius, _snapshotRadius, _viewRadius;
    private int _resolution, _pixel;

    private bool _open, _building, _dragging;
    private WorldMapPoiPreview _pois;
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep simulation running while the map owns gameplay input.
    public override void _Ready()
    {
        _modes = InputModes.For(this);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Nearest;
        Visible = false;
        SetProcess(false);
        SetProcessInput(Enabled);
    }

// =========================================================
// Release map input, metadata protection and preview resources.
public override void _ExitTree()
{
    if (GodotObject.IsInstanceValid(_modes)) _modes.Release(this);
    _pois?.Dispose();
    _pois = null;
    _image?.Dispose();
    _texture?.Dispose();
}

    // =========================================================
    // Resolve only the services needed for biome identity and player position.
    private bool ResolveWorld()
    {
        _generator = GetTree().GetFirstNodeInGroup(
            "world_generator") as WorldGenerator;
        if (_generator == null) return false;

        Node root = _generator.GetParent().GetParent();
        _chunks = root.GetNode<ChunkController>("Systems/ChunkController");
        _ground = root.GetNode<Node2D>("GroundChunks");
        _player = root.GetNode<Player>("WorldObjects/Player");
        _config = WorldConfig.Find(_generator);
        return _chunks.WorldReady;
    }
    #endregion

    #region Projection
    // =========================================================
    // Use the terrain's diagonal axes; MapRect supplies vertical compression.
    private static Vector2 Project(Vector2 tile)
    {
        return new Vector2(tile.X - tile.Y, tile.X + tile.Y);
    }

    // =========================================================
    // Convert projected preview coordinates back to absolute generation tiles.
    private static Vector2 Unproject(Vector2 plane)
    {
        return new Vector2(
            (plane.X + plane.Y) * 0.5f,
            (plane.Y - plane.X) * 0.5f);
    }

    // =========================================================
    // Read the shared player's logical terrain coordinate.
    private Vector2 PlayerTile()
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
    }

    // =========================================================
    // Position markers with the same projection used to build the preview image.
    private Vector2 MapPoint(Vector2 tile, Rect2 map)
    {
        Vector2 normalized =
            (Project(tile) - _viewCentre) / (_viewRadius * 2f) +
            Vector2.One * 0.5f;
        return map.Position + normalized * map.Size;
    }
    #endregion

    #region Input
// =========================================================
// Reopen cached previews and keep map gestures out of gameplay.
public override void _Input(InputEvent input)
{
    if (!Enabled) return;

    if (input is InputEventKey toggle &&
        toggle.Pressed && !toggle.Echo &&
        toggle.PhysicalKeycode == Key.M)
    {
        if (_open)
        {
            if (_modes.OwnsInput(this)) CloseMap();
        }
        else if (!GetViewport().GuiIsDragging() && ResolveWorld())
            OpenMap();

        GetViewport().SetInputAsHandled();
        return;
    }

    if (!_open || !_modes.OwnsInput(this)) return;

    if (input is InputEventKey key)
    {
        if (key.Pressed && !key.Echo)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.Escape:
                    CloseMap();
                    break;

                case Key.R:
                case Key.F:
                    StartBuild();
                    break;

                case Key.G:
                    _viewCentre = _snapshotPlane;
                    _viewRadius = _snapshotRadius;
                    break;
            }
        }

        GetViewport().SetInputAsHandled();
        QueueRedraw();
        return;
    }

    if (input is InputEventMouseMotion motion)
    {
        if (_dragging)
        {
            _viewCentre -= motion.Relative / MapRect().Size *
                (_viewRadius * 2f);
            ClampCentre();
            QueueRedraw();
        }

        GetViewport().SetInputAsHandled();
        return;
    }

    if (input is InputEventMouseButton mouse)
    {
        Vector2 cursor = GetLocalMousePosition();

        if (mouse.ButtonIndex == MouseButton.Left)
            _dragging = mouse.Pressed && MapRect().HasPoint(cursor);
        else if (mouse.Pressed && MapRect().HasPoint(cursor) &&
            (mouse.ButtonIndex == MouseButton.WheelUp ||
             mouse.ButtonIndex == MouseButton.WheelDown))
            ZoomAt(cursor,
                mouse.ButtonIndex == MouseButton.WheelUp ? 0.8f : 1.25f);

        GetViewport().SetInputAsHandled();
    }
}

// =========================================================
// Open from gameplay and reuse cached biome pixels.
private void OpenMap()
{
    if (!_modes.GameplayAllowed)
        return;

    float radius = _config.DebugMapRadiusTiles;

    if (!float.IsFinite(radius) || radius < 16f)
        throw new InvalidOperationException(
            "DebugMapRadiusTiles must be finite and at least 16.");

    Vector2 playerTile = PlayerTile();
    Vector2 distance = (playerTile - _snapshotTile).Abs();

    bool reusable =
        (_texture != null || _building) &&
        Mathf.IsEqualApprox(radius, _tileRadius) &&
        Mathf.Clamp(Resolution, 64, 512) == _resolution &&
        distance.X <= _tileRadius &&
        distance.Y <= _tileRadius;

    _open = true;
    _dragging = false;
    Visible = true;
    _modes.Push(this, PlayerInputMode.DebugMap);

    if (!reusable)
    {
        StartBuild();
        return;
    }

    // Reacquire nearby POI protection when reopening.
    _pois?.Dispose();
    _pois = new WorldMapPoiPreview(
        this, _ground, _chunks, _config, playerTile);

    // Preserve zoom and pan unless the player is outside the view.
    Rect2 map = MapRect();
    if (!map.HasPoint(MapPoint(playerTile, map)))
    {
        _viewCentre = Project(playerTile);
        ClampCentre();
    }

    SetProcess(true);
    QueueRedraw();
}

// =========================================================
// Restore gameplay and release map-owned POI preparation.
private void CloseMap()
{
    _open = _dragging = false;
    Visible = false;
    _modes.Release(this);

    _pois?.Dispose();
    _pois = null;
    SetProcess(_building);
}

    // =========================================================
    // Preserve the coordinate beneath the cursor while zooming.
    private void ZoomAt(Vector2 cursor, float factor)
    {
        Rect2 map = MapRect();
        Vector2 offset =
            (cursor - map.Position) / map.Size - Vector2.One * 0.5f;
        Vector2 anchor = _viewCentre + offset * (_viewRadius * 2f);

        _viewRadius = Mathf.Clamp(
            _viewRadius * factor,
            Mathf.Min(32f, _snapshotRadius), _snapshotRadius);
        _viewCentre = anchor - offset * (_viewRadius * 2f);

        ClampCentre();
        QueueRedraw();
    }

    // =========================================================
    // Keep pan and zoom within the current cached preview.
    private void ClampCentre()
    {
        float allowance = Mathf.Max(0f, _snapshotRadius - _viewRadius);
        _viewCentre.X = Mathf.Clamp(_viewCentre.X,
            _snapshotPlane.X - allowance, _snapshotPlane.X + allowance);
        _viewCentre.Y = Mathf.Clamp(_viewCentre.Y,
            _snapshotPlane.Y - allowance, _snapshotPlane.Y + allowance);
    }
    #endregion

    #region Building
// =========================================================
// Build a large biome snapshot and a separate nearby POI preview.
private void StartBuild()
{
    float radius = _config.DebugMapRadiusTiles;
    if (!float.IsFinite(radius) || radius < 16f)
        throw new InvalidOperationException(
            "DebugMapRadiusTiles must be finite and at least 16.");

    _snapshotTile = PlayerTile();
    _snapshotPlane = Project(_snapshotTile);
    _tileRadius = radius;
    _snapshotRadius = radius * 2f;
    _viewCentre = _snapshotPlane;
    _viewRadius = _snapshotRadius;

    _resolution = Mathf.Clamp(Resolution, 64, 512);
    _pixel = 0;
    _ids = new string[_resolution * _resolution];

    _image?.Dispose();
    _texture?.Dispose();
    _texture = null;
    _image = Image.CreateEmpty(
        _resolution, _resolution, false, Image.Format.Rgba8);

    _pois?.Dispose();
    _pois = new WorldMapPoiPreview(
        this, _ground, _chunks, _config, _snapshotTile);

    _building = true;
    SetProcess(true);
    QueueRedraw();
}

// =========================================================
// Budget biome sampling and nearby POI preparation separately.
public override void _Process(double delta)
{
    if (_building)
    {
        long started = Stopwatch.GetTimestamp();
        double budget = Math.Clamp(BuildBudgetMs, 0.2, 5.0);
        int total = _resolution * _resolution;

        while (_pixel < total)
        {
            int x = _pixel % _resolution;
            int y = _pixel / _resolution;

            Vector2 plane = _snapshotPlane + new Vector2(
                ((x + 0.5f) / _resolution * 2f - 1f) * _snapshotRadius,
                ((y + 0.5f) / _resolution * 2f - 1f) * _snapshotRadius);
            Vector2 tile = Unproject(plane);
            Vector2 difference = (tile - _snapshotTile).Abs();

            Color colour = new("#10161b");

            if (difference.X <= _tileRadius &&
                difference.Y <= _tileRadius)
            {
                string id = _generator.GetBiome(tile).Id;
                _ids[_pixel] = id;
                colour = BiomeColour(id);

                bool boundary =
                    (x > 0 && _ids[_pixel - 1] != id) ||
                    (y > 0 && _ids[_pixel - _resolution] != id);

                if (boundary) colour = new Color("#080e13");
            }

            _image.SetPixel(x, y, colour);
            _pixel++;

            if ((_pixel & 15) == 0 &&
                (Stopwatch.GetTimestamp() - started) * 1000.0 /
                Stopwatch.Frequency >= budget)
                break;
        }

        if (_pixel == total)
        {
            _texture = ImageTexture.CreateFromImage(_image);
            _image.Dispose();
            _image = null;
            _ids = null;
            _building = false;
            SetProcess(_open);
        }
    }

    if (_open)
    {
        _pois?.Tick(delta, PlayerTile());
        QueueRedraw();
    }
}

    // =========================================================
    // Give known biomes readable colours and future biomes stable seeded-free colours.
    private static Color BiomeColour(string id)
    {
        switch (id)
        {
            case "basalt_flats": return new Color("#596269");
            case "rolling_hills": return new Color("#69725b");
            case "carbon_forest": return new Color("#344e46");
            case "verdigris_wilds": return new Color("#226b70");
            case "rocky_mountains": return new Color("#777982");
        }

        uint hash = 2166136261u;
        unchecked
        {
            foreach (char letter in id)
                hash = (hash ^ letter) * 16777619u;
        }

        return Color.FromHsv(
            (hash & 65535u) / 65536f, 0.35f, 0.55f);
    }
    #endregion

    #region Layout And Drawing
    // =========================================================
    // Centre the preview panel inside the viewport.
    private Rect2 PanelRect()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(
            Mathf.Min(PanelSize.X, Mathf.Max(160f, viewport.X - 32f)),
            Mathf.Min(PanelSize.Y, Mathf.Max(160f, viewport.Y - 32f)));
        return new Rect2((viewport - size) * 0.5f, size);
    }

    // =========================================================
    // Match the terrain's tile proportions rather than displaying a square grid.
    private Rect2 MapRect()
    {
        Rect2 panel = PanelRect();
        float aspect = _chunks != null
            ? _chunks.TileSize.Y / _chunks.TileSize.X : 0.5f;
        float width = Mathf.Max(32f,
            Mathf.Min(panel.Size.X - 32f,
                (panel.Size.Y - 100f) / Mathf.Max(0.1f, aspect)));
        Vector2 size = new(width, width * aspect);

        return new Rect2(
            panel.Position + new Vector2(
                (panel.Size.X - size.X) * 0.5f,
                44f + (panel.Size.Y - 100f - size.Y) * 0.5f),
            size);
    }

    // =========================================================
    // Select the visible portion of the projected cached preview.
    private Rect2 SourceRect()
    {
        float scale = _resolution / (_snapshotRadius * 2f);
        Vector2 low = _viewCentre - Vector2.One * _viewRadius;
        Vector2 snapshotLow =
            _snapshotPlane - Vector2.One * _snapshotRadius;

        return new Rect2(
            (low - snapshotLow) * scale,
            Vector2.One * (_viewRadius * 2f * scale));
    }

// =========================================================
// Draw biome outlines, nearby POIs and the live player position.
public override void _Draw()
{
    if (!_open) return;

    Rect2 panel = PanelRect();
    Rect2 map = MapRect();
    Font font = ThemeDB.FallbackFont;

    DrawRect(panel, new Color(0.025f, 0.04f, 0.055f, 0.97f));
    DrawRect(panel, new Color("#536674"), false, 1f);
    DrawRect(map, new Color("#10161b"));

    DrawString(font, panel.Position + new Vector2(16, 28),
        "BIOME MAP  M/Esc close · Drag pan · Wheel zoom · R/F refresh · G fit",
        HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));

    string hovered = "";

    if (_texture != null)
    {
        DrawTextureRectRegion(_texture, map, SourceRect());

        if (_pois != null)
            foreach (WorldMapPoiPreview.Entry entry in _pois.Entries)
            {
                Vector2 point = MapPoint(entry.Tile, map);
                if (!map.HasPoint(point)) continue;

                Color colour = entry.Kind switch
                {
                    DebugMapPoiKind.CaveEntrance => new Color("#ffae67"),
                    DebugMapPoiKind.Resource => new Color("#78dfb1"),
                    DebugMapPoiKind.Settlement => new Color("#9daaff"),
                    _ => new Color("#e2d9f0")
                };

                Rect2 marker = new(
                    point - Vector2.One * 4f, Vector2.One * 8f);
                DrawRect(marker.Grow(1f), Colors.Black);
                DrawRect(marker, colour);

                if (point.DistanceSquaredTo(
                    GetLocalMousePosition()) <= 100f)
                    hovered = $"{entry.Kind}: {entry.Name} " +
                        $"({entry.Tile.X:0}, {entry.Tile.Y:0})";
            }

        Vector2 player = MapPoint(PlayerTile(), map);
        if (map.HasPoint(player))
        {
            DrawCircle(player, 6f, Colors.Black);
            DrawCircle(player, 4f, new Color("#ffed8a"));
        }
    }

    DrawRect(map, new Color("#526371"), false, 1f);

    string progress = _building
        ? $" · Biomes {_pixel * 100 / (_resolution * _resolution)}%"
        : "";

    if (_pois?.Building == true) progress += " · Preparing POIs";

    string status =
        $"Biome radius {_tileRadius:0} · POI radius {_pois?.Radius ?? 0:0}" +
        $" · Seed {_chunks.WorldSeed}{progress}";

    DrawString(font, panel.Position + new Vector2(16, panel.Size.Y - 40),
        status, HorizontalAlignment.Left, -1, 14, new Color("#97aebc"));

    DrawString(font, panel.Position + new Vector2(16, panel.Size.Y - 20),
        hovered.Length > 0 ? hovered :
            "Yellow: player · Orange: cave entrance · Hover markers for details",
        HorizontalAlignment.Left, -1, 14, new Color("#d8e5ea"));
}
    #endregion
}