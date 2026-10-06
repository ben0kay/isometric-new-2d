// Displays a cached world overview without pausing simulation.
// Claims DebugMap input while open; pan and zoom reuse the cached texture.
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

    #region State
    private InputModes _modes;
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private WaterBasinWorld _basins;
    private Node2D _ground;
    private Player _player;

    private Image _image;
    private ImageTexture _texture;
    private Vector2 _centre, _snapshotCentre;
    private float _snapshotRadius, _minimum, _maximum;
    private int _resolution, _pixel;
    private bool _open, _building, _dragging, _initialized;

    private uint _seed;
    private int _worldChunks, _chunkSize;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve input ownership and start with the map closed.
    public override void _Ready()
    {
        _modes = InputModes.For(this);
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        Visible = false;
        SetProcess(false);
        SetProcessInput(Enabled);
    }

    // =========================================================
    // Release ownership and cached resources when the map leaves the scene.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);

        _image?.Dispose();
        _texture?.Dispose();
        _image = null;
        _texture = null;
    }

    // =========================================================
    // Resolve world services and invalidate data if the world identity changes.
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
             _seed != chunks.WorldSeed ||
             _worldChunks != chunks.WorldChunksPerAxis ||
             _chunkSize != chunks.ChunkSize ||
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
    // Claim map input and prevent pointer events reaching underlying interfaces.
    public override void _Input(InputEvent input)
    {
        if (!Enabled) return;

        if (input is InputEventKey toggle &&
            toggle.Pressed && !toggle.Echo &&
            toggle.PhysicalKeycode == Key.M)
        {
            if (_open)
            {
                if (!_modes.OwnsInput(this)) return;
                CloseMap();
            }
            else
            {
                if (GetViewport().GuiIsDragging() || !ResolveWorld())
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }

                _open = true;
                Visible = true;
                _modes.Push(this, PlayerInputMode.DebugMap);

                if (!_initialized)
                {
                    StartBuild();
                    FitWorld();
                }

                SetProcess(true);
                QueueRedraw();
            }

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

                    case Key.G:
                        FitWorld();
                        break;

                    case Key.R:
                        ViewRadiusTiles = ClampRadius(
                            Mathf.Min(ViewRadiusTiles, 128f));
                        _centre = PlayerTile();
                        ClampCentre();
                        break;

                    case Key.F:
                        StartBuild();
                        break;
                }
            }

            // Keyboard events belong to the map while it owns input.
            GetViewport().SetInputAsHandled();
            QueueRedraw();
            return;
        }

        if (input is InputEventMouseMotion motion)
        {
            if (_dragging)
            {
                Rect2 map = MapRect();
                _centre -= motion.Relative / map.Size *
                    (ViewRadiusTiles * 2f);
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
            {
                _dragging = mouse.Pressed && MapRect().HasPoint(cursor);
            }
            else if (mouse.Pressed && MapRect().HasPoint(cursor) &&
                (mouse.ButtonIndex == MouseButton.WheelUp ||
                 mouse.ButtonIndex == MouseButton.WheelDown))
            {
                ZoomAt(cursor,
                    mouse.ButtonIndex == MouseButton.WheelUp ? 0.8f : 1.25f);
            }

            // Consume buttons everywhere, including over the underlying hotbar.
            GetViewport().SetInputAsHandled();
        }
    }

    // =========================================================
    // Restore the previous mode without discarding the map snapshot.
    private void CloseMap()
    {
        _open = false;
        _dragging = false;
        Visible = false;
        _modes.Release(this);
        SetProcess(_building);
    }

    // =========================================================
    // Zoom around the cursor using the existing overview.
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
    // Show the complete cached overview.
    private void FitWorld()
    {
        _centre = _snapshotCentre;
        ViewRadiusTiles = _snapshotRadius;
        QueueRedraw();
    }

    // =========================================================
    // Limit zoom to the cached map's available extent.
    private float ClampRadius(float requested)
    {
        float maximum = Mathf.Min(
            _snapshotRadius, Mathf.Max(1f, MaximumRadiusTiles));
        float minimum = Mathf.Min(
            maximum, Mathf.Max(1f, MinimumRadiusTiles));

        return Mathf.Clamp(requested, minimum, maximum);
    }

    // =========================================================
    // Keep the visible map inside the overview.
    private void ClampCentre()
    {
        float allowance = Mathf.Max(
            0f, _snapshotRadius - ViewRadiusTiles);

        _centre.X = Mathf.Clamp(_centre.X,
            _snapshotCentre.X - allowance, _snapshotCentre.X + allowance);
        _centre.Y = Mathf.Clamp(_centre.Y,
            _snapshotCentre.Y - allowance, _snapshotCentre.Y + allowance);
    }

    // =========================================================
    // Read player position on the logical terrain plane.
    private Vector2 PlayerTile()
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
    }
    #endregion

    #region Building
    // =========================================================
    // Prepare a new overview while retaining the old texture during refresh.
    private void StartBuild()
    {
        bool first = !_initialized;

        _seed = _chunks.WorldSeed;
        _worldChunks = _chunks.WorldChunksPerAxis;
        _chunkSize = _chunks.ChunkSize;
        _resolution = Mathf.Clamp(Resolution, 64, 512);

        float span = _worldChunks * _chunkSize;
        _minimum = -(_worldChunks / 2) * _chunkSize - 0.5f;
        _maximum = _minimum + span;
        _snapshotCentre = Vector2.One * (_minimum + span * 0.5f);
        _snapshotRadius = span * 0.52f;

        _image?.Dispose();
        _image = Image.CreateEmpty(
            _resolution, _resolution, false, Image.Format.Rgba8);

        _pixel = 0;
        _building = true;
        _initialized = true;

        if (first)
        {
            _centre = _snapshotCentre;
            ViewRadiusTiles = _snapshotRadius;
        }

        SetProcess(true);
        QueueRedraw();
    }

    // =========================================================
    // Budget initial generation across frames; reopening does not regenerate.
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
    // Show biome classification, liquid depth and restrained height shading.
    private Color SampleColour(Vector2 tile)
    {
        if (tile.X < _minimum || tile.Y < _minimum ||
            tile.X >= _maximum || tile.Y >= _maximum)
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
    // Centre the debug window within the viewport.
    private Rect2 PanelRect()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(
            Mathf.Min(PanelSize.X, Mathf.Max(160f, viewport.X - 32f)),
            Mathf.Min(PanelSize.Y, Mathf.Max(160f, viewport.Y - 32f)));

        return new Rect2((viewport - size) * 0.5f, size);
    }

    // =========================================================
    // Use a square map so both axes have the same scale.
    private Rect2 MapRect()
    {
        Rect2 panel = PanelRect();
        float side = Mathf.Max(
            32f, Mathf.Min(panel.Size.X - 32f, panel.Size.Y - 100f));

        return new Rect2(
            panel.Position + new Vector2((panel.Size.X - side) * 0.5f, 44f),
            Vector2.One * side);
    }

    // =========================================================
    // Select the visible part of the cached overview.
    private Rect2 SourceRect()
    {
        Vector2 low = _centre - Vector2.One * ViewRadiusTiles;
        Vector2 snapshotLow =
            _snapshotCentre - Vector2.One * _snapshotRadius;
        float scale = _resolution / (_snapshotRadius * 2f);

        return new Rect2(
            (low - snapshotLow) * scale,
            Vector2.One * (ViewRadiusTiles * 2f * scale));
    }

    // =========================================================
    // Convert a world tile position into a map position.
    private Vector2 MapPoint(Vector2 tile, Rect2 map)
    {
        Vector2 normalized = (tile - _centre) / (ViewRadiusTiles * 2f)
            + new Vector2(0.5f, 0.5f);

        return map.Position + normalized * map.Size;
    }
    #endregion

    #region Drawing
    // =========================================================
    // Display the cached map with live player and basin markers.
    public override void _Draw()
    {
        if (!_open) return;

        Font font = ThemeDB.FallbackFont;
        Rect2 panel = PanelRect();
        Rect2 map = MapRect();

        DrawRect(panel, new Color(0.025f, 0.04f, 0.055f, 0.97f));
        DrawRect(panel, new Color("#536674"), false, 1f);

        DrawString(font, panel.Position + new Vector2(16, 28),
            "DEBUG MAP  M/Esc close · Drag pan · Wheel zoom · R player · G world · F refresh",
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
            ? $"Building {_pixel * 100 / (_resolution * _resolution)}%..."
            : $"Width {ViewRadiusTiles * 2f:0} tiles · Seed {_seed} · " +
              $"Basins {_basins?.Basins.Count ?? 0} · Input: DebugMap";

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 34),
            status, HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 13),
            "Grey basalt · Olive hills · Green forest · Teal wilds · Blue water · Black chasm",
            HorizontalAlignment.Left, -1, 13, new Color("#97aebc"));
    }

    // =========================================================
    // Mark accepted basins even when they are small on the overview.
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
            text.X = Mathf.Clamp(text.X, map.Position.X + 4f,
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