// Shares route-planning time and A* search limits between navigation layers.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class NavigationWorkScheduler : Node
{
    #region State
    private readonly List<WorldNavigation> _services = new();
    private GlobalConfig _config;
    private int _cursor;

    public double LastWorkMs { get; private set; }
    public double PeakStepMs { get; private set; }
    public int LastSearchCount { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register early so subsequent services reuse this scheduler.
    public override void _EnterTree()
    {
        AddToGroup("navigation_work_scheduler");
    }

    // =========================================================
    // Run planning before the normal enemy physics updates.
    public override void _Ready()
    {
        _config = WorldConfig.Find(this);
        ProcessPhysicsPriority = -10;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Find the existing scheduler or create one beneath navigation.
    public static NavigationWorkScheduler Ensure(WorldNavigation context)
    {
        SceneTree tree = context.GetTree();
        var existing = tree.GetFirstNodeInGroup(
            "navigation_work_scheduler") as NavigationWorkScheduler;

        if (existing != null) return existing;

        var scheduler = new NavigationWorkScheduler
        {
            Name = "NavigationWorkScheduler"
        };

        Node parent = tree.GetFirstNodeInGroup("world_navigation")
            ?? context;
        parent.AddChild(scheduler);
        return scheduler;
    }

    // =========================================================
    // Add one navigation layer without duplicate registration.
    public void Register(WorldNavigation navigation)
    {
        if (!_services.Contains(navigation))
            _services.Add(navigation);

        SetPhysicsProcess(true);
    }

    // =========================================================
    // Remove a departing layer and stop when no services remain.
    public void Unregister(WorldNavigation navigation)
    {
        _services.Remove(navigation);
        SetPhysicsProcess(_services.Count > 0);
    }
    #endregion

    #region Work
    // =========================================================
    // Rotate services until the shared budget or available work is exhausted.
    public override void _PhysicsProcess(double delta)
    {
        foreach (WorldNavigation service in _services)
            service.BeginWorkTick();

        long started = Stopwatch.GetTimestamp();
        double budget = Math.Max(0.05, _config.NavigationBudgetMs);
        int searches = Math.Max(1, _config.NavigationSearchesPerTick);
        int idle = 0;
        LastSearchCount = 0;

        while (_services.Count > 0 &&
            idle < _services.Count && ElapsedMs(started) < budget)
        {
            _cursor %= _services.Count;
            WorldNavigation service = _services[_cursor];
            _cursor = (_cursor + 1) % _services.Count;

            if (!GodotObject.IsInstanceValid(service) ||
                !service.IsInsideTree() || !service.CanProcess())
            {
                idle++;
                continue;
            }

            long stepStarted = Stopwatch.GetTimestamp();
            bool progressed = service.Step(
                LastSearchCount < searches, out bool searched);
            double elapsed = ElapsedMs(stepStarted);

            service.RecordWork(elapsed);
            PeakStepMs = Math.Max(PeakStepMs, elapsed);
            if (searched) LastSearchCount++;

            idle = progressed ? 0 : idle + 1;
        }

        LastWorkMs = ElapsedMs(started);
    }

    // =========================================================
    // Read monotonic elapsed time without allocating a stopwatch.
    internal static double ElapsedMs(long started)
    {
        return (Stopwatch.GetTimestamp() - started) *
            1000.0 / Stopwatch.Frequency;
    }
    #endregion
}