// Displays a cached procedural world overview for debugging.
// Pan and zoom reuse the texture; only the initial build or F refresh samples terrain.
using Godot;
using System;
using System.Diagnostics;

public partial class WorldMapDebug : Control
{
    #region Configuration
    [ExportGroup("Window")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Vector2 PanelSize { get; set; } = new(980, 820);

    [ExportGroup("Map")]
    [Export] public int Resolution { get; set; } = 256;
    [Export] public float ViewRadiusTiles { get; set; } = 128f;
    [Export] public float MinimumRadiusTiles { get; set; } = 16f;
    [Export] public float MaximumRadiusTiles { get; set; } = 1024f;
    [Export] public double BuildBudgetMs { get; set; } = 2.0;

    [ExportGroup("Terrain Colours")]
    [Export] public Color BasaltColour { get; set; } = new("#596269");
    [Export] public Color HillsColour { get; set; } = new("#69725b");
    [Export] public Color ForestColour { get; set; } = new("#344e46");
    [Export] public Color WildsColour { get; set; } = new("#226b70");
    [Export] public Color OtherColour { get; set; } = new("#626b68");
    #endregion

    #region World State
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private WaterBasinWorld _basins;
    private Node2D _ground;
    private Player _player;

    private uint _cachedSeed;
    private int _cachedWorldChunks;
    private int _cachedChunkSize;
    #endregion

    #region Map State
    private Image _image;
    private ImageTexture _texture;
    private Vector2 _centre;
    private Vector2 _snapshotCentre;
    private float _snapshotRadius;
    private float _worldMinimum;
    private float _worldMaximum;
    private int _resolution;
    private int _pixel;
    private bool _open;
    private bool _building;
    private bool _dragging;
    private bool _initialized;
    #endregion

    #region Lifecycle
    // =========================================================
    // Leave the removable debug interface inactive until first opened.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        Visible = false;
        SetProcess(false);
        SetProcessInput(Enabled);
    }

    // =========================================================
    // Release both CPU and GPU map resources when the scene closes.
    public override void _ExitTree()
    {
        _image?.Dispose();
        _texture?.Dispose();
        _image = null;
        _texture = null;
    }

    // =========================================================
    // Resolve the current world and invalidate snapshots if its identity changes.
    private bool ResolveWorld()
    {
        WorldGenerator generator = GetTree()
            .GetFirstNodeInGroup("world_generator") as WorldGenerator;

        if (generator == null) return false;

        Node systems = generator.GetParent();
        Node root = systems.GetParent();

        ChunkController chunks =
            systems.GetNodeOrNull<ChunkController>("ChunkController");
        Node2D ground = root.GetNodeOrNull<Node2D>("GroundChunks");
        Player player = root.GetNodeOrNull<Player>("WorldObjects/Player");

        if (chunks == null || ground == null || player == null ||
            !chunks.WorldReady)
            return false;

        bool changed = _initialized &&
            (_generator != generator ||
             _cachedSeed != chunks.WorldSeed ||
             _cachedWorldChunks != chunks.WorldChunksPerAxis ||
             _cachedChunkSize != chunks.ChunkSize ||
             _resolution != Mathf.Clamp(Resolution, 64, 512));

        _generator = generator;
        _chunks = chunks;
        _ground = ground;
        _player = player;
        _basins = WaterBasinWorld.Find(this);

        if (changed)
        {
            _image?.Dispose();
            _texture?.Dispose();
            _image = null;
            _texture = null;
            _building = false;
            _initialized = false;
        }

        return true;
    }
    #endregion

