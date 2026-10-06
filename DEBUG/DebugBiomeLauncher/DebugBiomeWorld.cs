// Configures the inherited debug world before its generation systems initialize.
// Normal WorldTest never uses this script.
using Godot;

public partial class DebugBiomeWorld : Node2D
{
    #region Configuration
    [ExportGroup("Spawn Search")]
    [Export] public float SearchSpacingTiles { get; set; } = 12f;
    [Export] public double SearchBudgetMs { get; set; } = 2.0;

    [ExportGroup("Starting Objects")]
    [Export] public string[] SpawnCompanions { get; set; } =
    {
        "WorldObjects/LandingSite",
        "WorldObjects/IronDepositA",
        "WorldObjects/IronDepositB",
        "WorldObjects/IronDepositC"
    };
    #endregion

    #region State
    public string RequestedBiomeId { get; private set; }
    public uint RequestedSeed { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Apply debug settings before child Ready callbacks create terrain samplers.
    public override void _EnterTree()
    {
        if (!DebugBiomeLauncher.TryTakeRequest(out var request))
        {
            RequestedBiomeId = "basalt_flats";
            RequestedSeed = 64;
        }
        else
        {
            RequestedBiomeId = request.BiomeId;
            RequestedSeed = request.Seed;
        }

        ChunkController chunks =
            GetNode<ChunkController>("Systems/ChunkController");
        WorldGenerator generator =
            GetNode<WorldGenerator>("Systems/WorldGenerator");

        chunks.WorldSeed = RequestedSeed;
        generator.PlacementMode = BiomePlacementMode.Natural;
        generator.SandboxPlateau = false;
    }
    #endregion

    #region Placement
    // =========================================================
    // Relocate the starting equipment while preserving its player-relative offsets.
    public void PlaceStartingArea(Vector2 tile)
    {
        Player player = GetNode<Player>("WorldObjects/Player");
        Node2D ground = GetNode<Node2D>("GroundChunks");
        ChunkController chunks =
            GetNode<ChunkController>("Systems/ChunkController");

        Vector2 destination = ground.ToGlobal(
            IsoGrid.TileToWorld(tile, chunks.TileSize));
        Vector2 offset = destination - player.GlobalPosition;

        foreach (string path in SpawnCompanions)
        {
            Node2D companion = GetNodeOrNull<Node2D>(path);
            if (companion != null)
                companion.GlobalPosition += offset;
        }

        player.GlobalPosition = destination;
        player.GetNodeOrNull<TerrainVisual>("Visual")?.UpdateHeight();

        Camera2D camera = player.GetNode<Camera2D>("Camera2D");
        camera.ResetSmoothing();
        camera.ForceUpdateScroll();
    }
    #endregion

    #region Failure
    // =========================================================
    // Return to the launcher instead of starting in the wrong biome.
    public void ReturnToLauncher(string reason)
    {
        DebugBiomeLauncher.LastMessage =
            $"Seed {RequestedSeed}: {reason}\n" +
            "Generate again with a blank seed to try another world.";

        Callable.From(() =>
        {
            if (!IsInsideTree()) return;

            GetTree().ChangeSceneToFile(
                "res://DEBUG/DebugBiomeLauncher/DebugBiomeLauncher.tscn");
        }).CallDeferred();
    }
    #endregion
}