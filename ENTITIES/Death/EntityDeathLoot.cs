// Generates entity death drops or records the configured robot wreck.
// Delivery is separate from actor movement, combat and presentation.
using Godot;
using System;

public partial class EntityDeathLoot : Node
{
    #region State
    public InventoryStorage GeneratedContents { get; private set; }

    private Entity _actor;
    private bool _generated;
    #endregion

    #region Lifecycle
    // =========================================================
    // Subscribe after the shared actor has cached its health component.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Entity>();
        _actor.Health.Died += OnDeath;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remove the subscription when the actor leaves the world.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Health))
            _actor.Health.Died -= OnDeath;
    }
    #endregion

    #region Delivery
    // =========================================================
    // Deliver the configured death result once.
    private void OnDeath()
    {
        if (_generated) return;
        _generated = true;

        try
        {
            EntityDeaths deaths = EntityDeaths.Find(this);
            if (deaths != null && !deaths.TryClaimRewards(_actor)) return;

            if (_actor.Definition.DeathDelivery ==
                EntityDeathDelivery.RobotWreck)
            {
                RecordWreck();
                return;
            }

            LootTable table = _actor.Definition.DeathLoot;
            if (table == null) return;

            ResourceWorld resources = ResourceWorld.Find(this);

            if (resources == null)
                throw new InvalidOperationException(
                    "Entity loot requires ResourceWorld.");

            StorageDefinition capacity = new()
            {
                SlotCount = 96,
                MaximumWeightKg = float.MaxValue,
                CapacityLitres = float.MaxValue
            };

            ulong seed = _actor.RandomSeed != 0
                ? _actor.RandomSeed : !string.IsNullOrEmpty(_actor.PersistentId)
                    ? EntityDeaths.IdentitySeed(_actor.PersistentId) : _actor.GetInstanceId();

            GeneratedContents = table.Generate(
                resources.Catalog, capacity, seed);

            DeliverGroundDrops(resources);
        }
        catch (Exception error)
        {
            GD.PushError(
                $"Entity '{_actor.Definition.SpeciesId}' death delivery failed: {error}");
        }
    }

    // =========================================================
    // Preserve the existing persistent robot wreck identity and delivery.
    private void RecordWreck()
    {
        string layer = WorldLayerMember.For(_actor);
        ulong identity = _actor.RandomSeed != 0
            ? _actor.RandomSeed : _actor.GetInstanceId();

        string wreckId = !string.IsNullOrEmpty(_actor.PersistentId)
            ? "dead_entity:" + _actor.PersistentId
            : $"{layer}:dead_robot:{_actor.Definition.SpeciesId}:{identity}";

        LootWorld.GetOrCreate(this).RecordRobotDeath(
            wreckId, _actor.GlobalPosition, layer);
    }

    // =========================================================
    // Deliver generated stacks as ordinary same-layer world pickups.
    private void DeliverGroundDrops(ResourceWorld resources)
    {
        for (int i = 0; i < GeneratedContents.SlotCount; i++)
        {
            InventoryStack stack = GeneratedContents.Get(i);
            if (stack.IsEmpty) continue;

            Vector2 offset = Vector2.FromAngle(i * 2.4f) * 12f;

            if (!resources.SpawnItemFor(
                _actor, stack.Item, stack.Count,
                _actor.GlobalPosition + offset))
                GD.PushError(
                    $"Could not deliver death loot '{stack.Item.Id}'.");
        }
    }
    #endregion
}
