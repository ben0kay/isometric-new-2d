// Displays a dropped stack and collects only while a living player is nearby.
// Capacity failures leave items in the world; retries run only inside pickup range.
using Godot;
using System.Collections.Generic;

public partial class WorldPickup : Area2D
{
    #region Configuration
    public ItemDefinition Item { get; set; }
    public int Count { get; set; }
    public float PickupRadius { get; set; } = 48f;
    public double PickupDelay { get; set; } = 0.3;
    #endregion

    #region State
    private readonly HashSet<Player> _players = new();
    private double _delay, _retry;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build a player-only trigger and terrain-adjusted item artwork.
    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 2;
        Monitoring = true;
        Monitorable = false;
        _delay = PickupDelay;

        AddChild(new CollisionShape2D
        {
            Shape = new CircleShape2D { Radius = PickupRadius }
        });

        Vector2 size = Item.Icon.GetSize();
        TerrainVisual.Attach(this, new Rect2(Vector2.Zero, size),
            new Vector2(-12, -24), new Vector2(24f / size.X, 24f / size.Y),
            false, null, Item.Icon);

        BodyEntered += OnBodyEntered;
        BodyExited += OnBodyExited;
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Begin collection checks only when a player enters the trigger.
    private void OnBodyEntered(Node2D body)
    {
        if (body is not Player player) return;
        _players.Add(player);
        _retry = 0;
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Stop processing empty triggers.
    private void OnBodyExited(Node2D body)
    {
        if (body is Player player) _players.Remove(player);
        if (_players.Count == 0) SetPhysicsProcess(false);
    }
    #endregion

    #region Collection
    // =========================================================
    // Collect unit-by-unit up to a small budget, preserving any rejected remainder.
    public override void _PhysicsProcess(double delta)
    {
        _delay -= delta;
        if (_delay > 0) return;
        _retry -= delta;
        if (_retry > 0) return;
        _retry = 0.5;

        foreach (Player player in _players)
        {
            if (!GodotObject.IsInstanceValid(player) ||
                player.IsQueuedForDeletion()) continue;
            if (!player.GetNode<Health>("Systems/Health").IsAlive) continue;

            PlayerInventory inventory = player.GetNode<PlayerInventory>(
                "Systems/Inventory");
            int budget = 16;
            while (Count > 0 && budget-- > 0)
            {
                if (!inventory.TryCollect(Item, 1)) break;
                Count--;
            }

            if (Count > 0) continue;
            SetPhysicsProcess(false);
            QueueFree();
            return;
        }
    }
    #endregion
}