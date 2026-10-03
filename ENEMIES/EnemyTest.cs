// Spawns one test enemy after the world finishes its initial chunk loading.
using Godot;

public partial class EnemyTest : Node
{
    #region Configuration
    [Export] public Vector2 SpawnOffset { get; set; } = new(-180, -80);
    #endregion

    #region References
    private ChunkController _chunks;
    private Node2D _objects;
    private readonly PackedScene _enemyScene = GD.Load<PackedScene>("res://ENEMIES/Enemy.tscn");
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the world systems used by the sandbox test.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _objects = GetNode<Node2D>("../../WorldObjects");
    }

    // =========================================================
    // Spawn once when chunk initialization has completed.
    public override void _Process(double delta)
    {
        if (!_chunks.IsProcessing()) return;
        Player player = GetTree().GetFirstNodeInGroup("players") as Player;
        if (player == null) return;

        Enemy enemy = _enemyScene.Instantiate<Enemy>();
        enemy.Position = _objects.ToLocal(player.GlobalPosition + SpawnOffset);
        _objects.AddChild(enemy);
        SetProcess(false);
    }
    #endregion
}