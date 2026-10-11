// Resolves a bounded interaction-only point query with stable overlap ordering.
using Godot;
public sealed class InteractionResolver
{
    private readonly PhysicsPointQueryParameters2D _query = new()
    { CollisionMask = InteractionTarget.Mask, CollideWithAreas = true, CollideWithBodies = false };
    // =========================================================
    // Select valid artwork under the cursor, preferring the closest visual centre.
    public InteractionTarget Resolve(Player player, Vector2 cursor)
    {
        _query.Position = cursor;
        InteractionTarget best = null;
        float distance = float.PositiveInfinity;
        foreach (var hit in player.GetWorld2D().DirectSpaceState.IntersectPoint(_query, 32))
        {
            if (hit["collider"].AsGodotObject() is not InteractionTarget target ||
                !Valid(target, player)) continue;
            float next = cursor.DistanceSquaredTo(target.Anchor);
            if (next > distance || (next == distance && best != null &&
                target.GetInstanceId() >= best.GetInstanceId())) continue;
            best = target; distance = next;
        }
        return best;
    }
    // =========================================================
    // Never retain or execute a removed, hidden, unreachable or other-layer target.
    public static bool Valid(InteractionTarget target, Player player) =>
        GodotObject.IsInstanceValid(target) && target.IsInsideTree() &&
        !target.IsQueuedForDeletion() && target.IsVisibleInTree() &&
        target.Provider != null && target.Provider.CanInteract(player);
}
