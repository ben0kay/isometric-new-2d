// Provides finite extraction for rocks/trees and one-shot gathering for plants.
// State belongs to the instance; shared definitions contain only configuration.
using Godot;
using System;
using System.Collections.Generic;

public partial class ResourceHarvest : Area2D
{
    #region Configuration
    public WorldObjectDefinition Definition { get; set; }
    public bool GatherByHand { get; set; }
    #endregion

    #region State
    private Node2D _host;
    private ResourceWorld _resources;
    private ResourceChanges _changes;
    private float _work;
    private bool _depleted;
    private bool _configured;
    private HarvestDropPlan _plan;
    private IReadOnlyList<HarvestDropPlan.Reward> _prepared;
    public int RequiredStrength => Definition.RequiredMiningStrength;
    #endregion

    #region Attachment
    // =========================================================
    // Attach one reusable helper without adding per-frame work to resource objects.
    public static void Attach(Node2D host, WorldObjectDefinition definition,
        bool gatherByHand = false)
    {
        host.AddChild(new ResourceHarvest
        {
            Name = "Harvest",
            Definition = definition,
            GatherByHand = gatherByHand
        });
    }

    // =========================================================
    // Configure plant query areas; mining continues using the host's solid collision.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        CollisionLayer = 0;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = false;
        try
        {
            _host = GetParent<Node2D>();
            _resources = ResourceWorld.Find(this);
            _changes = ResourceChanges.Ensure(_host);
            string kind = _host is Tree ? "tree" : _host is Rock ? "rock" : "plant";
            _changes.BindScene(_host, Definition, kind);
            ResourceChangeData saved = _changes.Get(_host);
            if (saved?.Depleted == true)
            {
                _depleted = true;
                _host.QueueFree();
                return;
            }
            if (!float.IsFinite(Definition.HarvestWork) || Definition.HarvestWork <= 0f ||
                Definition.RequiredMiningStrength < 1 || !GodotObject.IsInstanceValid(_resources))
                throw new InvalidOperationException("Invalid harvest requirements or missing ResourceWorld.");

            _plan = _resources.PrepareHarvest(Definition.HarvestDrops);
            _work = saved == null ? 0f : Mathf.Min(Definition.HarvestWork, saved.Work);
            if (GatherByHand)
            {
                AddChild(new CollisionShape2D { Shape = new CircleShape2D { Radius = 8f } });
                CollisionLayer = 32u;
                Monitorable = true;
            }
            _configured = true;
        }
        catch (Exception error)
        {
            GD.PushError($"[Harvest] {Definition?.ResourcePath}: {error.Message}");
        }
    }
    #endregion

    #region Harvesting
    // =========================================================
    // Accumulate work only when the tool meets this resource's strength requirement.
    public bool Mine(int strength, float power)
    {
        if (!_configured || GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            strength < RequiredStrength || power <= 0f || !float.IsFinite(power)) return false;

        float required = Mathf.Max(0.1f, Definition.HarvestWork);
        _work = Mathf.Min(required, _work + power);
        _changes.Record(_host, _work);
        return _work < required || Finish();
    }

    // =========================================================
    // Require empty hands and close proximity; the profile controls all plant rewards.
    public bool Gather(Player player, float range)
    {
        if (!_configured || player == null || !float.IsFinite(range) || range < 0f ||
            !GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            !WorldLayerMember.Same(player, _host) ||
            player.GetNode<PlayerEquipment>("Systems/Equipment").CurrentTool != null ||
            player.GlobalPosition.DistanceSquaredTo(_host.GlobalPosition) >
                range * range) return false;
        return Finish();
    }

    // =========================================================
    // Keep a prepared batch across retries, accept it fully, then persist depletion once.
    private bool Finish()
    {
        if (!_configured || _depleted || !GodotObject.IsInstanceValid(_resources)) return false;
        if (_prepared == null)
        {
            Node world = WorldConfig.Find(_host).GetParent();
            uint seed = world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed;
            _prepared = _plan.Roll(HarvestDropPlan.Seed(
                seed, _changes.IdentityFor(_host), Definition.ResourcePath));
        }
        if (!_resources.SpawnHarvest(_prepared, _host.GlobalPosition, _host)) return false;

        _depleted = true;
        _changes.Record(_host, depleted: true);
        _host.QueueFree();
        return true;
    }
    #endregion
}
