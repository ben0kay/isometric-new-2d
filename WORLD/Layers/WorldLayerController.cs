// Coordinates bidirectional layer connections without owning terrain generation.
// Surface pausing, landing checks and connection metadata have focused helpers.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldLayerController : Node
{
    #region Configuration
    [ExportGroup("Connection Loading")]
    [Export(PropertyHint.Range, "16,256,8")]
    public float ExitPreloadDistanceTiles { get; set; } = 96f;
    #endregion

    #region State
    public string Current => Worlds?.ActiveLayer ?? WorldLayerId.Surface;
    public int Epoch { get; private set; }
    public WorldLayerRuntime Worlds { get; private set; }
    public WorldLayerDefinition CurrentDefinition => _config.GetLayerCatalog().Get(Current);
    public WorldLayerConnection LastSurfaceConnection { get; private set; }
    public event Action<string, string, WorldLayerConnection> LayerChanged;

    private Player _player;
    private Health _health;
    private Camera2D _camera;
    private Vector2 _cameraPosition;
    private WorldConfig _config;
    private ChunkController _surfaceChunks;
    private WorldLayerSurface _surface;
    private WorldLayerLanding _landing;
    private InfiniteWorldGeneration _generation;
    private Label _status;
    private WorldLayerConnection _pending, _arrival;
    private bool _ready, _arrivalDeparted;
    private Vector2 _destination;
    private double _landingTimer, _hudTimer;
    private string _previewLayer;
    private float _previewOpacity;
    private readonly Dictionary<WorldLayerConnection, IDisposable> _routeLeases = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Register one controller without running before its dependencies exist.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_controller");
        SetProcess(false);
    }

    // =========================================================
    // Bind shared player/services and create small runtime helpers.
    public void Configure(Node world, Player player, WorldLayerRuntime worlds)
    {
        Worlds = worlds; _player = player;
        _health = player.GetNode<Health>("Systems/Health");
        _config = WorldConfig.Find(world);
        _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _generation = InfiniteWorldGeneration.Find(world);
        _camera = player.GetNode<Camera2D>("Camera2D");
        _cameraPosition = _camera.Position;
        _surface = new WorldLayerSurface { Name = "SurfacePresentation" };
        AddChild(_surface);
        _surface.Configure(world, player);
        _landing = new WorldLayerLanding(worlds, world);
        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
        AddChild(hud);
        _status = new Label { Position = new Vector2(16, 200) };
        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
        hud.AddChild(_status);
        WorldLayerPursuit.Ensure(this, world);
        SetProcess(true);
    }

    // =========================================================
    // Advance preparation before checking the small physical crossing boundary.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player)) return;
        if (Current != WorldLayerId.Surface) _surfaceChunks.RetireUnusedChunks();
        _surface.Drain();
        _landingTimer -= delta; _hudTimer -= delta;
        if (!_health.IsAlive && Current != WorldLayerId.Surface) ReturnToSurface();

        WorldLayerConnection approach = FindApproach();
        if (approach != _pending)
        {
            CancelPending();
            _pending = approach;
            _landingTimer = 0;
        }
        if (_pending != null && !_ready && _landing.Prepare(_pending, Current))
        {
            _surface.Drain();
            if (_landingTimer <= 0)
            {
                _landingTimer = 0.25;
                _ready = _landing.TryReady(_pending, Current, out _destination);
            }
        }
        if (_pending != null && _ready && _health.IsAlive &&
            InputModes.For(_player).GameplayAllowed && AtCrossing(_pending, _player.GlobalPosition))
            Transfer(_pending, _destination);
        UpdatePresentation(delta);
    }

    // =========================================================
    // Release protected metadata and restore the shared camera on teardown.
    public override void _ExitTree()
    {
        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
        _routeLeases.Clear();
        if (GodotObject.IsInstanceValid(_camera)) _camera.Position = _cameraPosition;
    }
    #endregion

    #region Connection Selection
    // =========================================================
    // Prefer a connection underfoot, otherwise preload the nearest known endpoint.
    private WorldLayerConnection FindApproach()
    {
        CaveWorld primary = Worlds.SurfaceUnderground;
        Vector2 tile = primary.WorldToTile(_player.GlobalPosition);
        WorldLayerConnection best = null;
        float distance = ExitPreloadDistanceTiles * ExitPreloadDistanceTiles;
        foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
        {
            Vector2 local = connection.Coordinates(tile);
            if (connection == _arrival && Current == connection.LowerLayer && !_arrivalDeparted)
            {
                if (local.X > 1f || local.X < -0.1f || Mathf.Abs(local.Y) > 2f)
                    _arrivalDeparted = true;
                else continue;
            }
            // Ascending connections can be approached along their full ramp.
            bool onRamp = Current == connection.LowerLayer &&
                local.X >= -1.5f && local.X <= connection.TunnelLength &&
                Mathf.Abs(local.Y) < 1.4f;
            bool atMouth = local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) < 1.4f;
            if (onRamp || atMouth) return connection;
            float candidate = tile.DistanceSquaredTo(connection.MouthTile);
            if (candidate >= distance) continue;
            distance = candidate; best = connection;
        }
        return best;
    }

    // =========================================================
    // Never transfer from a distant preload: cross only at the actual mouth seam.
    private bool AtCrossing(WorldLayerConnection connection, Vector2 position)
    {
        Vector2 local = connection.Coordinates(Worlds.SurfaceUnderground.WorldToTile(position));
        return Mathf.Abs(local.Y) < 1.4f && (Current == connection.UpperLayer
            ? local.X >= 0.05f && local.X <= 0.6f
            : local.X >= -1.5f && local.X <= -0.45f);
    }

    // =========================================================
    // Release only the old destination's temporary preparation request.
    private void CancelPending()
    {
        if (_pending != null)
        {
            string target = _pending.Other(Current);
            if (target != WorldLayerId.Surface) Worlds.CancelPreload(target);
        }
        _pending = null; _ready = false;
    }
    #endregion

    #region Transfer
    // =========================================================
    // Change ownership, collision and movement together once landing is validated.
    private void Transfer(WorldLayerConnection connection, Vector2 position)
    {
        string from = Current, destination = connection.Other(from);
        if (from == WorldLayerId.Surface)
        {
            LastSurfaceConnection = connection;
            _surface.Pause();
        }
        HoldRoute(connection);
        CancelPending();
        HidePreview();
        Worlds.ActivateLayer(destination);
        _player.GlobalPosition = position;
        _player.Velocity = Vector2.Zero;
        _arrival = connection;
        _arrivalDeparted = destination != connection.LowerLayer;
        Epoch++;
        connection.Marker?.QueueRedraw();
        if (destination == WorldLayerId.Surface)
        {
            _surface.Resume();
            foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
            _routeLeases.Clear();
        }
        _camera.ResetSmoothing();
        StopMining();
        WorldLayerPursuit.Find(this)?.PlayerCrossed(connection, destination, _player);
        GD.Print($"[Layers] {from} -> {destination} through {connection.Id}");
        LayerChanged?.Invoke(from, destination, connection);
    }

    // =========================================================
    // Protect the original surface entrance during a multi-depth journey.
    private void HoldRoute(WorldLayerConnection connection)
    {
        if (connection.UpperLayer != WorldLayerId.Surface || _routeLeases.ContainsKey(connection)) return;
        _routeLeases.Add(connection, _generation.PinArea(new Rect2(
            connection.MouthTile - Vector2.One * 4f, Vector2.One * 8f)));
    }

    // =========================================================
    // Preserve death/respawn behaviour without treating every exit as surface.
    public void ReturnToSurface()
    {
        if (Current == WorldLayerId.Surface) return;
        CancelPending(); HidePreview();
        Worlds.ActivateLayer(WorldLayerId.Surface);
        if (LastSurfaceConnection != null)
            _player.GlobalPosition = LastSurfaceConnection.OutsidePosition(Worlds.SurfaceUnderground.TileSize);
        _player.Velocity = Vector2.Zero;
        _surface.Resume();
        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
        _routeLeases.Clear();
        _arrival = null;
        Epoch++;
        _camera.Position = _cameraPosition;
        _camera.ResetSmoothing();
        StopMining();
        // Respawn is a reset, not a traversable shortcut for pursuing actors.
        WorldLayerPursuit.Find(this)?.PlayerCrossed(null, Current, _player);
    }

    // =========================================================
    // Stop active mining across a change of collision world.
    private void StopMining()
    {
        foreach (Node node in _player.GetNode("Systems").GetChildren())
            if (node is MiningEmitter mining) mining.Stop();
    }
    #endregion

    #region Presentation
    // =========================================================
    // Fade only the departure layer while traversing a lower-owned ramp.
    private void UpdatePresentation(double delta)
    {
        string upper = null;
        float target = 0f;
        WorldLayerConnection ramp = null;
        if (Current != WorldLayerId.Surface)
        {
            CaveWorld cave = Worlds.GetUnderground(Current);
            Vector2 tile = cave.WorldToTile(_player.GlobalPosition);
            ramp = cave.TransitionAt(tile);
            if (ramp != null)
            {
                float descent = Mathf.Clamp(ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f);
                upper = ramp.UpperLayer;
                target = Mathf.Clamp(_config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f) * (1f - descent);
            }
            float reference = ramp?.UpperLayer == WorldLayerId.Surface
                ? ramp.RimHeight * (1f - Mathf.Clamp(
                    ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f)) : 0f;
            _camera.Position = _cameraPosition + Vector2.Down *
                (reference - cave.Elevation.SampleWorldHeight(_player.GlobalPosition));
        }
        else _camera.Position = _cameraPosition;

        if (_previewLayer != upper)
        {
            HidePreview(); _previewLayer = upper;
        }
        _previewOpacity = Mathf.MoveToward(_previewOpacity, target,
            (float)delta / Mathf.Max(0.05f, _config.ObstructionFadeSeconds));
        _surface.SetOpacity(Current == WorldLayerId.Surface ? 1f :
            upper == WorldLayerId.Surface ? _previewOpacity : 0f);
        if (upper != null && upper != WorldLayerId.Surface)
            Worlds.GetUnderground(upper).SetPreview(_previewOpacity);

        if (_hudTimer > 0) return;
        _hudTimer = 0.2;
        _surface.Prune();
        foreach (WorldLayerConnection connection in Worlds.Connections.All)
            if (GodotObject.IsInstanceValid(connection.Marker))
                connection.Marker.Visible = Current == connection.UpperLayer ||
                    (Current == connection.LowerLayer && connection == ramp);
        _status.Text = $"{CurrentDefinition.DisplayName}\n" +
            (_pending == null ? "Explore the connected world" :
                $"{_pending.Id}: {(_ready ? "destination ready" : _landing.Status)}") +
            (Current == WorldLayerId.Surface ? "" : " | M map is surface-only");
    }

    // =========================================================
    // Remove departure previews without reactivating their simulation.
    private void HidePreview()
    {
        if (_previewLayer != null && _previewLayer != WorldLayerId.Surface)
            Worlds.GetUnderground(_previewLayer).SetPreview(0f);
        _previewLayer = null; _previewOpacity = 0f;
    }
    #endregion

    #region Movement
    // =========================================================
    // Keep the shared player's footprint inside ready terrain, allowing axis sliding.
    public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, double delta)
    {
        if (delta <= 0) return velocity;
        Vector2 motion = velocity * (float)delta;
        if (CanTravel(position, motion)) return velocity;
        bool x = CanTravel(position, new Vector2(motion.X, 0f));
        bool y = CanTravel(position, new Vector2(0f, motion.Y));
        if (x && (!y || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y))) return new Vector2(velocity.X, 0f);
        return y ? new Vector2(0f, velocity.Y) : Vector2.Zero;
    }

    // =========================================================
    // Hold any connection seam until that exact destination has a safe landing.
    private bool CanTravel(Vector2 position, Vector2 motion)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(motion.Length() / 12f));
        if (steps > 64) return false;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 point = position + motion * ((float)i / steps);
            if (Current != WorldLayerId.Surface && !Worlds.IsAvailable(Current, point, 14f)) return false;
            foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
                if (AtCrossing(connection, point) && (connection != _pending || !_ready)) return false;
        }
        return true;
    }
    #endregion

    #region Shared Queries
    // =========================================================
    // Resolve the gameplay world's optional layer controller.
    public static WorldLayerController Find(Node context)
    {
        return context == null || !context.IsInsideTree() ? null :
            context.GetTree().GetFirstNodeInGroup("world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Route artwork and aiming to the actor's exact elevation provider.
    public static float HeightFor(Node owner, Vector2 point)
    {
        string layer = WorldLayerMember.For(owner);
        if (layer != WorldLayerId.Surface)
            return (WorldLayerRuntime.Find(owner) ?? throw new InvalidOperationException(
                $"No runtime for '{layer}'.")).GetUnderground(layer).Elevation.SampleWorldHeight(point);
        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
        return elevation?.SampleWorldHeight(point) ?? 0f;
    }

    // =========================================================
    // Keep player-created drops in the active world's object root.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
        return runtime == null ? surfaceRoot : runtime.ObjectsFor(runtime.ActiveLayer);
    }
    #endregion
}
