// Toggles cached enemy range drawings with F3.
// Discovers streamed enemies periodically rather than scanning every frame.
using Godot;
using System.Collections.Generic;

public partial class WorldEnemyRangesDebug : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = false;
    [Export] public double RefreshInterval { get; set; } = 0.25;
    #endregion

    #region State
    private readonly Dictionary<Enemy, EnemyRangeDebug> _helpers = new();
    private readonly List<Enemy> _removed = new();
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep the toggle available while ordinary debug processing is disabled.
    public override void _Ready()
    {
        SetProcessInput(true);
        SetProcess(Enabled);
    }

    // =========================================================
    // Toggle every existing helper without rebuilding its circles.
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key ||
            !key.Pressed || key.Echo ||
            key.PhysicalKeycode != Key.F3)
            return;

        Enabled = !Enabled;

        foreach (EnemyRangeDebug helper in _helpers.Values)
            if (GodotObject.IsInstanceValid(helper))
                helper.SetEnabled(Enabled);

        _timer = 0;
        SetProcess(Enabled);
        GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Attach helpers to new enemies and check radius changes at a modest rate.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0) return;
        _timer = System.Math.Max(0.1, RefreshInterval);

        _removed.Clear();

        foreach (var pair in _helpers)
            if (!GodotObject.IsInstanceValid(pair.Key) ||
                pair.Key.IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(pair.Value))
                _removed.Add(pair.Key);

        foreach (Enemy enemy in _removed)
            _helpers.Remove(enemy);

        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Enemy enemy ||
                enemy.IsQueuedForDeletion() ||
                enemy.Definition == null)
                continue;

            if (!_helpers.TryGetValue(enemy, out EnemyRangeDebug helper))
            {
                helper = enemy.GetNodeOrNull<EnemyRangeDebug>(
                    "EnemyRangeDebug");

                if (helper == null)
                {
                    helper = new EnemyRangeDebug
                    {
                        Name = "EnemyRangeDebug"
                    };
                    enemy.AddChild(helper);
                }

                _helpers[enemy] = helper;
            }

            helper.Configure(enemy, Enabled);
        }
    }

    // =========================================================
    // Remove surviving helpers if the debug manager itself is removed.
    public override void _ExitTree()
    {
        foreach (EnemyRangeDebug helper in _helpers.Values)
            if (GodotObject.IsInstanceValid(helper) &&
                !helper.IsQueuedForDeletion())
                helper.QueueFree();

        _helpers.Clear();
        _removed.Clear();
    }
    #endregion
}