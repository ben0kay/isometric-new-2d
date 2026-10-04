// Applies periodic contact damage to nearby living players.
// A clear movement corridor prevents attacks through obstacle footprints.
using Godot;

public partial class ContactAttack : Node
{
    #region Configuration
    [Export] public int Damage { get; set; } = 15;
    [Export] public float Range { get; set; } = 34f;
    [Export] public double Cooldown { get; set; } = 0.8;
    #endregion

    #region State
    private CharacterBody2D _actor;
    private Health _health;
    private WorldNavigation _navigation;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve this enemy's body and health component.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<CharacterBody2D>();
        _health = GetNode<Health>("../Health");
    }

    // =========================================================
    // Check nearby players periodically and apply one accepted contact hit.
    public override void _PhysicsProcess(double delta)
    {
        if (!_health.IsAlive) return;
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = 0.1;
        _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
        if (_navigation == null) return;

        foreach (Node node in GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player player) continue;
            Health target = player.GetNodeOrNull<Health>("Systems/Health");
            if (target == null || !target.IsAlive) continue;
            if (_actor.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) > Range * Range)
                continue;
            if (!_navigation.CanTravelDirectly(_actor.GlobalPosition, player.GlobalPosition))
                continue;

            if (target.Damage(Damage))
                _timer = System.Math.Max(0.1, Cooldown);
            return;
        }
    }
    #endregion
}