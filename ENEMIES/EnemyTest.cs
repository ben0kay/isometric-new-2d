// Maintains one sandbox enemy and replaces it after death.
// Spawn candidates must belong to loaded terrain and have a clear footprint.
using Godot;

public partial class EnemyTest : Node
{
    #region Configuration
    [Export] public Vector2 SpawnOffset { get; set; } = new(-180, -80);
    [Export] public double RespawnDelay { get; set; } = 2.0;
    #endregion

    #region State
    private ChunkController _chunks;
    private WorldNavigation _navigation;
    private Node2D _objects;
    private Enemy _enemy;
    private double _timer;
    private readonly PackedScene _enemyScene =
        GD.Load<PackedScene>("res://ENEMIES/Enemy.tscn");
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the world services used for safe test spawning.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _navigation = GetNode<WorldNavigation>("../WorldNavigation");
        _objects = GetNode<Node2D>("../../WorldObjects");
    }

    // =========================================================
    // Replace a defeated drone after the configured delay.
    public override void _Process(double delta)
    {
        if (!_chunks.IsProcessing()) return;
        if (GodotObject.IsInstanceValid(_enemy) && !_enemy.IsQueuedForDeletion()) return;

        _timer -= delta;
        if (_timer > 0.0) return;
        Player player = GetTree().GetFirstNodeInGroup("players") as Player;
        if (player == null || !player.GetNode<Health>("Systems/Health").IsAlive) return;

        // Physics queries run in the physics callback below.
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Try several positions around the player using physics-safe queries.
    public override void _PhysicsProcess(double delta)
    {
        SetPhysicsProcess(false);
        if (GodotObject.IsInstanceValid(_enemy) && !_enemy.IsQueuedForDeletion()) return;
        Player player = GetTree().GetFirstNodeInGroup("players") as Player;
        if (player == null || !player.GetNode<Health>("Systems/Health").IsAlive) return;

        _timer = 0.5;
        Vector2 offset = SpawnOffset.LengthSquared() > 1f ? SpawnOffset : new Vector2(-180, -80);
        for (int i = 0; i < 8; i++)
        {
            Vector2 point = player.GlobalPosition + offset.Rotated(Mathf.Tau * i / 8f);
            if (!_chunks.IsNavigationPointAvailable(point)) continue;
            if (!_navigation.CanTravelDirectly(point, point)) continue;

            _enemy = _enemyScene.Instantiate<Enemy>();
            _enemy.Position = _objects.ToLocal(point);
            _objects.AddChild(_enemy);
            _timer = System.Math.Max(0.1, RespawnDelay);
            return;
        }
    }
    #endregion
}