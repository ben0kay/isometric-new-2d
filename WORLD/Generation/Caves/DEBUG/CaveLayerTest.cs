// Creates the existing cave test using incremental surface-data checks.
// The starting debug hole bypasses probability, but not terrain suitability.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class CaveLayerTest : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    [Export] public CaveGenerationSettings GenerationSettings { get; set; }

    [ExportGroup("Surface Sampling")]
    [Export] public double SamplingBudgetMs { get; set; } = 1.0;
    #endregion

    #region State
    private IEnumerator<int> _work;
    #endregion

    #region Lifecycle
    // =========================================================
    // Wait for startup, then advance selection under a small frame budget.
    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            _work?.Dispose();
            _work = null;
            SetProcess(false);
            return;
        }

        Node world = GetParent();
        ChunkController chunks = world.GetNode<ChunkController>(
            "Systems/ChunkController");

        if (!chunks.WorldReady) return;

        try
        {
            _work ??= CreateTest(world, chunks).GetEnumerator();
            long started = Stopwatch.GetTimestamp();
            double budget = double.IsFinite(SamplingBudgetMs)
                ? Math.Max(0.05, SamplingBudgetMs) : 1.0;

            while (ElapsedMs(started) < budget)
            {
                if (_work.MoveNext()) continue;

                _work.Dispose();
                _work = null;
                SetProcess(false);
                break;
            }
        }
        catch (Exception error)
        {
            _work?.Dispose();
            _work = null;
            SetProcess(false);
            GD.PushError($"[Cave test] Setup failed: {error}");
        }
    }

    // =========================================================
    // Release unfinished sampling when this helper is removed.
    public override void _ExitTree()
    {
        _work?.Dispose();
        _work = null;
    }

    // =========================================================
    // Measure elapsed work without allocating a stopwatch each frame.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion

    #region Test Construction
    // =========================================================
    // Find a loaded starting entrance and a generation-valid remote exit.
    private IEnumerable<int> CreateTest(
        Node world, ChunkController chunks)
    {
        Player player = world.GetNode<Player>("WorldObjects/Player");

        CaveGenerationSettings settings = GenerationSettings ??
            GD.Load<CaveGenerationSettings>(
                "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

        if (settings == null)
            throw new InvalidOperationException(
                "Missing cave generation profile.");

        settings.Validate();

        CaveSurfaceSampler sampler = new(world, chunks);
        CaveSurfaceSampler.Result first = null;
        Vector2 origin = Vector2.Zero;

        using CircleShape2D shape = new();
        using PhysicsShapeQueryParameters2D query = new()
        {
            Shape = shape,
            CollisionMask = 9,
            CollideWithAreas = false
        };

        for (int i = 0; i < 24; i++)
        {
            yield return 0;

            Vector2 point = player.GlobalPosition +
                Vector2.FromAngle(Mathf.Tau * i / 24f) * 420f;

            Vector2 outside = point -
                IsoGrid.TileToWorld(
                    Vector2.Right * 0.9f, chunks.TileSize);

            if (!chunks.IsNavigationPointAvailable(point, 16f) ||
                !chunks.IsNavigationPointAvailable(outside, 16f))
                continue;

            CaveSurfaceSampler.Result result = new();

            foreach (int step in sampler.Evaluate(
                point, Vector2.Right, true, result))
                yield return step;

            if (!result.Accepted) continue;

            shape.Radius = result.ClearRadius;
            bool blocked = false;

            foreach (Vector2 centre in new[] { point, outside })
            {
                query.Transform = new Transform2D(0f, centre);

                if (player.GetWorld2D().DirectSpaceState
                        .IntersectShape(query, 1).Count > 0)
                {
                    blocked = true;
                    break;
                }

                yield return 0;
            }

            if (blocked) continue;

            origin = point;
            first = result;
            break;
        }

        if (first == null)
        {
            GD.PushWarning(
                "[Cave test] No suitable clear starting hole. " +
                "Check the biome's cave-hole profile or try another seed.");
            yield break;
        }

        CaveHole second = null;
        float hub = settings.EntranceTunnelLengthTiles + 8f;
        float edge =
            (settings.CellsEitherSide + 1) * settings.CellSpacingTiles;

        for (int sideIndex = 0;
            sideIndex < 2 && second == null; sideIndex++)
        {
            int side = sideIndex == 0 ? -1 : 1;
            Vector2 direction =
                side < 0 ? Vector2.Down : Vector2.Up;

            for (int cell = 0;
                cell < settings.CellsAcross && second == null; cell++)
            {
                for (int offsetIndex = 0;
                    offsetIndex < 3 && second == null; offsetIndex++)
                {
                    float offset = offsetIndex == 0 ? 0f :
                        offsetIndex == 1 ? -6f : 6f;

                    Vector2 tile = new(
                        hub + cell * settings.CellSpacingTiles + offset,
                        side * edge);

                    Vector2 point = origin +
                        IsoGrid.TileToWorld(tile, chunks.TileSize);

                    CaveSurfaceSampler.Result result = new();

                    foreach (int step in sampler.Evaluate(
                        point, direction, false, result))
                        yield return step;

                    if (!result.Accepted) continue;

                    second = new CaveHole(
                        "B", tile, direction, point,
                        result.RimHeight,
                        settings.EntranceTunnelLengthTiles,
                        new Vector2I(
                            cell, side * settings.CellsEitherSide))
                    {
                        SurfaceClearRadius = result.ClearRadius
                    };
                }
            }
        }

        CaveWorld cave = new() { Name = "CaveWorld" };
        AddChild(cave);

        cave.Build(
            origin, chunks.TileSize, first.RimHeight,
            chunks.WorldSeed, settings, second);

        cave.Holes[0].SurfaceClearRadius = first.ClearRadius;

        WorldLayerController controller = new()
        {
            Name = "WorldLayers"
        };
        AddChild(controller);
        controller.Configure(world, player, cave);

        GD.Print(
            $"[Cave test] Hole A: {origin}; biome: {first.BiomeId}.");

        if (second != null)
        {
            GD.Print(
                $"[Cave test] Hole B: {second.SurfacePosition}; " +
                $"anchor chamber: {second.AnchorCell}.");
        }
        else
        {
            GD.PushWarning(
                "[Cave test] No suitable second hole. " +
                "The cave remains usable through A. Check chance, " +
                "height variation or try another seed.");
        }

        GD.Print(
            "[Cave test] Surface suitability used generation data only. " +
            "Full surface chunks load when a ramp is approached.");
    }
    #endregion
}