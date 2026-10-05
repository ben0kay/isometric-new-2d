// Uses a short-range physics query while left-clicking with empty hands.
// Selects one nearby plant; inventory windows and death block gathering.
using Godot;

public partial class PlayerGathering : Node
{
    #region Configuration
    [Export] public float GatheringRange { get; set; } = 64f;
    [Export] public double GatheringInterval { get; set; } = 0.35;
    #endregion

    #region State
    private Player _player;
    private PlayerEquipment _equipment;
    private InventoryHud _hud;
    private Health _health;
    private CircleShape2D _shape;
    private PhysicsShapeQueryParameters2D _query;
    private double _cooldown;
    #endregion

    #region Lifecycle
    // =========================================================
    // Cache components and a reusable plant-only query.
    public override void _Ready()
    {
        _player = GetParent().GetParent<Player>();
        _equipment = GetNode<PlayerEquipment>("../Equipment");
        _health = GetNode<Health>("../Health");
        _hud = _player.GetNode<InventoryHud>("InventoryHud");
        _shape = new CircleShape2D
        {
            Radius = Mathf.Max(8f, GatheringRange)
        };
        _query = new PhysicsShapeQueryParameters2D
        {
            Shape = _shape,
            CollisionMask = 32,
            CollideWithAreas = true,
            CollideWithBodies = false
        };
    }

    // =========================================================
    // Release native query resources when the player is removed.
    public override void _ExitTree()
    {
        _query?.Dispose();
        _shape?.Dispose();
    }
    #endregion

    #region Gathering
    // =========================================================
    // Harvest one nearby plant per interval using the selected empty tool slot.
    public override void _PhysicsProcess(double delta)
    {
        _cooldown -= delta;
        if (_cooldown > 0 || !_health.IsAlive ||
            _equipment.CurrentTool != null || _hud.BlocksWorldAttack() ||
            !Input.IsMouseButtonPressed(MouseButton.Left)) return;

        _cooldown = System.Math.Max(0.1, GatheringInterval);
        _shape.Radius = Mathf.Max(8f, GatheringRange);
        _query.Transform = new Transform2D(0f, _player.GlobalPosition);
        var hits = _player.GetWorld2D().DirectSpaceState.IntersectShape(_query, 32);

        ResourceHarvest best = null;
        float bestDistance = float.PositiveInfinity;
        Vector2 cursor = _player.GetGlobalMousePosition();

        foreach (var hit in hits)
        {
            if (hit["collider"].AsGodotObject() is not ResourceHarvest harvest ||
                harvest.IsQueuedForDeletion() ||
                harvest.GetParent().IsQueuedForDeletion()) continue;

            Node2D host = harvest.GetParent<Node2D>();
            float distance = host.GlobalPosition.DistanceSquaredTo(cursor);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = harvest;
        }

        best?.Gather(_player, GatheringRange);
    }
    #endregion
}