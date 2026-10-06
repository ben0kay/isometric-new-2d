// Displays a removable M-key debug map of procedural terrain and water basins.
// Samples unloaded terrain directly and builds its texture under a frame budget.
using Godot;
using System.Collections.Generic;
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
    private readonly Dictionary<string, Color> _biomeColors = new();
    private readonly Color _waterColor = new("#3ba8c9");
    private readonly Color _voidColor = new("#080b13");
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep the debug interface hidden and inactive until explicitly opened.
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
        SetProcess(false);
        SetProcessInput(Enabled);
    }

    // =========================================================
    // Release the CPU-side image when leaving the test scene.
    public override void _ExitTree()
    {
        _image?.Dispose();
        _image = null;
    }

    // =========================================================
    // Resolve the current world without assuming its root scene name.
    private bool ResolveWorld()
    {
        _generator = GetTree().GetFirstNodeInGroup("world_generator")
            as WorldGenerator;
        if (_generator == null) return false;

        Node systems = _generator.GetParent();
        _chunks = systems.GetNodeOrNull<ChunkController>("ChunkController");
        Node root = systems.GetParent();
        _ground = root.GetNodeOrNull<Node2D>("GroundChunks");
        _player = root.GetNodeOrNull<Player>("WorldObjects/Player");
        _basins = WaterBasinWorld.Find(this);

        return _chunks != null && _ground != null && _player != null
            && _chunks.WorldReady;
    }
    #endregion

    #region Input
    // =========================================================
    // Toggle with M, recenter with R, and zoom over the map with the wheel.
    public override void _Input(InputEvent input)
    {
        if (!Enabled) return;

        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.M)
            {
                if (!_open && !ResolveWorld())
                {
                    GD.Print("[Debug map] Wait for world loading to finish.");
                    return;
                }

                _open = !_open;
                Visible = _open;
                SetProcess(_open);
                if (_open) Recenter();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (_open && key.PhysicalKeycode == Key.R)
            {
                Recenter();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (!_open || input is not InputEventMouseButton mouse ||
            !mouse.Pressed || !MapRect().HasPoint(GetLocalMousePosition()))
            return;

        if (mouse.ButtonIndex != MouseButton.WheelUp &&
            mouse.ButtonIndex != MouseButton.WheelDown) return;

        float factor = mouse.ButtonIndex == MouseButton.WheelUp ? 0.75f : 1.333333f;
        ViewRadiusTiles = Mathf.Clamp(
            ViewRadiusTiles * factor,
            Mathf.Max(1f, MinimumRadiusTiles),
            Mathf.Max(MinimumRadiusTiles, MaximumRadiusTiles));
        StartBuild();
        GetViewport().SetInputAsHandled();
    }
    #endregion

    #region Map Building
    // =========================================================
    // Centre the requested map on the player's logical ground position.
    private void Recenter()
    {
        _centre = IsoGrid.WorldToTile(
            _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
        StartBuild();
    }

    // =========================================================
    // Start a fresh snapshot without requesting or loading world chunks.
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
    // Spread procedural sampling across frames while keeping the marker live.
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

                _image.SetPixel(x, y, SampleColor(tile));
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
    // Display actual terrain, chasms and the assigned liquid profile's colour.
    private Color SampleColor(Vector2 tile)
    {
        float minimum = -(_chunks.WorldChunksPerAxis / 2)
            * _chunks.ChunkSize - 0.5f;
        float maximum = minimum +
            _chunks.WorldChunksPerAxis * _chunks.ChunkSize;

        if (tile.X < minimum || tile.Y < minimum ||
            tile.X >= maximum || tile.Y >= maximum)
            return new Color("#030509");

        WorldSample sample = _generator.SampleTile(tile);
        if (!sample.Walkable) return _voidColor;

        WaterBasinWorld.Basin basin = _basins?.GetBasinAt(tile);
        if (basin != null && basin.Fill > 0.0001f &&
            basin.WaterHeight > sample.Height)
        {
            float depth = Mathf.Clamp(
                (basin.WaterHeight - sample.Height) /
                basin.Definition.BasinDepth, 0f, 1f);
            Color liquid = basin.Definition.Liquid.SurfaceColour;
            float brightness = Mathf.Lerp(1.4f, 0.65f, depth);

            return new Color(
                Mathf.Clamp(liquid.R * brightness, 0f, 1f),
                Mathf.Clamp(liquid.G * brightness, 0f, 1f),
                Mathf.Clamp(liquid.B * brightness, 0f, 1f), 1f);
        }

        Color color = BiomeColor(sample.BiomeId);
        float neighbour = _generator.GetHeight(tile + new Vector2(1f, 1f));
        float slope = Mathf.Clamp(
            (sample.Height - neighbour) / 32f, -0.35f, 0.35f);
        float altitude = Mathf.Clamp(
            sample.Height / Mathf.Max(1f, _generator.HeightRange), -1f, 1f);
        float light = Mathf.Clamp(
            0.85f + slope + altitude * 0.15f, 0.4f, 1.2f);

        return new Color(
            Mathf.Clamp(color.R * light, 0f, 1f),
            Mathf.Clamp(color.G * light, 0f, 1f),
            Mathf.Clamp(color.B * light, 0f, 1f), 1f);
    }

    // =========================================================
    // Assign stable diagnostic colours without altering the terrain shader.
    private Color BiomeColor(string id)
    {
        if (_biomeColors.TryGetValue(id, out Color color)) return color;

        uint hash = 2166136261u;
        foreach (char character in id)
            hash = unchecked((hash ^ character) * 16777619u);

        color = Color.FromHsv((hash % 1000u) / 1000f, 0.35f, 0.65f);
        _biomeColors.Add(id, color);
        return color;
    }
    #endregion

    #region Layout And Drawing
    // =========================================================
    // Keep the debug window centred and inside the current viewport.
    private Rect2 PanelRect()
    {
        Vector2 viewport = GetViewportRect().Size;
        Vector2 size = new(
            Mathf.Min(PanelSize.X, Mathf.Max(160f, viewport.X - 32f)),
            Mathf.Min(PanelSize.Y, Mathf.Max(160f, viewport.Y - 32f)));
        return new Rect2((viewport - size) * 0.5f, size);
    }

    // =========================================================
    // Preserve a square top-down map instead of stretching tile distances.
    private Rect2 MapRect()
    {
        Rect2 panel = PanelRect();
        float side = Mathf.Max(32f, Mathf.Min(
            panel.Size.X - 32f, panel.Size.Y - 100f));
        return new Rect2(
            panel.Position + new Vector2((panel.Size.X - side) * 0.5f, 44f),
            new Vector2(side, side));
    }

    // =========================================================
    // Draw the cached map, moving player marker and compact debug instructions.
    public override void _Draw()
    {
        if (!_open) return;

        Font font = ThemeDB.FallbackFont;
        Rect2 panel = PanelRect();
        Rect2 map = MapRect();
        DrawRect(panel, new Color(0.025f, 0.04f, 0.06f, 0.96f));
        DrawRect(panel, new Color("#536674"), false, 2f);
        DrawString(font, panel.Position + new Vector2(16, 28),
            "WORLD GENERATION DEBUG — M close · Wheel zoom · R recenter",
            HorizontalAlignment.Left, -1, 17, new Color("#d8e5ea"));

        DrawRect(map, new Color("#030509"));
        if (_texture != null) DrawTextureRect(_texture, map, false);
        DrawRect(map, new Color("#526371"), false, 1f);

        if (_texture != null)
        {
            Vector2 tile = IsoGrid.WorldToTile(
                _ground.ToLocal(_player.GlobalPosition), _chunks.TileSize);
            Vector2 normalized = (tile - _centre) / (_buildRadius * 2f)
                + new Vector2(0.5f, 0.5f);
            Vector2 point = map.Position + normalized * map.Size;

            if (map.HasPoint(point))
            {
                DrawCircle(point, 6f, Colors.Black);
                DrawCircle(point, 4f, new Color("#ffed8a"));
            }
        }

        string status = _building
            ? $"Building {_pixel * 100 / (_resolution * _resolution)}%..."
            : $"Width {_buildRadius * 2f:0} tiles · " +
                $"Seed {_chunks.WorldSeed} · Basins {_basins?.Basins.Count ?? 0}";

        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 34),
            status, HorizontalAlignment.Left, -1, 15, new Color("#d8e5ea"));
        DrawString(font,
            panel.Position + new Vector2(16, panel.Size.Y - 13),
            "Biome colours + height shading · Cyan: water · Black: chasm/outside world",
            HorizontalAlignment.Left, -1, 13, new Color("#97aebc"));
    }
    #endregion
}