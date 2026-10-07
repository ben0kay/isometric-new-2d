// Shares simple height-based cover rules between awareness and projectiles.
// Uses logical ground collision, so tree canopies never become cover.
using Godot;

public static class CombatCover
{
    #region Settings
    public const float DefaultHeight = 64f;
    private const int MaximumHits = 64;
    #endregion

    #region Actor Height
// =========================================================
// Include player jump height in the existing relative cover-height rules.
public static float HeightFor(Node2D actor)
{
    float bodyHeight = actor is Player player
        ? player.BodyHeight : DefaultHeight;

    if (!float.IsFinite(bodyHeight) || bodyHeight <= 0f)
        bodyHeight = DefaultHeight;

    float jumpHeight = actor is Player airborne
        ? airborne.JumpHeight : 0f;

    return bodyHeight + jumpHeight;
}
    #endregion

    #region Queries
    // =========================================================
    // Find the first blocking solid, skipping short cover and other world layers.
    public static Godot.Collections.Dictionary FindHit(
        PhysicsDirectSpaceState2D space,
        PhysicsRayQueryParameters2D query,
        Godot.Collections.Array<Rid> excluded,
        Vector2 from, Vector2 to, float height, WorldLayer layer)
    {
        excluded.Clear();
        query.Exclude = excluded;
        query.From = from;
        query.To = to;
        query.CollisionMask = 1u;
        query.CollideWithBodies = true;
        query.CollideWithAreas = false;
        query.HitFromInside = true;

        Godot.Collections.Dictionary hit = new();

        for (int attempt = 0; attempt < MaximumHits; attempt++)
        {
            hit = space.IntersectRay(query);
            if (hit.Count == 0) return hit;

            Node collider = hit["collider"].AsGodotObject() as Node;

            bool otherLayer = collider != null &&
                WorldLayerMember.For(collider) != layer;

            bool shortCover = collider is Obstacle obstacle &&
                float.IsFinite(obstacle.Height) &&
                obstacle.Height < height;

            if (!otherLayer && !shortCover) return hit;

            excluded.Add(hit["rid"].AsRid());
            query.Exclude = excluded;
        }

        // Treat unusually crowded queries conservatively rather than firing blindly.
        return hit;
    }
    #endregion
}