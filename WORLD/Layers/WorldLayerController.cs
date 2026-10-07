// Coordinates paired cave holes and destination surface loading.
// Surface simulation stays paused while its builder prepares an underground exit.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerController : Node
{
    #region State
    public WorldLayer Current { get; private set; } = WorldLayer.Surface;
    public int Epoch { get; private set; }
    public CaveWorld Cave { get; private set; }

    private Node _world;
    private Player _player;
    private Camera2D _camera;
    private Vector2 _cameraPosition;
    private WorldConfig _config;
    private ChunkController _surfaceChunks;
    private float _surfaceOpacity = 1f;
    private Label _status;

    private CaveHole _lastEntry, _pendingExit;
    private bool _exitReady;
    private Vector2 _exitLanding;

    private readonly List<WorldLayerMember> _surface = new();
    private WorldLayerMember _underground;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the optional location service.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_controller");
    }

    // =========================================================
    // Wait for the debug setup helper.
    public override void _Ready()
    {
        SetProcess(false);
    }

    // =========================================================
    // Restore surface roots if this feature is removed.
    public override void _ExitTree()
    {
        foreach (WorldLayerMember member in _surface)
        {
            if (!GodotObject.IsInstanceValid(member)) continue;
            member.SetActive(true);
            member.SetOpacity(1f);
        }

        if (GodotObject.IsInstanceValid(_camera))
            _camera.Position = _cameraPosition;
    }

    // =========================================================
    // Connect the shared player, cave streamer and existing surface streamer.
    public void Configure(Node world, Player player, CaveWorld cave)
    {
        _world = world;
        _player = player;
        Cave = cave;
        _config = WorldConfig.Find(world);
        _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _camera = player.GetNode<Camera2D>("Camera2D");
        _cameraPosition = _camera.Position;

        _underground = WorldLayerMember.Attach(cave.Root, WorldLayer.Cave);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);
        cave.Streaming.ConfigurePlayer(player);
        cave.Streaming.SetActive(false);

        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
        AddChild(hud);
        _status = new Label { Position = new Vector2(16, 170) };
        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
        hud.AddChild(_status);
        SetProcess(true);
    }

    // =========================================================
    // Detect either hole, preload its destination and update layer presentation.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player)) return;

        Health health = _player.GetNode<Health>("Systems/Health");
        if (!health.IsAlive && Current == WorldLayer.Cave)
            ReturnToSurface();

        Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);

        if (Current == WorldLayer.Surface)
        {
            _pendingExit = null;
            _exitReady = false;

            if (health.IsAlive && InputModes.For(_player).GameplayAllowed)
            {
                foreach (CaveHole hole in Cave.Holes)
                {
                    Vector2 local = hole.Coordinates(tile);
                    if (local.X >= 0f && local.X < 0.6f &&
                        Mathf.Abs(local.Y) < 1.4f &&
                        Cave.Streaming.EntryReady(hole))
                    {
                        EnterCave(hole);
                        tile = Cave.WorldToTile(_player.GlobalPosition);
                        break;
                    }
                }
            }
        }

        if (Current == WorldLayer.Cave)
        {
            CaveHole approach = Cave.TransitionAt(tile);
            if (approach != _pendingExit)
            {
                _pendingExit = approach;
                _exitReady = false;
            }

            if (_pendingExit != null)
            {
                bool loaded = _surfaceChunks.PrepareDestination(
                    _pendingExit.OutsidePosition(Cave.TileSize));

                // New surface props must be paused before the next physics tick.
                PauseSurfaceContent();

                _exitReady = loaded &&
                    TrySurfaceLanding(_pendingExit, out _exitLanding);

                Vector2 local = _pendingExit.Coordinates(tile);
                if (_exitReady && local.X < -0.6f &&
                    health.IsAlive && InputModes.For(_player).GameplayAllowed)
                {
                    string id = _pendingExit.Id;
                    RestoreSurface(_exitLanding);
                    GD.Print($"[Layers] Exited through hole {id}.");
                }
            }
        }

        UpdatePresentation(delta);
    }
    #endregion

    #region Switching
    // =========================================================
    // Align entry with the corridor and activate the shared cave network.
    private void EnterCave(CaveHole hole)
    {
        if (!Cave.Streaming.EntryReady(hole)) return;

        Vector2 local = hole.Coordinates(Cave.WorldToTile(_player.GlobalPosition));
        Vector2 entryPosition = Cave.TileToWorld(
            hole.TileAt(Mathf.Clamp(local.X, 0f, 0.5f)));

        if (!Cave.Streaming.IsAvailable(entryPosition)) return;

        _surface.Clear();
        CaptureSurface();
        foreach (WorldLayerMember member in _surface)
            member.SetActive(false);

        _player.GlobalPosition = entryPosition;
        _player.Velocity = Vector2.Zero;
        _lastEntry = hole;
        _pendingExit = null;
        _exitReady = false;

        _underground.SetActive(true);
        _underground.SetOpacity(1f);
        Cave.Streaming.SetActive(true);
        Current = WorldLayer.Cave;
        Epoch++;
        StopMining();

        GD.Print($"[Layers] Entered through hole {hole.Id}.");
    }

    // =========================================================
    // Keep death/respawn integration compatible with the existing no-argument call.
    public void ReturnToSurface()
    {
        if (Current == WorldLayer.Surface) return;

        Vector2 landing = _lastEntry != null
            ? _lastEntry.OutsidePosition(Cave.TileSize)
            : _player.GlobalPosition;
        RestoreSurface(landing);
    }

    // =========================================================
    // Restore surface play only after normal exit readiness has been confirmed.
    private void RestoreSurface(Vector2 landing)
    {
        Cave.Streaming.SetActive(false);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);

        _player.GlobalPosition = landing;
        _player.Velocity = Vector2.Zero;

        foreach (WorldLayerMember member in _surface)
        {
            if (!GodotObject.IsInstanceValid(member)) continue;
            member.SetActive(true);
            member.SetOpacity(_surfaceOpacity);
        }

        Current = WorldLayer.Surface;
        Epoch++;
        _pendingExit = null;
        _exitReady = false;
        _camera.Position = _cameraPosition;
        _camera.ResetSmoothing();
        StopMining();
    }

    // =========================================================
    // Register current surface roots without disabling the shared player or pool.
    private void CaptureSurface()
    {
        _surface.RemoveAll(member =>
            !GodotObject.IsInstanceValid(member) || member.IsQueuedForDeletion());

        AddSurface(_world.GetNode("GroundChunks"));

        foreach (Node child in _world.GetNode("WorldObjects").GetChildren())
            if (child != _player && child is not Projectile &&
                !child.IsQueuedForDeletion())
                AddSurface(child);

        Node systems = _world.GetNode("Systems");
        foreach (string name in new[]
        {
            "ChunkController", "WorldNavigation", "EnemyPopulation",
            "Atmosphere", "GroundFog", "VegetationInteraction", "Surfaces"
        })
        {
            Node node = systems.GetNodeOrNull<Node>(name);
            if (node != null) AddSurface(node);
        }

        Node fading = _config.GetNodeOrNull<Node>("PlayerObstructionFade");
        if (fading != null) AddSurface(fading);
    }

    // =========================================================
    // Include objects added by destination generation in the inactive surface layer.
    private void PauseSurfaceContent()
    {
        CaptureSurface();
        foreach (WorldLayerMember member in _surface)
        {
            member.SetActive(false);
            member.SetOpacity(_surfaceOpacity);
        }
    }

    // =========================================================
    // Deduplicate managed surface roots.
    private void AddSurface(Node node)
    {
        WorldLayerMember member = WorldLayerMember.Attach(node, WorldLayer.Surface);
        if (!_surface.Contains(member))
            _surface.Add(member);
    }

    // =========================================================
    // Find a loaded, walkable landing without querying disabled surface physics.
    private bool TrySurfaceLanding(CaveHole hole, out Vector2 landing)
    {
        Vector2 centre = hole.OutsidePosition(Cave.TileSize);
        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(
            _world.GetNode<Node2D>("WorldObjects"));

        for (int i = 0; i <= 8; i++)
        {
            Vector2 point = i == 0 ? centre :
                centre + Vector2.FromAngle(Mathf.Tau * (i - 1) / 8f) * 24f;

            if (!_surfaceChunks.IsNavigationPointAvailable(point, 14f))
                continue;

            bool blocked = false;
            foreach (Obstacle obstacle in obstacles)
            {
                if (!GodotObject.IsInstanceValid(obstacle) ||
                    obstacle.IsQueuedForDeletion()) continue;

                Vector2 difference = point - obstacle.GlobalPosition;
                Vector2 separation = obstacle.Footprint * 0.5f +
                    new Vector2(18f, 18f);

                if (Mathf.Abs(difference.X) < separation.X &&
                    Mathf.Abs(difference.Y) < separation.Y)
                {
                    blocked = true;
                    break;
                }
            }

            if (blocked) continue;
            landing = point;
            return true;
        }

        landing = centre;
        return false;
    }

    // =========================================================
    // Stop the beam without relying on the component's node name.
    private void StopMining()
    {
        foreach (Node node in _player.GetNode("Systems").GetChildren())
            if (node is MiningEmitter mining)
                mining.Stop();
    }
    #endregion

    #region Presentation
    // =========================================================
    // Fade at either ramp and bring the camera back to its surface offset at the mouth.
    private void UpdatePresentation(double delta)
    {
        float target = 1f;

        if (Current == WorldLayer.Cave)
        {
            Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);
            CaveHole ramp = Cave.TransitionAt(tile);
            float descent = ramp != null
                ? Mathf.Clamp(ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f)
                : 1f;

            float dim = Mathf.Clamp(
                _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f);
            target = dim * (1f - descent);

            float reference = ramp != null
                ? Mathf.Lerp(ramp.RimHeight, Cave.RimHeight, descent)
                : Cave.RimHeight;
            float height = Cave.Elevation.SampleWorldHeight(_player.GlobalPosition);

            _camera.Position = _cameraPosition + Vector2.Down * (reference - height);
        }
        else
            _camera.Position = _cameraPosition;

        float step = (float)delta /
            Mathf.Max(0.05f, _config.ObstructionFadeSeconds);
        _surfaceOpacity = Mathf.MoveToward(_surfaceOpacity, target, step);

        foreach (WorldLayerMember member in _surface)
            if (GodotObject.IsInstanceValid(member))
                member.SetOpacity(_surfaceOpacity);

        foreach (CaveHole hole in Cave.Holes)
            hole.Marker.Visible = Current == WorldLayer.Surface || hole == _pendingExit;

        if (Current == WorldLayer.Cave)
        {
            string exit = _pendingExit == null ? "Explore the connected network" :
                $"Hole {_pendingExit.Id}: " +
                (_exitReady ? "surface ready" : "preparing surface / checking landing");

            _status.Text =
                $"CAVE | {Cave.Streaming.ReadyCount}/{Cave.Streaming.LoadedCount} chunks\n" +
                exit + " | M map is surface-only";
        }
        else
        {
            CaveHole nearest = Cave.NearestSurfaceHole(_player.GlobalPosition);
            _status.Text = $"SURFACE | Nearest hole: {nearest?.Id}\n" +
                (Cave.Streaming.EntryReady(nearest)
                    ? "Cave entrance ready" : "Preparing underground entrance...");
        }
    }
    #endregion

    #region Movement
    // =========================================================
    // Restrict cave travel to ready floor and hold an exit until its surface is ready.
    public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, double delta)
    {
        if (Current != WorldLayer.Cave || delta <= 0)
            return velocity;

        Vector2 motion = velocity * (float)delta;
        if (CanTravel(position, motion)) return velocity;

        Vector2 horizontal = new(motion.X, 0f);
        Vector2 vertical = new(0f, motion.Y);
        bool canX = CanTravel(position, horizontal);
        bool canY = CanTravel(position, vertical);

        if (canX && (!canY || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y)))
            return new Vector2(velocity.X, 0f);
        if (canY)
            return new Vector2(0f, velocity.Y);

        return Vector2.Zero;
    }

    // =========================================================
    // Check intermediate positions and the readiness boundary at every hole.
    private bool CanTravel(Vector2 position, Vector2 motion)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(motion.Length() / 12f));
        if (steps > 64) return false;

        for (int i = 1; i <= steps; i++)
        {
            Vector2 point = position + motion * ((float)i / steps);
            if (!Cave.Streaming.IsAvailable(point)) return false;

            Vector2 tile = Cave.WorldToTile(point);
            foreach (CaveHole hole in Cave.Holes)
            {
                Vector2 local = hole.Coordinates(tile);
                if (local.X < -0.5f && local.X >= -1.5f &&
                    Mathf.Abs(local.Y) < 1.4f &&
                    (!_exitReady || _pendingExit != hole))
                    return false;
            }
        }

        return true;
    }
    #endregion

    #region Shared Queries
    // =========================================================
    // Find the optional location service.
    public static WorldLayerController Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Select the correct height provider for artwork and aiming.
    public static float HeightFor(Node owner, Vector2 point)
    {
        WorldLayerController controller = Find(owner);
        if (controller != null && WorldLayerMember.For(owner) == WorldLayer.Cave)
            return controller.Cave.Elevation.SampleWorldHeight(point);

        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup(
            "terrain_elevation") as TerrainElevation;
        return elevation?.SampleWorldHeight(point) ?? 0f;
    }

    // =========================================================
    // Route player-created drops to the current layer.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerController controller = Find(context);
        return controller?.Current == WorldLayer.Cave
            ? controller.Cave.Objects : surfaceRoot;
    }
    #endregion
}