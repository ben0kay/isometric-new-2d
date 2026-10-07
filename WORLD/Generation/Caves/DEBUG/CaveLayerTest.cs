// Registers two linked test holes in the existing world.
// The second surface destination is validated from generation data before streaming.
using Godot;

public partial class CaveLayerTest : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    [Export] public CaveGenerationSettings GenerationSettings { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Wait for surface startup, then register the shared two-hole cave test.
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

        CaveGenerationSettings settings = GenerationSettings ??
            GD.Load<CaveGenerationSettings>(
                "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

        if (settings == null)
        {
            GD.PushError("[Cave test] Missing generation profile.");
            return;
        }
        settings.Validate();

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
            Vector2 candidate = player.GlobalPosition +
                Vector2.FromAngle(Mathf.Tau * i / 24f) * 420f;
            Vector2 approach = candidate -
                IsoGrid.TileToWorld(new Vector2(0.9f, 0f), chunks.TileSize);

            if (!chunks.IsNavigationPointAvailable(candidate, 16f) ||
                !chunks.IsNavigationPointAvailable(approach, 16f))
                continue;

            query.Transform = new Transform2D(0f, candidate);
            if (player.GetWorld2D().DirectSpaceState.IntersectShape(query, 1).Count > 0)
                continue;

            origin = candidate;
            found = true;
            break;
        }

        if (!found)
        {
            GD.PushError("[Cave test] No clear starting hole. Try another seed.");
            return;
        }

        CaveHole second = FindSecondHole(world, chunks, elevation, settings, origin);
        if (second == null)
        {
            GD.PushError(
                "[Cave test] No suitable second surface hole for this test layout. " +
                "Try another seed or a flatter starting biome.");
            return;
        }

        CaveWorld cave = new() { Name = "CaveWorld" };
        AddChild(cave);
        cave.Build(
            origin, chunks.TileSize, elevation.SampleWorldHeight(origin),
            chunks.WorldSeed, settings, second);

        WorldLayerController controller = new() { Name = "WorldLayers" };
        AddChild(controller);
        controller.Configure(world, player, cave);

        GD.Print($"[Cave test] Hole A: {origin}");
        GD.Print(
            $"[Cave test] Hole B: {second.SurfacePosition}; " +
            $"cave tile {second.MouthTile}; anchor chamber {second.AnchorCell}.");
        GD.Print(
            "[Cave test] Both holes share one network. " +
            "Hole B's surface loads when approached underground.");
    }
    #endregion

    #region Destination Selection
    // =========================================================
    // Choose an opening beyond the upper or lower edge of the generated network.
    private static CaveHole FindSecondHole(
        Node world, ChunkController chunks, TerrainElevation elevation,
        CaveGenerationSettings settings, Vector2 origin)
    {
        Node2D ground = world.GetNode<Node2D>("GroundChunks");
        TerrainSlopeWorld slopes = TerrainSlopeWorld.Ensure(world);
        float hub = settings.EntranceTunnelLengthTiles + 8f;
        float edge = (settings.CellsEitherSide + 1) * settings.CellSpacingTiles;

        for (int sideIndex = 0; sideIndex < 2; sideIndex++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            Vector2 direction = side < 0 ? Vector2.Down : Vector2.Up;

            for (int cell = 0; cell < settings.CellsAcross; cell++)
            for (int offsetIndex = 0; offsetIndex < 3; offsetIndex++)
            {
                float offset = offsetIndex == 0 ? 0f :
                    offsetIndex == 1 ? -6f : 6f;

                Vector2 tile = new(
                    hub + cell * settings.CellSpacingTiles + offset,
                    side * edge);
                Vector2 point = origin + IsoGrid.TileToWorld(tile, chunks.TileSize);
                Vector2 outside = point -
                    IsoGrid.TileToWorld(direction * 0.9f, chunks.TileSize);

                if (!chunks.IsDestinationWithinBounds(point, 120f) ||
                    !chunks.IsDestinationWithinBounds(outside, 120f))
                    continue;

                if (WorldPlacement.IsBasinReserved(
                    world, point, new Vector2(200f, 200f), Vector2.Zero))
                    continue;

                if (!ChasmFeature.HasGroundClearance(
                    ground.ToLocal(point), chunks.TileSize, 100f) ||
                    !ChasmFeature.HasGroundClearance(
                    ground.ToLocal(outside), chunks.TileSize, 100f))
                    continue;

                if (!slopes.HasClearance(point, 100f) ||
                    !slopes.HasClearance(outside, 100f))
                    continue;

                return new CaveHole(
                    "B", tile, direction, point,
                    elevation.SampleWorldHeight(point),
                    settings.EntranceTunnelLengthTiles,
                    new Vector2I(cell, side * settings.CellsEitherSide));
            }
        }

        return null;
    }
    #endregion
}