// Coordinates surface/cave activation, fading and shared elevation lookup.
// Location state remains independent of inventory and debug input modes.
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
    // Register this optional feature without depending on the world root name.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_controller");
    }

    // =========================================================
    // Wait for the test helper to supply the completed cave.
    public override void _Ready()
    {
        SetProcess(false);
    }

    // =========================================================
    // Restore the surface if this feature is removed during play.
    public override void _ExitTree()
    {
        foreach (WorldLayerMember member in _surface)
            if (GodotObject.IsInstanceValid(member))
                member.SetActive(true);

        if (GodotObject.IsInstanceValid(_camera))
            _camera.Position = _cameraPosition;
    }

    // =========================================================
    // Connect the existing player and completed underground layout.
    public void Configure(Node world, Player player, CaveWorld cave)
    {
        _world = world;
        _player = player;
        Cave = cave;
        _config = WorldConfig.Find(world);
        _camera = player.GetNode<Camera2D>("Camera2D");
        _cameraPosition = _camera.Position;

        _underground = WorldLayerMember.Attach(
            cave.Root, WorldLayer.Cave);
        _underground.SetActive(false);
        _underground.SetOpacity(0f);

        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
        AddChild(hud);
        _status = new Label
        {
            Position = new Vector2(16, 170),
            Text = "SURFACE | Cave test entrance nearby"
        };
        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
        hud.AddChild(_status);
        SetProcess(true);
    }

    // =========================================================
    // Handle reversible entrance traversal and animate surface visibility.
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
                tile.X >= 0f && tile.X < 0.6f)
                EnterCave();
            else if (Current == WorldLayer.Cave && corridor && tile.X < -0.6f)
                ReturnToSurface();
        }

        float target = 1f;
        if (Current == WorldLayer.Cave)
        {
            float descent = corridor
                ? Mathf.Clamp(tile.X / Cave.TunnelLengthTiles, 0f, 1f)
                : 1f;

            float dim = Mathf.Clamp(
                _config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f);
            target = dim * (1f - descent);

            float height = Cave.Elevation.SampleWorldHeight(
                _player.GlobalPosition);
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
            ? "CAVE | Return through the entrance tunnel | M map is surface-only"
            : "SURFACE | Walk into the marked cave opening";
    }
    #endregion

    #region Switching
    // =========================================================
    // Pause surface roots and activate the already-built cave.
    private void EnterCave()
    {
        CaptureSurface();
        foreach (WorldLayerMember member in _surface)
            member.SetActive(false);

        _underground.SetActive(true);
        _underground.SetOpacity(1f);
        Current = WorldLayer.Cave;
        Epoch++;
        _player.GetNode<MiningEmitter>("Systems/MiningEmitter").Stop();
        GD.Print("[Layers] Entered cave.");
    }

    // =========================================================
    // Restore surface state and hide underground geometry.
    public void ReturnToSurface()
    {
        if (Current == WorldLayer.Surface) return;

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
        _player.GetNode<MiningEmitter>("Systems/MiningEmitter").Stop();
        GD.Print("[Layers] Returned to surface.");
    }

    // =========================================================
    // Capture current streamed objects at entry, keeping the player active.
    private void CaptureSurface()
    {
        _surface.Clear();
        AddSurface(_world.GetNode("GroundChunks"));

        Node objects = _world.GetNode("WorldObjects");
        foreach (Node child in objects.GetChildren())
            if (child != _player)
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
    // Add one managed surface root.
    private void AddSurface(Node node)
    {
        _surface.Add(WorldLayerMember.Attach(node, WorldLayer.Surface));
    }
    #endregion

    #region Shared Queries
    // =========================================================
    // Find the optional controller in this scene tree.
    public static WorldLayerController Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Select the owner's height provider without altering surface generation.
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
    // Select where player-created item drops belong.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerController controller = Find(context);
        return controller?.Current == WorldLayer.Cave
            ? controller.Cave.Objects : surfaceRoot;
    }
    #endregion

    // =========================================================
// Stop the beam without depending on the component's scene node name.
private void StopMining()
{
    foreach (Node node in _player.GetNode("Systems").GetChildren())
        if (node is MiningEmitter mining)
            mining.Stop();
}
}