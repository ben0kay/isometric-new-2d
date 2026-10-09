// Owns robot target selection, cached target health and cover-aware sight.
// The actor schedules checks; this helper has no processing callbacks.
using Godot;
using System;

public sealed class EnemyTargeting : IDisposable
{
    #region State
    public Player Target { get; private set; }
    public bool HasSight { get; private set; }

    public bool HasTarget =>
        GodotObject.IsInstanceValid(Target) &&
        Target.IsInsideTree() &&
        !Target.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(_targetHealth) &&
        _targetHealth.IsAlive;

    private readonly Enemy _actor;
    private Health _targetHealth;
    private readonly PhysicsRayQueryParameters2D _query = new();
    private readonly Godot.Collections.Array<Rid> _excluded = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Prepare reusable sight queries once for this actor.
    public EnemyTargeting(Enemy actor)
    {
        _actor = actor;
        _query.CollisionMask = 1u;
        _query.CollideWithAreas = false;
        _query.HitFromInside = true;
    }

    // =========================================================
    // Release native query resources when the actor leaves the world.
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
    // Resolve health only during acquisition; reuse it for the current target.
    private bool IsLiving(Player player)
    {
        if (!GodotObject.IsInstanceValid(player) ||
            !player.IsInsideTree() || player.IsQueuedForDeletion())
            return false;

        if (player == Target) return HasTarget;

        Health health = player.GetNodeOrNull<Health>("Systems/Health");
        return GodotObject.IsInstanceValid(health) && health.IsAlive;
    }

    // =========================================================
    // Require sight to acquire targets but retain tracked targets behind cover.
    public bool SelectTarget()
    {
        EnemyDefinition definition = _actor.Definition;
        Vector2 position = _actor.GlobalPosition;
        Player next = HasTarget ? Target : null;

        if (next != null &&
            position.DistanceSquaredTo(next.GlobalPosition) >
                definition.ForgetRange * definition.ForgetRange)
            next = null;

        float detectionSquared =
            definition.DetectionRange * definition.DetectionRange;

        float best = next != null
            ? position.DistanceSquaredTo(next.GlobalPosition) * 0.64f
            : detectionSquared;

        foreach (Node node in _actor.GetTree().GetNodesInGroup("players"))
        {
            if (node is not Player player || !IsLiving(player) ||
                !WorldLayerMember.Same(_actor, player))
                continue;

            float distance =
                position.DistanceSquaredTo(player.GlobalPosition);

            if (distance > detectionSquared || distance >= best ||
                !CanSee(player.GlobalPosition, player))
                continue;

            next = player;
            best = distance;
        }

        if (next == Target) return false;

        Target = next;
        _targetHealth = next?.GetNodeOrNull<Health>("Systems/Health");
        HasSight = false;
        return true;
    }
    #endregion

    #region Sight
    // =========================================================
    // Sample sight only when the actor's decision schedule requests it.
    public void RefreshSight()
    {
        HasSight = HasTarget && CanSee(Target.GlobalPosition, Target);
    }

    // =========================================================
    // Clear previous sight after a layer change or pursuit reset.
    public void ClearSight()
    {
        HasSight = false;
    }

    // =========================================================
    // Check same-layer sight against tall obstacles and trunk footprints.
    private bool CanSee(Vector2 point, Player player)
    {
        if (!IsLiving(player) ||
            !WorldLayerMember.Same(_actor, player))
            return false;

        return CombatCover.FindHit(
            _actor.GetWorld2D().DirectSpaceState,
            _query, _excluded,
            _actor.GlobalPosition, point,
            CombatCover.HeightFor(player),
            WorldLayerMember.For(_actor)).Count == 0;
    }
    #endregion
}