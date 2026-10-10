// Connects a health-bearing actor to generic group membership and alerts.
// Movement and threat behaviour subscribe to events rather than living here.
using Godot;
using System;

public partial class EntityGroupMember : Node
{
    #region Configuration
    [Export] public string GroupId { get; set; } = "";
    #endregion

    #region State
    public Node2D Actor { get; private set; }
    public EntityGroup Group { get; private set; }
    public Func<bool> ThreatActive { get; set; }

    public event Action GroupChanged;
    public event Action AreaChanged;
    public event Action<Node2D> ThreatReceived;

    private Health _health;
    public bool IsAlive => GodotObject.IsInstanceValid(Actor) &&
        Actor.IsInsideTree() && !Actor.IsQueuedForDeletion() &&
        _health?.IsAlive == true;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared health before the actor begins its own ready callback.
    public override void _Ready()
    {
        Actor = GetParent().GetParent<Node2D>();
        _health = Actor.GetNode<Health>("Systems/Health");
        _health.Hit += OnHit;
        _health.Died += OnDeath;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Leave membership and release shared health subscriptions.
    public override void _ExitTree()
    {
        LeaveGroup();
        if (GodotObject.IsInstanceValid(_health))
        {
            _health.Hit -= OnHit;
            _health.Died -= OnDeath;
        }
    }
    #endregion

    #region Membership
    // =========================================================
    // Join after the actor has bound its behaviour components and layer.
    public void JoinAssignedGroup()
    {
        EntityGroup next = EntityGroup.Find(Actor, GroupId);
        if (next == Group) return;

        LeaveGroup();
        if (next == null || !next.Join(this)) return;

        Group = next;
        GroupId = next.GroupId;
        GroupChanged?.Invoke();
    }

    // =========================================================
    // Clear membership before notifying movement and the previous group.
    public void LeaveGroup()
    {
        EntityGroup previous = Group;
        Group = null;

        if (previous == null) return;

        GroupId = "";
        GroupChanged?.Invoke();

        if (GodotObject.IsInstanceValid(previous))
            previous.Leave(this);
    }

    // =========================================================
    // Deliver an alert to the actor's chosen threat behaviour.
    public void ReceiveThreat(Node2D attacker)
    {
        ThreatReceived?.Invoke(attacker);
    }

    // =========================================================
    // Notify movement without specifying how that movement is implemented.
    public void NotifyAreaChanged()
    {
        AreaChanged?.Invoke();
    }
    #endregion

    #region Health Events
    // =========================================================
    // Deliver the local threat and optionally alert other group members.
    private void OnHit()
    {
        Node2D attacker = _health.LastDamageSource;
        ReceiveThreat(attacker);

        if (GodotObject.IsInstanceValid(Group) &&
            !Group.IsQueuedForDeletion())
            Group.Alert(attacker, this);
    }

    // =========================================================
    // Update membership immediately when this actor dies.
    private void OnDeath()
    {
        LeaveGroup();
    }
    #endregion
}