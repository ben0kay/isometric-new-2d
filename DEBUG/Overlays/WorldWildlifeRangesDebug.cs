// Discovers wildlife and herds for the shared F1 debug toggle.
// Disabled overlays perform no discovery; existing helpers retain their geometry.
using Godot;
using System.Collections.Generic;

public partial class WorldWildlifeRangesDebug : Node, IDebugOptionProvider
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = false;
    [Export] public double RefreshInterval { get; set; } = 0.25;
    #endregion

    #region State
    private readonly Dictionary<Node2D, WildlifeRangeDebug> _helpers = new();
    private readonly List<Node2D> _removed = new();
    private double _timer;
    #endregion

    // =========================================================
// Register this overlay for automatic discovery by the F1 menu.
public override void _EnterTree()
{
    AddToGroup(DebugOption.Group);
}

// =========================================================
// Supply wildlife controls and herd legends to the shared menu.
public IEnumerable<DebugOption> GetDebugOptions()
{
    yield return new DebugOption
    {
        Name = "Wildlife wander / herds",
        Order = 30,
        Read = () => Enabled,
        Write = SetEnabled,
        Legends = new[]
        {
            new DebugLegend(
                "Cyan — Wander area / current centre", new Color("#68dce8")),
            new DebugLegend(
                "Amber — Herd anchor / roaming area", new Color("#e8bd68")),
            new DebugLegend(
                "Amber dot — Moving herd centre", new Color("#e8bd68"))
        }
    };
}

    #region Lifecycle
    // =========================================================
    // Start discovery only when the overlay is enabled.
    public override void _Ready()
    {
        SetEnabled(Enabled);
    }

// =========================================================
// Discover wildlife and optional group roaming areas at a modest refresh rate.
public override void _Process(double delta)
{
    _timer -= delta;
    if (_timer > 0.0) return;
    _timer = System.Math.Max(0.1, RefreshInterval);

    _removed.Clear();
    foreach (var pair in _helpers)
        if (!GodotObject.IsInstanceValid(pair.Key) ||
            pair.Key.IsQueuedForDeletion() ||
            !GodotObject.IsInstanceValid(pair.Value))
            _removed.Add(pair.Key);

    foreach (Node2D owner in _removed)
        _helpers.Remove(owner);

    foreach (Node node in GetTree().GetNodesInGroup("entities"))
    {
        if (node is not Entity entity || entity.IsQueuedForDeletion() ||
            entity.Definition == null || entity.Wandering == null ||
            entity.Health?.IsAlive != true)
            continue;

        HelperFor(entity).Configure(entity, Enabled);
    }

    foreach (Node node in GetTree().GetNodesInGroup("entity_groups"))
    {
        if (node is not EntityGroup group || group.IsQueuedForDeletion() ||
            !GodotObject.IsInstanceValid(group.Roaming))
            continue;

        HelperFor(group).Configure(group, Enabled);
    }
}
    // =========================================================
    // Remove surviving helpers when the debug service leaves the world.
    public override void _ExitTree()
    {
        foreach (WildlifeRangeDebug helper in _helpers.Values)
            if (GodotObject.IsInstanceValid(helper) &&
                !helper.IsQueuedForDeletion())
                helper.QueueFree();

        _helpers.Clear();
        _removed.Clear();
    }
    #endregion

    #region Controls
    // =========================================================
    // Reuse one debug helper per creature or herd.
    private WildlifeRangeDebug HelperFor(Node2D owner)
    {
        if (_helpers.TryGetValue(owner, out WildlifeRangeDebug helper) &&
            GodotObject.IsInstanceValid(helper))
            return helper;

        helper = new WildlifeRangeDebug { Name = "WildlifeRangeDebug" };
        owner.AddChild(helper);
        _helpers[owner] = helper;
        return helper;
    }

    // =========================================================
    // Toggle presentation and suspend discovery completely while disabled.
    public void SetEnabled(bool enabled)
    {
        Enabled = enabled;
        foreach (WildlifeRangeDebug helper in _helpers.Values)
            if (GodotObject.IsInstanceValid(helper) &&
                !helper.IsQueuedForDeletion())
                helper.SetEnabled(enabled);

        _timer = 0.0;
        SetProcess(enabled);
    }
    #endregion
}