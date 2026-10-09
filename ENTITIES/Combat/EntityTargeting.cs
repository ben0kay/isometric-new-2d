// Selects health-bearing targets and checks cover-aware sight for any entity.
// Candidate groups and eligibility come from the owning behaviour.
using Godot;
using System;

public sealed class EntityTargeting : IDisposable
{
    #region State
    public Node2D Target { get; private set; }
    public bool HasSight { get; private set; }

    public bool HasTarget =>
        GodotObject.IsInstanceValid(Target) &&
        Target.IsInsideTree() &&
        !Target.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(_targetHealth) &&
        _targetHealth.IsAlive;

    private readonly EntityBody _actor;
    private readonly string[] _groups;
    private readonly Func<Node2D, bool> _eligible;
    private Health _targetHealth;
    private readonly PhysicsRayQueryParameters2D _query = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared actor services without assuming a species or target type.
    public EntityTargeting(
        EntityBody actor, string[] groups,
        Func<Node2D, bool> eligible = null)
    {
        _actor = actor;
        _groups = groups ?? Array.Empty<string>();
        _eligible = eligible;
        _query.CollisionMask = 1u;
        _query.CollideWithAreas = false;
        _query.HitFromInside = true;
    }

    // =========================================================
    // Release reusable native query resources when the owner exits.
    public void Dispose()
    {
        _query.Dispose();
        _excluded.Clear();
        Target = null;
        _targetHealth = null;
        HasSight = false;
    }
    #endregion

    #region Target Selection
    // =========================================================
    // Use shared actor health where available and support other health owners.
    private static Health FindHealth(Node2D candidate)
    {
        return candidate is EntityBody body
            ? body.Health
            : candidate.GetNodeOrNull<Health>("Systems/Health");
    }

    // =========================================================
    // Reuse current target health instead of looking it up repeatedly.
    private bool IsLiving(Node2D candidate)
    {
        if (!GodotObject.IsInstanceValid(candidate) ||
            !candidate.IsInsideTree() ||
            candidate.IsQueuedForDeletion())
            return false;

        if (candidate == Target) return HasTarget;

        Health health = FindHealth(candidate);
        return GodotObject.IsInstanceValid(health) && health.IsAlive;
    }

    // =========================================================
    // Allow another behaviour to assign a target, such as a received group alert.
    public bool SetTarget(Node2D candidate)
    {
        if (candidate != null &&
            (candidate == _actor || !IsLiving(candidate)))
            return false;

        if (candidate == Target) return false;

        Target = candidate;
        _targetHealth = candidate == null ? null : FindHealth(candidate);
        HasSight = false;
        return true;
    }

    // =========================================================
    // Acquire visible eligible targets while retaining remembered targets.
    public bool SelectTarget(float detectionRange, float forgetRange)
    {
        Vector2 position = _actor.GlobalPosition;
        Node2D next = HasTarget ? Target : null;

        if (next != null &&
            position.DistanceSquaredTo(next.GlobalPosition) >
                forgetRange * forgetRange)
            next = null;

        float detectionSquared = detectionRange * detectionRange;
        float best = next != null
            ? position.DistanceSquaredTo(next.GlobalPosition) * 0.64f
            : detectionSquared;

        foreach (string group in _groups)
        foreach (Node node in _actor.GetTree().GetNodesInGroup(group))
        {
            if (node is not Node2D candidate || candidate == _actor ||
                !IsLiving(candidate) ||
                !WorldLayerMember.Same(_actor, candidate) ||
                (_eligible != null && !_eligible(candidate)))
                continue;

            float distance =
                position.DistanceSquaredTo(candidate.GlobalPosition);

            if (distance > detectionSquared || distance >= best ||
                !CanSee(candidate))
                continue;

            next = candidate;
            best = distance;
        }

        return SetTarget(next);
    }
    #endregion

    #region Sight
    // =========================================================
    // Refresh sight only when the owning actor schedules a decision.
    public void RefreshSight()
    {
        HasSight = HasTarget && CanSee(Target);
    }

    // =========================================================
    // Clear stale visibility after a pursuit reset or layer transfer.
    public void ClearSight()
    {
        HasSight = false;
    }

    // =========================================================
    // Check sight using shared cover and layer services.
    private bool CanSee(Node2D candidate)
    {
        if (!IsLiving(candidate) ||
            !WorldLayerMember.Same(_actor, candidate))
            return false;

        return CombatCover.FindHit(
            _actor.GetWorld2D().DirectSpaceState,
            _query, _excluded,
            _actor.GlobalPosition, candidate.GlobalPosition,
            CombatCover.HeightFor(candidate),
            WorldLayerMember.For(_actor)).Count == 0;
    }
    #endregion
}