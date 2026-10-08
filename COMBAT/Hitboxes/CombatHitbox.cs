// Attaches a combat silhouette to terrain-adjusted actor artwork.
// Separate collision layers keep projectile targeting independent from movement.
using Godot;
using System;

public partial class CombatHitbox : Area2D
{
    #region Layers
    public const uint PlayerLayer = 1u << 4;
    public const uint EnemyLayer = 1u << 5;
    #endregion

    #region State
    private Node2D _actor;
    private TerrainVisual _visual;
    private Health _health;
    private CombatHitboxDefinition _definition;
    private uint _teamLayer;

    public bool CanReceiveProjectile =>
        GodotObject.IsInstanceValid(_actor) &&
        !_actor.IsQueuedForDeletion() &&
        _actor.IsVisibleInTree() &&
        _health?.IsAlive == true &&
        (_actor is not Enemy enemy ||
            (enemy.Initialized && enemy.IsActivated && !enemy.SpawnPending));

    public Vector2 AimWorldPosition => ToGlobal(_definition.AimPoint);
    #endregion

    #region Creation
// =========================================================
// Attach the correct silhouette to player, robot or configurable wildlife artwork.
public static void Attach(Node2D actor, TerrainVisual visual)
{
    CombatHitboxDefinition definition;
    uint layer;

    if (actor is Player)
    {
        definition = GD.Load<CombatHitboxDefinition>(
            "res://PLAYER/PlayerHitbox.tres");
        layer = PlayerLayer;
    }
    else if (actor is Entity entity)
    {
        definition = entity.Definition.Hitbox;
        layer = EnemyLayer;
    }
    else if (actor is Enemy)
    {
        definition = GD.Load<CombatHitboxDefinition>(
            "res://ENTITIES/Species/Robots/RobotHitbox.tres");
        layer = EnemyLayer;
    }
    else return;

    if (definition == null)
        throw new InvalidOperationException("Actor hitbox definition is missing.");

    definition.Validate();

    visual.GetNode<Node2D>("Artwork").AddChild(new CombatHitbox
    {
        Name = "CombatHitbox",
        _actor = actor,
        _visual = visual,
        _health = actor.GetNode<Health>("Systems/Health"),
        _definition = definition,
        _teamLayer = layer
    });
}
    // =========================================================
    // Find an actor's hitbox without searching the whole scene.
    public static CombatHitbox Find(Node2D actor)
    {
        return actor.GetNodeOrNull<CombatHitbox>(
            "Visual/Artwork/CombatHitbox");
    }
    #endregion

    #region Lifecycle
    // =========================================================
    // Create a unique shape and update after actor movement, before projectiles.
    public override void _Ready()
    {
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = true;
        ProcessPhysicsPriority = 10;

        AddChild(new CollisionShape2D
        {
            Name = "BodyShape",
            Shape = new ConvexPolygonShape2D
            {
                Points = _definition.Points
            }
        });

        _health.Changed += OnHealthChanged;
        Refresh();
    }

    // =========================================================
    // Disconnect signals when the owning actor is removed.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_health))
            _health.Changed -= OnHealthChanged;
    }

    // =========================================================
    // Synchronize cached artwork height and activation state before shot queries.
    public override void _PhysicsProcess(double delta)
    {
        Refresh();
    }

    // =========================================================
    // Disable dead actors immediately; respawn activation is checked next tick.
    private void OnHealthChanged(int current, int maximum)
    {
        if (current <= 0) CollisionLayer = 0;
    }
    #endregion

    #region Combat
    // =========================================================
    // Follow the visible actor without moving its ground collision.
    public void Refresh()
    {
        if (!GodotObject.IsInstanceValid(_visual)) return;

        _visual.UpdateHeight();
        uint layer = CanReceiveProjectile ? _teamLayer : 0u;
        if (CollisionLayer != layer) CollisionLayer = layer;
    }

// =========================================================
// Forward damage and its actual source through shared health.
public void ReceiveDamage(
    int amount, DamageType type, Node2D source = null)
{
    if (CanReceiveProjectile)
        _health.Damage(amount, type, source);
}

    // =========================================================
// Prevent a projectile from damaging an actor in another world layer.
public bool MatchesLayer(WorldLayer layer)
{
    return GodotObject.IsInstanceValid(_actor) &&
        WorldLayerMember.For(_actor) == layer;
}
    #endregion
}