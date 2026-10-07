// Coordinates cave transitions, incremental surface registration and exit readiness.
// Surface simulation stays paused while destination chunks are prepared.
using Godot;
using System.Collections.Generic;

public partial class WorldLayerController : Node
{

        #region Configuration
    [ExportGroup("Exit Loading")]
    [Export(PropertyHint.Range, "16,256,8")]
    public float ExitPreloadDistanceTiles { get; set; } = 96f;
    #endregion

    #region State
    public WorldLayer Current { get; private set; } = WorldLayer.Surface;
    public int Epoch { get; private set; }
    public CaveWorld Cave { get; private set; }

    private Node _world;
    private Node2D _ground, _objects;
    private Player _player;
    private Camera2D _camera;
    private SceneTree _tree;
    private Vector2 _cameraPosition;
    private WorldConfig _config;
    private ChunkController _surfaceChunks;
    private Label _status;

    private float _surfaceOpacity = 1f;
    private float _appliedOpacity = float.NaN;
    private double _landingTimer;
    private double _hudTimer;

    private CaveHole _lastEntry, _pendingExit;
    private bool _entryDeparted;
    private bool _exitReady, _surfaceLoaded;
    private Vector2 _exitLanding;
    private string _exitStatus = "";

    private readonly List<WorldLayerMember> _surface = new();
    private readonly Dictionary<Node, WorldLayerMember> _roots = new();
    private readonly HashSet<WorldLayerMember> _inheritedFade = new();
    private readonly Queue<Node> _addedSurface = new();
    private WorldLayerMember _underground;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the optional layer service.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_controller");
    }

    // =========================================================
    // Wait for cave configuration.
    public override void _Ready()
    {
        SetProcess(false);
    }

    // =========================================================
    // Disconnect listeners and restore managed surface branches.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_tree))
            _tree.NodeAdded -= OnNodeAdded;

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
    // Connect the shared player, surface streamer and cave services.
    public void Configure(Node world, Player player, CaveWorld cave)
    {
        _world = world;
        _player = player;
        Cave = cave;
        _ground = world.GetNode<Node2D>("GroundChunks");
        _objects = world.GetNode<Node2D>("WorldObjects");
        _config = WorldConfig.Find(world);
        _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _camera = player.GetNode<Camera2D>("Camera2D");
        _cameraPosition = _camera.Position;

        _underground = WorldLayerMember.Attach(cave.Root, WorldLayer.Cave);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);
        cave.Streaming.ConfigurePlayer(player);
        cave.Streaming.SetActive(false);

        _tree = GetTree();
        _tree.NodeAdded += OnNodeAdded;

        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
        AddChild(hud);
        _status = new Label { Position = new Vector2(16, 200) };
        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
        hud.AddChild(_status);
        SetProcess(true);
    }

    // =========================================================
    // Preload nearby exits while exploring, then switch layers at the mouth.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player)) return;

        _landingTimer -= delta;
        _hudTimer -= delta;

        Health health = _player.GetNode<Health>("Systems/Health");
        if (!health.IsAlive && Current == WorldLayer.Cave)
            ReturnToSurface();

        Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);

        if (Current == WorldLayer.Surface &&
            health.IsAlive && InputModes.For(_player).GameplayAllowed)
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

        if (Current == WorldLayer.Cave)
        {
            CaveHole approach = Cave.TransitionAt(tile);

            // Avoid treating the initial descent as an exit request.
            // Turning back immediately still allows returning to the surface.
            if (!_entryDeparted && _lastEntry != null)
            {
                float along = _lastEntry.Coordinates(tile).X;
                if (approach != _lastEntry || along > 1f)
                    _entryDeparted = true;
                else if (along >= -0.1f)
                    approach = null;
            }

            // Outside a ramp, start loading the nearest nearby exit.
            // A ramp always takes priority over distance-based selection.
            if (approach == null)
            {
                float radius = Mathf.Clamp(
                    ExitPreloadDistanceTiles, 16f, 256f);
                float nearestDistance = radius * radius;

                foreach (CaveHole hole in Cave.Holes)
                {
                    if (!_entryDeparted && hole == _lastEntry)
                        continue;

                    float distance = tile.DistanceSquaredTo(hole.MouthTile);
                    if (distance >= nearestDistance) continue;

                    nearestDistance = distance;
                    approach = hole;
                }
            }

            if (approach != _pendingExit)
            {
                _pendingExit = approach;
                _exitReady = false;
                _surfaceLoaded = false;
                _landingTimer = 0;
                _exitStatus = "";
            }

            if (_pendingExit != null && !_exitReady)
            {
                if (!_surfaceLoaded)
                {
                    _surfaceLoaded = _surfaceChunks.PrepareDestination(
                        _pendingExit.OutsidePosition(Cave.TileSize));

                    DrainAddedSurface();

                    if (!_surfaceLoaded)
                        _exitStatus = _surfaceChunks.GetMeta(
                            "destination_preload_status",
                            "loading surface").AsString();
                }

                if (_surfaceLoaded && _landingTimer <= 0)
                {
                    _landingTimer = 0.25;
                    _exitReady = TrySurfaceLanding(
                        _pendingExit, out _exitLanding);

                    _exitStatus = _exitReady
                        ? "surface ready"
                        : "surface loaded; no safe landing found";
                }
            }

            DrainAddedSurface();

            if (_pendingExit != null && _exitReady &&
                health.IsAlive && InputModes.For(_player).GameplayAllowed)
            {
                Vector2 local = _pendingExit.Coordinates(tile);

                // Preloading from far away must never teleport the player.
                // Switch only within the small apron at the actual mouth.
                if (local.X >= -1.5f && local.X <= -0.45f &&
                    Mathf.Abs(local.Y) < 1.4f)
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

    #region Incremental Surface Registration
    // =========================================================
    // Queue newly added surface content while excluding shared gameplay nodes.
    private void OnNodeAdded(Node node)
    {
        if (Current != WorldLayer.Cave || node is WorldLayerMember)
            return;

        bool surface = _ground.IsAncestorOf(node) || _objects.IsAncestorOf(node);
        for (Node parent = node; parent != null; parent = parent.GetParent())
        {
            if (parent is Player || parent is Projectile) return;
            if (_roots.ContainsKey(parent)) surface = true;
        }

        if (surface) _addedSurface.Enqueue(node);
    }

    // =========================================================
    // Pause new branches without revisiting existing surface hierarchies.
    private void DrainAddedSurface()
    {
        while (_addedSurface.Count > 0)
        {
            Node node = _addedSurface.Dequeue();
            if (!GodotObject.IsInstanceValid(node) ||
                node.IsQueuedForDeletion() || !node.IsInsideTree())
                continue;

            bool covered = false;
            for (Node parent = node; parent != null; parent = parent.GetParent())
            {
                if (!_roots.TryGetValue(parent, out WorldLayerMember member))
                    continue;
                covered = member.Covers(node);
                break;
            }

            if (covered) continue;

            WorldLayerMember added = RegisterRoot(node);
            added.SetActive(false);
            if (!_inheritedFade.Contains(added))
                added.SetOpacity(_surfaceOpacity);
        }
    }

    // =========================================================
    // Register a branch and detect inherited canvas fading.
    private WorldLayerMember RegisterRoot(Node node)
    {
        if (_roots.TryGetValue(node, out WorldLayerMember existing))
            return existing;

        bool inherited = false;
        for (Node parent = node.GetParent(); parent != null; parent = parent.GetParent())
        {
            if (parent is CanvasItem && _roots.ContainsKey(parent))
            {
                inherited = true;
                break;
            }
        }

        WorldLayerMember member = WorldLayerMember.Attach(node, WorldLayer.Surface);
        _roots.Add(node, member);
        _surface.Add(member);
        if (inherited) _inheritedFade.Add(member);
        return member;
    }

    // =========================================================
    // Include branches independently registered during earlier visits.
    private void RegisterExistingBranches(Node node)
    {
        if (node is WorldLayerMember || node.IsQueuedForDeletion()) return;

        WorldLayerMember member =
            node.GetNodeOrNull<WorldLayerMember>("WorldLayerMember");
        if (member != null && member.Layer == WorldLayer.Surface)
            RegisterRoot(node);

        foreach (Node child in node.GetChildren())
            RegisterExistingBranches(child);
    }

    // =========================================================
    // Collect surface ownership once when entering a cave.
    private void CaptureSurface()
    {
        _surface.Clear();
        _roots.Clear();
        _inheritedFade.Clear();
        _addedSurface.Clear();

        RegisterRoot(_ground);
        RegisterExistingBranches(_ground);

        foreach (Node child in _objects.GetChildren())
        {
            if (child == _player || child is Projectile ||
                child.IsQueuedForDeletion()) continue;
            RegisterRoot(child);
            RegisterExistingBranches(child);
        }

        Node systems = _world.GetNode("Systems");
        foreach (string name in new[]
        {
            "ChunkController", "WorldNavigation", "EnemyPopulation",
            "Atmosphere", "GroundFog", "VegetationInteraction", "Surfaces"
        })
        {
            Node node = systems.GetNodeOrNull<Node>(name);
            if (node == null) continue;
            RegisterRoot(node);
            RegisterExistingBranches(node);
        }

        Node fading = _config.GetNodeOrNull<Node>("PlayerObstructionFade");
        if (fading != null) RegisterRoot(fading);
    }
    #endregion

    #region Switching And Landing
    // =========================================================
    // Align entry and pause existing surface simulation once.
    private void EnterCave(CaveHole hole)
    {
        if (!Cave.Streaming.EntryReady(hole)) return;

        Vector2 local = hole.Coordinates(Cave.WorldToTile(_player.GlobalPosition));
        Vector2 entry = Cave.TileToWorld(hole.TileAt(Mathf.Clamp(local.X, 0f, 0.5f)));
        if (!Cave.Streaming.IsAvailable(entry)) return;

        CaptureSurface();

        // Restore presentation before any branch takes a fresh snapshot.
        foreach (WorldLayerMember member in _surface)
            member.SetOpacity(1f);
        foreach (WorldLayerMember member in _surface)
            member.SetActive(false);

        _player.GlobalPosition = entry;
        _player.Velocity = Vector2.Zero;
        _lastEntry = hole;
        _entryDeparted = false;
        _pendingExit = null;
        _exitReady = false;
        _surfaceLoaded = false;
        _appliedOpacity = float.NaN;

        _underground.SetActive(true);
        _underground.SetOpacity(1f);
        Cave.Streaming.SetActive(true);
        Current = WorldLayer.Cave;
        Epoch++;
        StopMining();
        GD.Print($"[Layers] Entered through hole {hole.Id}.");
    }

    // =========================================================
    // Preserve the existing death and respawn integration.
    public void ReturnToSurface()
    {
        if (Current == WorldLayer.Surface) return;
        RestoreSurface(_lastEntry != null
            ? _lastEntry.OutsidePosition(Cave.TileSize)
            : _player.GlobalPosition);
    }

    // =========================================================
    // Restore existing and newly streamed surface branches.
    private void RestoreSurface(Vector2 landing)
    {
        DrainAddedSurface();
        Cave.Streaming.SetActive(false);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);

        _player.GlobalPosition = landing;
        _player.Velocity = Vector2.Zero;

        foreach (WorldLayerMember member in _surface)
        {
            if (!GodotObject.IsInstanceValid(member)) continue;
            member.SetActive(true);
            if (!_inheritedFade.Contains(member))
                member.SetOpacity(_surfaceOpacity);
        }

        Current = WorldLayer.Surface;
        Epoch++;
        _pendingExit = null;
        _exitReady = false;
        _surfaceLoaded = false;
        _appliedOpacity = float.NaN;
        _camera.Position = _cameraPosition;
        _camera.ResetSmoothing();
        StopMining();
    }

    // =========================================================
    // Check ready terrain and obstacle footprints without disabled physics queries.
    private bool TrySurfaceLanding(CaveHole hole, out Vector2 landing)
    {
        Vector2 centre = hole.OutsidePosition(Cave.TileSize);
        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);

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
                Vector2 separation = obstacle.Footprint * 0.5f + new Vector2(18f, 18f);
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
    // Stop mining without depending on the component's node name.
    private void StopMining()
    {
        foreach (Node node in _player.GetNode("Systems").GetChildren())
            if (node is MiningEmitter mining)
                mining.Stop();
    }
    #endregion

    #region Presentation
    // =========================================================
    // Fade only when opacity changes and refresh diagnostic text periodically.
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

            target = Mathf.Clamp(
                _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f) * (1f - descent);

            float reference = ramp != null
                ? Mathf.Lerp(ramp.RimHeight, Cave.RimHeight, descent)
                : Cave.RimHeight;
            _camera.Position = _cameraPosition + Vector2.Down *
                (reference - Cave.Elevation.SampleWorldHeight(_player.GlobalPosition));
        }
        else
            _camera.Position = _cameraPosition;

        _surfaceOpacity = Mathf.MoveToward(
            _surfaceOpacity, target,
            (float)delta / Mathf.Max(0.05f, _config.ObstructionFadeSeconds));

        if (float.IsNaN(_appliedOpacity) ||
            !Mathf.IsEqualApprox(_appliedOpacity, _surfaceOpacity))
        {
            _appliedOpacity = _surfaceOpacity;
            foreach (WorldLayerMember member in _surface)
                if (GodotObject.IsInstanceValid(member) &&
                    !_inheritedFade.Contains(member))
                    member.SetOpacity(_surfaceOpacity);
        }

        foreach (CaveHole hole in Cave.Holes)
        {
            if (!GodotObject.IsInstanceValid(hole.Marker)) continue;
            bool visible = Current == WorldLayer.Surface ||
                hole == _pendingExit || (!_entryDeparted && hole == _lastEntry);
            if (hole.Marker.Visible != visible)
                hole.Marker.Visible = visible;
        }

        if (_hudTimer > 0) return;
        _hudTimer = 0.2;

        if (Current == WorldLayer.Cave)
            _status.Text =
                $"CAVE | {Cave.Streaming.ReadyCount}/{Cave.Streaming.LoadedCount} chunks\n" +
                (_pendingExit == null ? "Explore the connected network" :
                    $"Hole {_pendingExit.Id}: {_exitStatus}") +
                " | M map is surface-only";
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
    // Restrict cave movement to available floor, allowing axis sliding.
    public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, double delta)
    {
        if (Current != WorldLayer.Cave || delta <= 0) return velocity;

        Vector2 motion = velocity * (float)delta;
        if (CanTravel(position, motion)) return velocity;

        bool canX = CanTravel(position, new Vector2(motion.X, 0f));
        bool canY = CanTravel(position, new Vector2(0f, motion.Y));
        if (canX && (!canY || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y)))
            return new Vector2(velocity.X, 0f);
        if (canY) return new Vector2(0f, velocity.Y);
        return Vector2.Zero;
    }

    // =========================================================
    // Hold the outward boundary until the matching surface exit is ready.
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
    // Find the optional layer controller.
    public static WorldLayerController Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Select the appropriate elevation provider.
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
    // Route player-created drops into the active layer.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerController controller = Find(context);
        return controller?.Current == WorldLayer.Cave
            ? controller.Cave.Objects : surfaceRoot;
    }
    #endregion
}