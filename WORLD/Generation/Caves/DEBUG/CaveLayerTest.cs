// Places one removable cave test beside the current starting area.
// Waits for surface streaming and checks entrance clearance before building.
using Godot;

public partial class CaveLayerTest : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    [Export] public float CaveDepthPixels { get; set; } = 160f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Wait until surface terrain and startup objects are ready.
    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            SetProcess(false);
            return;
        }

        Node world = GetParent();
        ChunkController chunks = world.GetNode<ChunkController>(
            "Systems/ChunkController");
        if (!chunks.WorldReady) return;

        SetProcess(false);

        Player player = world.GetNode<Player>("WorldObjects/Player");
        TerrainElevation elevation = world.GetNode<TerrainElevation>(
            "Systems/TerrainElevation");

        using CircleShape2D shape = new() { Radius = 80f };
        using PhysicsShapeQueryParameters2D query = new()
        {
            Shape = shape,
            CollisionMask = 9,
            CollideWithAreas = false
        };

        Vector2 origin = Vector2.Zero;
        bool found = false;

        for (int i = 0; i < 24; i++)
        {
            float angle = Mathf.Tau * i / 24f;
            Vector2 candidate = player.GlobalPosition +
                Vector2.FromAngle(angle) * 420f;

            Vector2 approach = candidate -
                IsoGrid.TileToWorld(new Vector2(0.8f, 0f), chunks.TileSize);

            if (!chunks.IsNavigationPointAvailable(candidate, 16f) ||
                !chunks.IsNavigationPointAvailable(approach, 16f))
                continue;

            query.Transform = new Transform2D(0f, candidate);
            if (player.GetWorld2D().DirectSpaceState
                .IntersectShape(query, 1).Count > 0)
                continue;

            origin = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            GD.PushError(
                "[Cave test] No clear entrance location near this spawn. " +
                "Try another seed or starting biome.");
            return;
        }

        CaveWorld cave = new()
        {
            Name = "CaveWorld",
            DepthPixels = Mathf.Max(32f, CaveDepthPixels)
        };
        AddChild(cave);
        cave.Build(origin, chunks.TileSize,
            elevation.SampleWorldHeight(origin));

        WorldLayerController controller = new()
        {
            Name = "WorldLayers"
        };
        AddChild(controller);
        controller.Configure(world, player, cave);

        GD.Print(
            $"[Cave test] Entrance at world {origin}. " +
            "Walk into its centre, then follow the tunnel down-right.");
    }
    #endregion
}