    #region Input
    // =========================================================
    // Toggle the cached map, navigate it and explicitly refresh when requested.
    public override void _Input(InputEvent input)
    {
        if (!Enabled) return;

        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.M)
            {
                if (!_open && !ResolveWorld())
                {
                    GD.Print("[Map] Wait for world loading to finish.");
                    GetViewport().SetInputAsHandled();
                    return;
                }

                _open = !_open;
                _dragging = false;
                Visible = _open;

                if (_open && !_initialized)
                {
                    StartBuild();
                    FitWorld();
                }

                // An unfinished initial build can continue while the map is closed.
                SetProcess(_open || _building);
                QueueRedraw();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_open)
            {
                switch (key.PhysicalKeycode)
                {
                    case Key.G:
                        FitWorld();
                        break;

                    case Key.R:
                        ViewRadiusTiles = ClampRadius(
                            Mathf.Min(ViewRadiusTiles, 128f));
                        _centre = PlayerTile();
                        ClampCentre();
                        QueueRedraw();
                        break;

                    case Key.F:
                        StartBuild();
                        break;

                    default:
                        return;
                }

                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (!_open) return;

        if (input is InputEventMouseMotion motion)
        {
            if (!_dragging) return;

            Rect2 map = MapRect();
            _centre -= motion.Relative / map.Size
                * (ViewRadiusTiles * 2f);

            ClampCentre();
            QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (input is not InputEventMouseButton mouse) return;

        if (mouse.ButtonIndex == MouseButton.Left && !mouse.Pressed)
        {
            bool wasDragging = _dragging;
            _dragging = false;

            if (wasDragging || PanelRect().HasPoint(GetLocalMousePosition()))
                GetViewport().SetInputAsHandled();

            return;
        }

        Vector2 cursor = GetLocalMousePosition();

        if (MapRect().HasPoint(cursor) && mouse.Pressed)
        {
            if (mouse.ButtonIndex == MouseButton.Left)
            {
                _dragging = true;
            }
            else if (mouse.ButtonIndex == MouseButton.WheelUp ||
                     mouse.ButtonIndex == MouseButton.WheelDown)
            {
                ZoomAt(cursor,
                    mouse.ButtonIndex == MouseButton.WheelUp
                        ? 0.8f : 1.25f);
            }
        }

        if (PanelRect().HasPoint(cursor))
            GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Zoom around the cursor without requesting another terrain snapshot.
    private void ZoomAt(Vector2 cursor, float factor)
    {
        Rect2 map = MapRect();
        Vector2 offset = (cursor - map.Position) / map.Size
            - new Vector2(0.5f, 0.5f);

        Vector2 anchor = _centre + offset * (ViewRadiusTiles * 2f);
        ViewRadiusTiles = ClampRadius(ViewRadiusTiles * factor);
        _centre = anchor - offset * (ViewRadiusTiles * 2f);

        ClampCentre();
        QueueRedraw();
    }

    // =========================================================
    // Fit the existing overview without rebuilding its texture.
    private void FitWorld()
    {
        _centre = _snapshotCentre;
        ViewRadiusTiles = _snapshotRadius;
        QueueRedraw();
    }

    // =========================================================
    // Limit interactive zoom to the cached overview.
    private float ClampRadius(float requested)
    {
        float maximum = Mathf.Min(
            _snapshotRadius, Mathf.Max(1f, MaximumRadiusTiles));
        float minimum = Mathf.Min(
            maximum, Mathf.Max(1f, MinimumRadiusTiles));

        return Mathf.Clamp(requested, minimum, maximum);
    }

    // =========================================================
    // Keep the visible map rectangle within the cached image.
    private void ClampCentre()
    {
        float allowance = Mathf.Max(
            0f, _snapshotRadius - ViewRadiusTiles);

        _centre.X = Mathf.Clamp(
            _centre.X,
            _snapshotCentre.X - allowance,
            _snapshotCentre.X + allowance);

        _centre.Y = Mathf.Clamp(
            _centre.Y,
            _snapshotCentre.Y - allowance,
            _snapshotCentre.Y + allowance);
    }

    // =========================================================
    // Read logical player position independently from visual elevation.
    private Vector2 PlayerTile()
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
    }
    #endregion

    #region Building
    // =========================================================
    // Build one full-world snapshot; keep an existing texture visible on refresh.
    private void StartBuild()
    {
        bool firstBuild = !_initialized;

        _cachedSeed = _chunks.WorldSeed;
        _cachedWorldChunks = _chunks.WorldChunksPerAxis;
        _cachedChunkSize = _chunks.ChunkSize;
        _resolution = Mathf.Clamp(Resolution, 64, 512);

        float span = _chunks.WorldChunksPerAxis * _chunks.ChunkSize;

        _worldMinimum = -(_chunks.WorldChunksPerAxis / 2)
            * _chunks.ChunkSize - 0.5f;
        _worldMaximum = _worldMinimum + span;
        _snapshotCentre = Vector2.One * (_worldMinimum + span * 0.5f);
        _snapshotRadius = span * 0.52f;

        _image?.Dispose();
        _image = Image.CreateEmpty(
            _resolution, _resolution, false, Image.Format.Rgba8);

        _pixel = 0;
        _building = true;
        _initialized = true;

        if (firstBuild)
        {
            _centre = _snapshotCentre;
            ViewRadiusTiles = _snapshotRadius;
        }

        SetProcess(true);
        QueueRedraw();
    }

    // =========================================================
    // Budget snapshot sampling across frames and keep player overlays live.
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

                Vector2 tile = _snapshotCentre + new Vector2(
                    ((x + 0.5f) / _resolution * 2f - 1f) * _snapshotRadius,
                    ((y + 0.5f) / _resolution * 2f - 1f) * _snapshotRadius);

                _image.SetPixel(x, y, SampleColour(tile));
                _pixel++;

                if ((_pixel & 15) == 0 &&
                    (Stopwatch.GetTimestamp() - started) * 1000.0 /
                    Stopwatch.Frequency >= budget)
                    break;
            }

