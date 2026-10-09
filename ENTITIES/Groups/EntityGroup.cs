// Owns generic group membership, formation and shared threat alerts.
// Herds and squads choose their own minimum member count and optional roaming.
using Godot;
using System.Collections.Generic;

public partial class EntityGroup : Node2D
{
    #region Configuration
    [Export] public string GroupId { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "Group";
    [Export] public bool ShareThreats { get; set; } = true;
    [Export] public int MinimumMembers { get; set; } = 2;
    #endregion

    #region State
    public GroupRoaming Roaming { get; private set; }
    public bool FormationComplete { get; private set; }
    public int MemberCount => _members.Count;

    private readonly List<EntityGroupMember> _members = new();
    private bool _closing;
    #endregion

    #region Lifecycle
    // =========================================================
    // Discover optional roaming without starting a group processing loop.
    public override void _Ready()
    {
        AddToGroup("entity_groups");
        WorldLayerMember.Attach(this, WorldLayer.Surface);
        Roaming = GetNodeOrNull<GroupRoaming>("Systems/Roaming");
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Detach surviving members when the group itself is removed.
    public override void _ExitTree()
    {
        _closing = true;
        for (int i = _members.Count - 1; i >= 0; i--)
            if (GodotObject.IsInstanceValid(_members[i]))
                _members[i].LeaveGroup();

        _members.Clear();
    }
    #endregion

    #region Membership
    // =========================================================
    // Resolve a group by ID within the member's world layer.
    public static EntityGroup Find(Node2D actor, string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;

        foreach (Node node in actor.GetTree().GetNodesInGroup("entity_groups"))
            if (node is EntityGroup group && !group._closing &&
                !group.IsQueuedForDeletion() && group.GroupId == id &&
                WorldLayerMember.Same(actor, group))
                return group;

        return null;
    }

    // =========================================================
    // Accept one member without coupling membership to a species or AI class.
    public bool Join(EntityGroupMember member)
    {
        if (_closing || IsQueuedForDeletion()) return false;
        if (!_members.Contains(member)) _members.Add(member);
        return true;
    }

    // =========================================================
    // Finish staged spawning before applying minimum-member rules.
    public void CompleteFormation()
    {
        if (FormationComplete || _closing) return;

        FormationComplete = true;
        CheckMembership();

        if (!_closing)
            Roaming?.Start();
    }

    // =========================================================
    // Remove a member and apply the completed group's survival policy.
    public void Leave(EntityGroupMember member)
    {
        if (!_members.Remove(member) || _closing || !FormationComplete)
            return;

        CheckMembership();
    }

    // =========================================================
    // Dissolve below the configured minimum and notify remaining members.
    private void CheckMembership()
    {
        if (_members.Count >= System.Math.Max(1, MinimumMembers))
            return;

        _closing = true;
        Roaming?.Stop();

        while (_members.Count > 0)
        {
            EntityGroupMember member = _members[^1];
            if (GodotObject.IsInstanceValid(member))
                member.LeaveGroup();
            else
                _members.RemoveAt(_members.Count - 1);
        }

        QueueFree();
    }
    #endregion

    #region Coordination
    // =========================================================
    // Check active threats only when a roaming step is due.
    public bool HasThreat()
    {
        foreach (EntityGroupMember member in _members)
            if (GodotObject.IsInstanceValid(member) &&
                member.IsAlive && member.ThreatActive?.Invoke() == true)
                return true;

        return false;
    }

    // =========================================================
    // Broadcast a threat without recipients rebroadcasting the alert.
    public void Alert(Node2D attacker, EntityGroupMember source)
    {
        if (!ShareThreats || _closing) return;

        foreach (EntityGroupMember member in _members)
            if (member != source && GodotObject.IsInstanceValid(member) &&
                member.IsAlive)
                member.ReceiveThreat(attacker);
    }

    // =========================================================
    // Let members reconsider movement when their shared area changes.
    public void NotifyAreaChanged()
    {
        foreach (EntityGroupMember member in _members)
            if (GodotObject.IsInstanceValid(member) && member.IsAlive)
                member.NotifyAreaChanged();
    }
    #endregion
}