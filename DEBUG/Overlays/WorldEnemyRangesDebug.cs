// Toggles cached enemy range drawings with F3.
// Discovers streamed enemies periodically rather than scanning every frame.
using Godot;
using System.Collections.Generic;

public partial class WorldEnemyRangesDebug : Node, IDebugOptionProvider
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

    // =========================================================
// Register this overlay for automatic discovery by the F1 menu.
public override void _EnterTree()
{
    AddToGroup(DebugOption.Group);
}

// =========================================================
// Supply the enemy toggle and its range legend to the shared menu.
public IEnumerable<DebugOption> GetDebugOptions()
{
    yield return new DebugOption
    {
        Name = "Enemy ranges",
        Order = 20,
        Read = () => Enabled,
        Write = SetEnabled,
        Legends = new[]
        {
            new DebugLegend("Green — Detection", new Color("#65e58b")),
            new DebugLegend("Red — Attack", new Color("#ff7272")),
            new DebugLegend("Amber — Forget", new Color("#e8bd68"))
        }
    };
}

    #region Lifecycle
// =========================================================
// Use the shared F1 menu instead of a dedicated overlay hotkey.
public override void _Ready()
{
    SetProcessInput(false);
    SetEnabled(Enabled);
}

// =========================================================
// Overlay hotkeys are handled centrally by the F1 menu.
public override void _Input(InputEvent input)
{
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

    // =========================================================
// Toggle existing helpers without rebuilding their cached circles.
public void SetEnabled(bool enabled)
{
    Enabled = enabled;

    foreach (EnemyRangeDebug helper in _helpers.Values)
        if (GodotObject.IsInstanceValid(helper) &&
            !helper.IsQueuedForDeletion())
            helper.SetEnabled(enabled);

    _timer = 0;
    SetProcess(enabled);
}
    #endregion
}