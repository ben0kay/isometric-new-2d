// Starts the shared seeded cave network before surface content generation.
// Remains removable from world_test without adding another whole-world scene.
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
    private Node _world;
    private ChunkController _chunks;
    private IEnumerator<int> _work;
    #endregion

    #region Lifecycle
    // =========================================================
    // Reserve startup planning before the surface builder's first frame.
    public override void _Ready()
    {
        _world = GetParent();
        _chunks = _world.GetNode<ChunkController>("Systems/ChunkController");

        bool enabled = Enabled && WorldConfig.Find(_world).GenerateCaves;
        _chunks.CavePlanReady = !enabled;
        SetProcess(enabled);

        if (enabled)
            _world.GetNode<Label>("HUD/ChunkInfo").Text =
                "Planning seeded cave entrances...";
    }

    // =========================================================
    // Advance entrance planning under the existing small sampling budget.
    public override void _Process(double delta)
    {
        if (!Enabled)
        {
            FinishPlanning();
            return;
        }

        WorldGenerator generator =
            _world.GetNode<WorldGenerator>("Systems/WorldGenerator");
        if (!generator.IsNodeReady()) return;

        try
        {
            _work ??= CreateNetwork().GetEnumerator();
            long started = Stopwatch.GetTimestamp();
            double budget = double.IsFinite(SamplingBudgetMs)
                ? Math.Max(0.05, SamplingBudgetMs) : 1.0;

            while (ElapsedMs(started) < budget)
            {
                if (_work.MoveNext()) continue;
                FinishPlanning();
                break;
            }
        }
        catch (Exception error)
        {
            FinishPlanning();
            GD.PushError($"[Caves] Startup planning failed: {error}");
        }
    }

    // =========================================================
    // Release unfinished work and the startup gate when removed.
    public override void _ExitTree()
    {
        _work?.Dispose();
        _work = null;

        if (GodotObject.IsInstanceValid(_chunks))
            _chunks.CavePlanReady = true;
    }

    // =========================================================
    // Release normal surface generation after reservations are registered.
    private void FinishPlanning()
    {
        _work?.Dispose();
        _work = null;
        _chunks.CavePlanReady = true;
        SetProcess(false);
    }

    // =========================================================
    // Measure work without allocating a stopwatch each frame.
    private static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion

    #region Construction
    // =========================================================
    // Build shared metadata first, then let both layers stream normally.
    private IEnumerable<int> CreateNetwork()
    {
        CaveGenerationSettings settings = GenerationSettings ??
            GD.Load<CaveGenerationSettings>(
                "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

        if (settings == null)
            throw new InvalidOperationException("Missing cave generation profile.");

        CaveEntrancePlanner plan = new(_world, _chunks, settings);
        foreach (int step in plan.Prepare())
            yield return step;

        if (plan.Holes.Count == 0)
        {
            GD.PushWarning(
                "[Caves] No suitable entrances for this seed and profiles.");
            yield break;
        }

        CaveWorld cave = new() { Name = "CaveWorld" };
        AddChild(cave);
        cave.Build(
            plan.Origin, _chunks.TileSize, plan.RimHeight,
            _chunks.WorldSeed, plan.Settings, plan.Holes);

        Player player = _world.GetNode<Player>("WorldObjects/Player");
        WorldLayerController controller = new() { Name = "WorldLayers" };
        AddChild(controller);
        controller.Configure(_world, player, cave);

        CaveHole nearest = cave.NearestSurfaceHole(player.GlobalPosition);
        GD.Print(
            $"[Caves] Planned {plan.Holes.Count} entrances. " +
            $"Nearest: {nearest.Id} at {nearest.SurfacePosition}.");
    }
    #endregion
}