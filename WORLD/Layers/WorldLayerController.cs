// Coordinates surface/cave switching, streaming readiness and shared height queries.
// Location state remains independent of inventory and debug input ownership.
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
    private float _surfaceOpacity = 1f;
    private Label _status;
    private readonly List<WorldLayerMember> _surface = new();
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
    // Wait for the cave setup helper.
    public override void _Ready()
    {
        SetProcess(false);
    }

    // =========================================================
    // Restore the surface when the feature is removed.
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
    // Connect the shared player and start underground preloading.
    public void Configure(Node world, Player player, CaveWorld cave)
    {
        _world = world;
        _player = player;
        Cave = cave;
        _config = WorldConfig.Find(world);
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
    // Traverse only the entrance corridor and animate surface visibility.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player)) return;

        Health health = _player.GetNode<Health>("Systems/Health");
        if (!health.IsAlive && Current == WorldLayer.Cave)
            ReturnToSurface();

        Vector2 tile = Cave.WorldToTile(_player.GlobalPosition);
        bool corridor = Mathf.Abs(tile.Y) < 1.4f;

        if (health.IsAlive && InputModes.For(_player).GameplayAllowed)
        {
            if (Current == WorldLayer.Surface && corridor &&
                tile.X >= 0f && tile.X < 0.6f &&
                Cave.Streaming.EntryReady())
                EnterCave();
            else if (Current == WorldLayer.Cave &&
                corridor && tile.X < -0.6f)
                ReturnToSurface();
        }

        float target = 1f;

        if (Current == WorldLayer.Cave)
        {
            float descent = corridor
                ? Mathf.Clamp(tile.X / Cave.TunnelLengthTiles, 0f, 1f) : 1f;
            float dim = Mathf.Clamp(
                _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f);
            target = dim * (1f - descent);

            float height = Cave.Elevation.SampleWorldHeight(_player.GlobalPosition);
            _camera.Position = _cameraPosition +
                Vector2.Down * (Cave.RimHeight - height);
        }
        else
            _camera.Position = _cameraPosition;

        float step = (float)delta /
            Mathf.Max(0.05f, _config.ObstructionFadeSeconds);
        _surfaceOpacity = Mathf.MoveToward(_surfaceOpacity, target, step);

        foreach (WorldLayerMember member in _surface)
            if (GodotObject.IsInstanceValid(member))
                member.SetOpacity(_surfaceOpacity);

        Cave.Entrance.Visible = Current == WorldLayer.Surface;
        _status.Text = Current == WorldLayer.Cave
            ? $"CAVE | {Cave.Streaming.ReadyCount}/{Cave.Streaming.LoadedCount} chunks\n" +
                "Return through entrance | M map is surface-only"
            : Cave.Streaming.EntryReady()
                ? "SURFACE | Cave entrance ready"
                : "SURFACE | Preparing cave entrance...";
    }
    #endregion

    #region Switching
    // =========================================================
    // Activate completed cave chunks after the entrance buffer is ready.
    private void EnterCave()
    {
        if (!Cave.Streaming.EntryReady()) return;

        CaptureSurface();
        foreach (WorldLayerMember member in _surface)
            member.SetActive(false);

        _underground.SetActive(true);
        _underground.SetOpacity(1f);
        Cave.Streaming.SetActive(true);
        Current = WorldLayer.Cave;
        Epoch++;
        StopMining();
        GD.Print("[Layers] Entered cave.");
    }

    // =========================================================
    // Restore surface simulation and disable every completed cave chunk.
    public void ReturnToSurface()
    {
        if (Current == WorldLayer.Surface) return;

        Cave.Streaming.SetActive(false);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);

        foreach (WorldLayerMember member in _surface)
        {
            if (!GodotObject.IsInstanceValid(member)) continue;
            member.SetActive(true);
            member.SetOpacity(_surfaceOpacity);
        }

        Current = WorldLayer.Surface;
        Epoch++;
        _camera.Position = _cameraPosition;
        StopMining();
        GD.Print("[Layers] Returned to surface.");
    }

    // =========================================================
    // Capture surface roots while keeping pooled projectiles available for reuse.
    private void CaptureSurface()
    {
        _surface.Clear();
        AddSurface(_world.GetNode("GroundChunks"));

        foreach (Node child in _world.GetNode("WorldObjects").GetChildren())
            if (child != _player && child is not Projectile)
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
    // Register one managed surface root.
    private void AddSurface(Node node)
    {
        _surface.Add(WorldLayerMember.Attach(node, WorldLayer.Surface));
    }

    // =========================================================
    // Stop the mining component without depending on its scene node name.
    private void StopMining()
    {
        foreach (Node node in _player.GetNode("Systems").GetChildren())
            if (node is MiningEmitter mining)
                mining.Stop();
    }
    #endregion

    #region Movement
    // =========================================================
    // Prevent cave movement into missing floor or unfinished chunks.
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
    // Check intermediate points so fast movement cannot skip unready space.
    private bool CanTravel(Vector2 position, Vector2 motion)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(motion.Length() / 12f));
        if (steps > 64) return false;

        for (int i = 1; i <= steps; i++)
            if (!Cave.Streaming.IsAvailable(position + motion * ((float)i / steps)))
                return false;
        return true;
    }
    #endregion

    #region Shared Queries
    // =========================================================
    // Find the optional controller in the current scene tree.
    public static WorldLayerController Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Select the owner's elevation provider.
    public static float HeightFor(Node owner, Vector2 point)
    {
        WorldLayerController controller = Find(owner);
        if (controller != null &&
            WorldLayerMember.For(owner) == WorldLayer.Cave)
            return controller.Cave.Elevation.SampleWorldHeight(point);

        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup(
            "terrain_elevation") as TerrainElevation;
        return elevation?.SampleWorldHeight(point) ?? 0f;
    }

    // =========================================================
    // Route player-created drops to their current layer.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerController controller = Find(context);
        return controller?.Current == WorldLayer.Cave
            ? controller.Cave.Objects : surfaceRoot;
    }
    #endregion
}