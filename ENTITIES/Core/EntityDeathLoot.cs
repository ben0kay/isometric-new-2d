// Generates death loot once and currently delivers it as ordinary world pickups.
// GeneratedContents can later be handed to a carcass instead of ground drops.
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
    // Subscribe independently from the actor's death presentation.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Entity>();
        _actor.Health.Died += OnDeath;
    }

    // =========================================================
    // Remove the subscription when the actor leaves the world.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_actor?.Health))
            _actor.Health.Died -= OnDeath;
    }
    #endregion

    #region Loot
    // =========================================================
    // Generate once, then pass the retained contents to the current delivery mode.
    private void OnDeath()
    {
        if (_generated) return;
        _generated = true;

        LootTable table = _actor.Definition.DeathLoot;
        if (table == null) return;

        ResourceWorld resources = ResourceWorld.Find(this);
        if (resources == null)
            throw new InvalidOperationException("Entity loot requires ResourceWorld.");

        StorageDefinition capacity = new()
        {
            SlotCount = 96,
            MaximumWeightKg = float.MaxValue,
            CapacityLitres = float.MaxValue
        };

        GeneratedContents = table.Generate(
            resources.Catalog, capacity, _actor.GetInstanceId());

        DeliverGroundDrops(resources);
    }

    // =========================================================
    // Keep delivery replaceable without changing species or loot generation.
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
                GD.PushError($"Could not deliver death loot '{stack.Item.Id}'.");
        }
    }
    #endregion
}