            if (_pixel == total)
            {
                ImageTexture replacement = ImageTexture.CreateFromImage(_image);
                _texture?.Dispose();
                _texture = replacement;

                _image.Dispose();
                _image = null;
                _building = false;
                SetProcess(_open);
            }
        }

        if (_open) QueueRedraw();
    }

    // =========================================================
    // Sample one terrain query per pixel and give Wilds its own debug colour.
    private Color SampleColour(Vector2 tile)
    {
        if (tile.X < _worldMinimum || tile.Y < _worldMinimum ||
            tile.X >= _worldMaximum || tile.Y >= _worldMaximum)
            return new Color("#10161b");

        WorldSample sample = _generator.SampleTile(tile);
        if (!sample.Walkable) return new Color("#030609");

        WaterBasinWorld.Basin basin = _basins?.GetBasinAt(tile);

        if (basin != null && basin.Fill > 0.0001f &&
            basin.WaterHeight > sample.Height)
        {
            float depth = Mathf.Clamp(
                (basin.WaterHeight - sample.Height) /
                Mathf.Max(0.001f, basin.Definition.BasinDepth), 0f, 1f);

            Color shallow = basin.Definition.Liquid.DamagePerSecond > 0f
                ? new Color("#9ba83e") : new Color("#409bb2");

            return shallow.Lerp(new Color("#203e50"), depth * 0.65f);
        }

        Color colour = sample.BiomeId switch
        {
            "basalt_flats" => BasaltColour,
            "rolling_hills" => HillsColour,
            "carbon_forest" => ForestColour,
            "verdigris_wilds" => WildsColour,
            _ => OtherColour
        };

        // Keep gentle height variation without a second procedural height query.
        float altitude = Mathf.Clamp(
            sample.Height / Mathf.Max(1f, _generator.HeightRange), -1f, 1f);
        float light = 0.94f + altitude * 0.08f;

        return new Color(
            Mathf.Clamp(colour.R * light, 0f, 1f),
            Mathf.Clamp(colour.G * light, 0f, 1f),
            Mathf.Clamp(colour.B * light, 0f, 1f), 1f);
    }
    #endregion

    #region Layout
    // =========================================================
    // Centre the debug window in the current viewport.
    private Rect2 PanelRect()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(
            Mathf.Min(PanelSize.X, Mathf.Max(160f, viewport.X - 32f)),
            Mathf.Min(PanelSize.Y, Mathf.Max(160f, viewport.Y - 32f)));

        return new Rect2((viewport - size) * 0.5f, size);
    }

    // =========================================================
    // Preserve equal tile distances on both map axes.
    private Rect2 MapRect()
    {
        Rect2 panel = PanelRect();
        float side = Mathf.Max(
            32f, Mathf.Min(panel.Size.X - 32f, panel.Size.Y - 100f));

        return new Rect2(
            panel.Position + new Vector2((panel.Size.X - side) * 0.5f, 44f),
            new Vector2(side, side));
    }

    // =========================================================
    // Select the cached image portion corresponding to the current view.
    private Rect2 SourceRect()
    {
        Vector2 low = _centre - Vector2.One * ViewRadiusTiles;
        Vector2 snapshotLow =
            _snapshotCentre - Vector2.One * _snapshotRadius;

        float pixelsPerTile = _resolution / (_snapshotRadius * 2f);

        return new Rect2(
            (low - snapshotLow) * pixelsPerTile,
            Vector2.One * (ViewRadiusTiles * 2f * pixelsPerTile));
    }

    // =========================================================
    // Project logical world coordinates onto the currently visible map.
    private Vector2 MapPoint(Vector2 tile, Rect2 map)
    {
        Vector2 normalized = (tile - _centre) / (ViewRadiusTiles * 2f)
            + new Vector2(0.5f, 0.5f);

        return map.Position + normalized * map.Size;
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw the cached view with live player and basin markers.
    public override void _Draw()
    {
        if (!_open) return;

        Font font = ThemeDB.FallbackFont;
        Rect2 panel = PanelRect();
        Rect2 map = MapRect();

        DrawRect(panel, new Color(0.025f, 0.04f, 0.055f, 0.97f));
        DrawRect(panel, new Color("#536674"), false, 1f);

        DrawString(font, panel.Position + new Vector2(16, 28),
            "TERRAIN DEBUG  M close · Drag pan · Wheel zoom · R player · G world · F refresh",
            HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));

        DrawRect(map, new Color("#10161b"));

        if (_texture != null)
        {
            DrawTextureRectRegion(_texture, map, SourceRect());
            DrawLiquids(map, font);

            Vector2 player = MapPoint(PlayerTile(), map);

            if (map.HasPoint(player))
            {
                DrawCircle(player, 6f, Colors.Black);
                DrawCircle(player, 4f, new Color("#ffed8a"));
            }
        }

        DrawRect(map, new Color("#526371"), false, 1f);

        string status = _building
            ? $"Building overview {_pixel * 100 / (_resolution * _resolution)}%..."
            : $"Width {ViewRadiusTiles * 2f:0} tiles · " +
              $"Seed {_chunks.WorldSeed} · " +
              $"Basins {_basins?.Basins.Count ?? 0} · Cached";

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 34),
            status, HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 13),
            "Grey basalt · Olive hills · Green forest · Teal wilds · Blue water · Black chasm",
            HorizontalAlignment.Left, -1, 13, new Color("#97aebc"));
    }

    // =========================================================
    // Mark accepted basins, including small bodies hidden by overview resolution.
    private void DrawLiquids(Rect2 map, Font font)
    {
        if (_basins == null) return;

        foreach (WaterBasinWorld.Basin basin in _basins.Basins)
        {
            Vector2 point = MapPoint(basin.Centre, map);

            if (!map.Grow(-8f).HasPoint(point)) continue;

            bool filled = basin.Fill > 0.0001f;
            Color colour = !filled ? new Color("#a1a8ac")
                : basin.Definition.Liquid.DamagePerSecond > 0f
                    ? new Color("#d0da65") : new Color("#65d9ef");

            DrawCircle(point, 7f, Colors.Black);
            DrawArc(point, 6f, 0f, Mathf.Tau, 24, colour, 2f, true);

            string label = filled
                ? basin.Definition.Liquid.Id : "empty basin";
            label += $" ({basin.Centre.X:0}, {basin.Centre.Y:0})";

            Vector2 text = point + new Vector2(10, -10);
            text.X = Mathf.Clamp(
                text.X, map.Position.X + 4f,
                Mathf.Max(map.Position.X + 4f, map.End.X - 190f));
            text.Y = Mathf.Clamp(
                text.Y, map.Position.Y + 18f, map.End.Y - 4f);

            DrawString(font, text + Vector2.One,
                label, HorizontalAlignment.Left, -1, 13, Colors.Black);
            DrawString(font, text,
                label, HorizontalAlignment.Left, -1, 13, colour);
        }
    }
    #endregion
}