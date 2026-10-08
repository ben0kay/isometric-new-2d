// Maintains a slowly roaming herd centre around a fixed home anchor.
// Damage alerts are events; members choose their own configured response.
using Godot;
using System.Collections.Generic;

public partial class EntityHerd : Node2D
{
    #region Configuration
    [Export] public string HerdId { get; set; } = "";
    [Export] public float RoamRadius { get; set; } = 260f;
    [Export] public float CentreSpeed { get; set; } = 8f;
    #endregion

    #region State
    public Vector2 Home { get; private set; }
    private Vector2 _destination;
    private double _wait;
    private readonly List<Entity> _members = new();
    private readonly RandomNumberGenerator _rng = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Register this layer-scoped herd and retain its original anchor.
    public override void _Ready()
    {
        AddToGroup("entity_herds");
        Home = GlobalPosition;
        _destination = Home;
        _rng.Randomize();
        WorldLayerMember.Attach(this, WorldLayer.Surface);
    }

    // =========================================================
    // Release the native random generator with the herd.
    public override void _ExitTree()
    {
        _rng.Dispose();
    }

    // =========================================================
    // Move slowly along validated ground, pausing while members defend or flee.
    public override void _PhysicsProcess(double delta)
    {
        if (_members.Count == 0) return;
        foreach (Entity member in _members)
            if (GodotObject.IsInstanceValid(member) && member.HasThreat)
                return;

        _wait -= delta;
        if (!StaggeredUpdate.DueSeconds(this, 0.25, 21)) return;

        WorldNavigation navigation = WorldNavigation.For(this);
        if (navigation == null) return;

        if (GlobalPosition.DistanceSquaredTo(_destination) <= 16f)
        {
            if (_wait > 0.0) return;
            _wait = _rng.RandfRange(6f, 12f);

            Vector2 candidate = Home +
                Vector2.FromAngle(_rng.RandfRange(0f, Mathf.Tau)) *
                Mathf.Sqrt(_rng.Randf()) * RoamRadius;

            if (navigation.CanTravelDirectly(GlobalPosition, candidate))
                _destination = candidate;
        }

        Vector2 next = GlobalPosition.MoveToward(
            _destination, Mathf.Max(0f, CentreSpeed) * 0.25f);

        if (navigation.CanTravelDirectly(GlobalPosition, next))
            GlobalPosition = next;
        else
            _destination = GlobalPosition;
    }
    #endregion

    #region Membership
    // =========================================================
    // Resolve the requested herd only within the member's current world layer.
    public static EntityHerd Find(Entity actor, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (Node node in actor.GetTree().GetNodesInGroup("entity_herds"))
            if (node is EntityHerd herd && herd.HerdId == id &&
                WorldLayerMember.Same(actor, herd))
                return herd;

        return null;
    }

    // =========================================================
    // Register one member without duplicates.
    public void Join(Entity member)
    {
        if (!_members.Contains(member)) _members.Add(member);
    }

    // =========================================================
    // Remove dead or departing members.
    public void Leave(Entity member)
    {
        _members.Remove(member);
    }

    // =========================================================
    // Notify the herd once; recipients never rebroadcast the same alert.
    public void Alert(Node2D attacker)
    {
        foreach (Entity member in _members)
            if (GodotObject.IsInstanceValid(member) &&
                !member.IsQueuedForDeletion() && member.Health.IsAlive)
                member.ReactTo(attacker);
    }
    #endregion
}