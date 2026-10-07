// Places a removable procedural cave test beside the starting area.
// The helper supplies the existing world's seed and a cave generation profile.
using Godot;

public partial class CaveLayerTest : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    [Export] public CaveGenerationSettings GenerationSettings { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Wait for surface readiness and choose a clear entrance location.
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
                "[Cave test] No clear entrance near this spawn. " +
                "Try another seed or starting biome.");
            return;
        }

        CaveGenerationSettings settings = GenerationSettings ??
            GD.Load<CaveGenerationSettings>(
                "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

        if (settings == null)
        {
            GD.PushError("[Cave test] Missing cave generation profile.");
            return;
        }

        CaveWorld cave = new() { Name = "CaveWorld" };
        AddChild(cave);
        cave.Build(
            origin, chunks.TileSize, elevation.SampleWorldHeight(origin),
            chunks.WorldSeed, settings);

        WorldLayerController controller = new() { Name = "WorldLayers" };
        AddChild(controller);
        controller.Configure(world, player, cave);

        GD.Print(
            $"[Cave test] Seed {chunks.WorldSeed}; entrance at {origin}. " +
            "Follow the descending tunnel into the generated network.");
    }
    #endregion
}