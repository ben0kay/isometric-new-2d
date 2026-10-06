// Displays procedural terrain and liquid locations for testing.
// Builds a cached top-down map under a small frame budget without loading chunks.
using Godot;
using System.Diagnostics;

public partial class WorldMapDebug : Control
{
    #region Configuration
    [ExportGroup("Window")]
    [Export] public bool Enabled { get; set; } = true;
    [Export] public Vector2 PanelSize { get; set; } = new(980, 820);

    [ExportGroup("Map")]
    [Export] public int Resolution { get; set; } = 384;
    [Export] public float ViewRadiusTiles { get; set; } = 128f;
    [Export] public float MinimumRadiusTiles { get; set; } = 16f;
    [Export] public float MaximumRadiusTiles { get; set; } = 1024f;
    [Export] public double BuildBudgetMs { get; set; } = 2.0;

    [ExportGroup("Terrain Colours")]
    [Export] public Color BasaltColour { get; set; } = new("#596269");
    [Export] public Color HillsColour { get; set; } = new("#69725b");
    [Export] public Color ForestColour { get; set; } = new("#344e46");
    [Export] public Color OtherColour { get; set; } = new("#626b68");
    #endregion

    #region State
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private WaterBasinWorld _basins;
    private Node2D _ground;
    private Player _player;
    private Image _image;
    private ImageTexture _texture;
    private Vector2 _centre;
    private float _buildRadius;
    private int _resolution, _pixel;
    private bool _open, _building;
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep this removable debug interface inactive while closed.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        TextureFilter = TextureFilterEnum.Linear;
        Visible = false;
        SetProcess(false);
        SetProcessInput(Enabled);
    }

    // =========================================================
    // Release the temporary CPU image when leaving the scene.
    public override void _ExitTree()
    {
        _image?.Dispose();
        _image = null;
    }

    // =========================================================
    // Resolve the current world without depending on its root name.
    private bool ResolveWorld()
    {
        _generator = GetTree().GetFirstNodeInGroup("world_generator")
            as WorldGenerator;
        if (_generator == null) return false;

        Node systems = _generator.GetParent();
        Node root = systems.GetParent();
        _chunks = systems.GetNodeOrNull<ChunkController>("ChunkController");
        _ground = root.GetNodeOrNull<Node2D>("GroundChunks");
        _player = root.GetNodeOrNull<Player>("WorldObjects/Player");
        _basins = WaterBasinWorld.Find(this);

        return _chunks != null && _ground != null && _player != null
            && _chunks.WorldReady;
    }
    #endregion

    #region Input
    // =========================================================
    // Toggle, recenter, fit the whole world and zoom without pausing gameplay.
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
                    return;
                }

                _open = !_open;
                Visible = _open;
                SetProcess(_open);
                if (_open) FitWorld();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_open && key.PhysicalKeycode == Key.G)
            {
                FitWorld();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_open && key.PhysicalKeycode == Key.R)
            {
                _centre = PlayerTile();
                StartBuild();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (!_open || input is not InputEventMouseButton mouse ||
            !mouse.Pressed || !MapRect().HasPoint(GetLocalMousePosition()))
            return;

        if (mouse.ButtonIndex != MouseButton.WheelUp &&
            mouse.ButtonIndex != MouseButton.WheelDown) return;

        float factor = mouse.ButtonIndex == MouseButton.WheelUp
            ? 0.75f : 1.333333f;
        ViewRadiusTiles = Mathf.Clamp(
            ViewRadiusTiles * factor,
            Mathf.Max(1f, MinimumRadiusTiles),
            Mathf.Max(MinimumRadiusTiles, MaximumRadiusTiles));
        StartBuild();
        GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Include the complete finite world with a small outside border.
    private void FitWorld()
    {
        float span = _chunks.WorldChunksPerAxis * _chunks.ChunkSize;
        float minimum = -(_chunks.WorldChunksPerAxis / 2)
            * _chunks.ChunkSize - 0.5f;
        _centre = Vector2.One * (minimum + span * 0.5f);
        ViewRadiusTiles = span * 0.52f;
        StartBuild();
    }

    // =========================================================
    // Read the player's logical position independently from visual elevation.
    private Vector2 PlayerTile()
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
    }
    #endregion

    #region Building
    // =========================================================
    // Start a fresh terrain snapshot without requesting world chunks.
    private void StartBuild()
    {
        _resolution = Mathf.Clamp(Resolution, 64, 512);
        _buildRadius = Mathf.Max(1f, ViewRadiusTiles);
        _image?.Dispose();
        _image = Image.CreateEmpty(
            _resolution, _resolution, false, Image.Format.Rgba8);
        _texture = null;
        _pixel = 0;
        _building = true;
        QueueRedraw();
    }

    // =========================================================
    // Spread procedural sampling across frames and keep overlays live.
    public override void _Process(double delta)
    {
        if (_building)
        {
            long started = Stopwatch.GetTimestamp();
            double budget = System.Math.Clamp(BuildBudgetMs, 0.2, 5.0);
            int total = _resolution * _resolution;

            while (_pixel < total)
            {
                int x = _pixel % _resolution;
                int y = _pixel / _resolution;
                Vector2 tile = _centre + new Vector2(
                    ((x + 0.5f) / _resolution * 2f - 1f) * _buildRadius,
                    ((y + 0.5f) / _resolution * 2f - 1f) * _buildRadius);
                _image.SetPixel(x, y, SampleColour(tile));
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
                _building = false;
            }
        }
        QueueRedraw();
    }

    // =========================================================
    // Draw readable biome terrain with restrained relief and clear liquid colours.
    private Color SampleColour(Vector2 tile)
    {
        float minimum = -(_chunks.WorldChunksPerAxis / 2)
            * _chunks.ChunkSize - 0.5f;
        float maximum = minimum +
            _chunks.WorldChunksPerAxis * _chunks.ChunkSize;

        if (tile.X < minimum || tile.Y < minimum ||
            tile.X >= maximum || tile.Y >= maximum)
            return new Color("#10161b");

        WorldSample sample = _generator.SampleTile(tile);
        if (!sample.Walkable) return new Color("#030609");

        WaterBasinWorld.Basin basin = _basins?.GetBasinAt(tile);
        if (basin != null && basin.Fill > 0.0001f &&
            basin.WaterHeight > sample.Height)
        {
            float depth = Mathf.Clamp(
                (basin.WaterHeight - sample.Height) /
                basin.Definition.BasinDepth, 0f, 1f);
            Color shallow = basin.Definition.Liquid.DamagePerSecond > 0f
                ? new Color("#9ba83e") : new Color("#409bb2");
            return shallow.Lerp(new Color("#203e50"), depth * 0.65f);
        }

        Color colour = sample.BiomeId switch
        {
            "basalt_flats" => BasaltColour,
            "rolling_hills" => HillsColour,
            "carbon_forest" => ForestColour,
            _ => OtherColour
        };

        float neighbour = _generator.GetHeight(tile + new Vector2(1f, 1f));
        float slope = Mathf.Clamp(
            (sample.Height - neighbour) / 64f, -0.18f, 0.18f);
        float altitude = Mathf.Clamp(
            sample.Height / Mathf.Max(1f, _generator.HeightRange), -1f, 1f);
        float light = Mathf.Clamp(
            0.92f + slope + altitude * 0.08f, 0.65f, 1.15f);

        return new Color(
            Mathf.Clamp(colour.R * light, 0f, 1f),
            Mathf.Clamp(colour.G * light, 0f, 1f),
            Mathf.Clamp(colour.B * light, 0f, 1f), 1f);
    }
    #endregion

    #region Layout
    // =========================================================
    // Centre the window inside the current viewport.
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
    // Convert logical terrain coordinates to a position on the map.
    private Vector2 MapPoint(Vector2 tile, Rect2 map)
    {
        Vector2 normalized = (tile - _centre) / (_buildRadius * 2f)
            + new Vector2(0.5f, 0.5f);
        return map.Position + normalized * map.Size;
    }
    #endregion

    #region Drawing
    // =========================================================
    // Draw cached terrain, labelled liquids and the current player position.
    public override void _Draw()
    {
        if (!_open) return;

        Font font = ThemeDB.FallbackFont;
        Rect2 panel = PanelRect();
        Rect2 map = MapRect();

        DrawRect(panel, new Color(0.025f, 0.04f, 0.055f, 0.97f));
        DrawRect(panel, new Color("#536674"), false, 1f);
        DrawString(font, panel.Position + new Vector2(16, 28),
            "TERRAIN DEBUG   M close · Wheel zoom · R player · G whole world",
            HorizontalAlignment.Left, -1, 16, new Color("#d8e5ea"));

        DrawRect(map, new Color("#10161b"));
        if (_texture != null)
        {
            DrawTextureRect(_texture, map, false);
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
            : $"Width {_buildRadius * 2f:0} tiles · Seed {_chunks.WorldSeed} · " +
                $"Basins {_basins?.Basins.Count ?? 0}";

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 34),
            status, HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));
        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 13),
            "Grey basalt · Olive hills · Green forest · Blue liquid · Black chasm · Yellow player",
            HorizontalAlignment.Left, -1, 13, new Color("#97aebc"));
    }

    // =========================================================
    // Mark accepted basins so even small distant liquid bodies are easy to find.
    private void DrawLiquids(Rect2 map, Font font)
    {
        if (_basins == null) return;

        foreach (WaterBasinWorld.Basin basin in _basins.Basins)
        {
            Vector2 point = MapPoint(basin.Centre, map);
            if (!map.HasPoint(point)) continue;

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