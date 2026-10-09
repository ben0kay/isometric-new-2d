// Provides finite extraction for rocks/trees and one-shot gathering for plants.
// State belongs to the instance; shared definitions contain only configuration.
using Godot;

public partial class ResourceHarvest : Area2D
{
    #region Configuration
    public WorldObjectDefinition Definition { get; set; }
    public string DefaultItemId { get; set; }
    public bool GatherByHand { get; set; }
    #endregion

    #region State
    private Node2D _host;
    private ResourceWorld _resources;
    private float _work;
    private bool _depleted;
    public int RequiredStrength => Definition.RequiredMiningStrength;
    #endregion

    #region Attachment
    // =========================================================
    // Attach one reusable helper without adding per-frame work to resource objects.
    public static void Attach(Node2D host, WorldObjectDefinition definition,
        string defaultItemId, bool gatherByHand = false)
    {
        host.AddChild(new ResourceHarvest
        {
            Name = "Harvest",
            Definition = definition,
            DefaultItemId = defaultItemId,
            GatherByHand = gatherByHand
        });
    }

    // =========================================================
    // Configure plant query areas; mining continues using the host's solid collision.
    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _resources = ResourceWorld.Find(this);
        CollisionLayer = GatherByHand ? 32u : 0u;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = GatherByHand;

        if (GatherByHand)
            AddChild(new CollisionShape2D
            {
                Shape = new CircleShape2D { Radius = 8f }
            });

        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Harvesting
    // =========================================================
    // Accumulate work only when the tool meets this resource's strength requirement.
    public bool Mine(int strength, float power)
    {
        if (GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            strength < RequiredStrength || power <= 0f) return false;

        float required = Mathf.Max(0.1f, Definition.HarvestWork);
        _work = Mathf.Min(required, _work + power);
        return _work < required || Finish();
    }

    // =========================================================
    // Require empty hands and close proximity; plants yield one fiber in this pass.
    public bool Gather(Player player, float range)
    {
        if (!GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            !WorldLayerMember.Same(player, _host) ||
            player.GetNode<PlayerEquipment>("Systems/Equipment").CurrentTool != null ||
            player.GlobalPosition.DistanceSquaredTo(_host.GlobalPosition) >
                range * range) return false;
        return Finish();
    }

// =========================================================
// Spawn primary and bonus rewards together before removing the source.
private bool Finish()
{
    if (_depleted || !GodotObject.IsInstanceValid(_resources)) return false;

    string id = string.IsNullOrWhiteSpace(Definition.HarvestItemId)
        ? DefaultItemId : Definition.HarvestItemId;
    int units = GatherByHand ? 1 : Mathf.Max(1, Definition.HarvestUnits);

    if (!_resources.SpawnHarvest(id, units,
        Definition.BonusHarvestItems, _host.GlobalPosition, _host)) return false;

    _depleted = true;
    _host.QueueFree();
    return true;
}
    #endregion
}
