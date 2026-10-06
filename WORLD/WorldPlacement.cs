// Shares basin reservations, obstacle footprints and vegetation spacing checks.
// All placement uses logical ground positions rather than elevated artwork.
using Godot;
using System.Collections.Generic;

public static class WorldPlacement
{
    #region Basin Reservations
    // =========================================================
    // Keep generated objects out of permanent basins, including drained ones.
    public static bool IsBasinReserved(
        Node context, Vector2 point,
        Vector2 footprint, Vector2 padding)
    {
        WaterBasinWorld basins = WaterBasinWorld.Find(context);

        return basins != null &&
            basins.OverlapsWorldFootprint(point, footprint, padding);
    }
    #endregion

    #region Obstacles
    // =========================================================
    // Collect solids throughout the world hierarchy, including nested presets.
    public static List<Obstacle> CollectObstacles(Node2D objects)
    {
        List<Obstacle> result = new();

        foreach (Node node in objects.GetTree().GetNodesInGroup("world_obstacles"))
        {
            if (node is not Obstacle obstacle ||
                !GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion() ||
                !objects.IsAncestorOf(obstacle)) continue;

            result.Add(obstacle);
        }

        return result;
    }

    // =========================================================
    // Reject basin reservations before checking loaded solid obstacles.
    public static bool IsBlocked(
        Node context, Vector2 point, Vector2 footprint,
        List<Obstacle> obstacles, Vector2 padding)
    {
        if (IsBasinReserved(context, point, footprint, padding))
            return true;

        foreach (Obstacle obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion()) continue;

            Vector2 difference = point - obstacle.GlobalPosition;
            Vector2 separation =
                (footprint + obstacle.Footprint) * 0.5f + padding;

            if (Mathf.Abs(difference.X) < separation.X &&
                Mathf.Abs(difference.Y) < separation.Y)
                return true;
        }

        return false;
    }
    #endregion

    #region Spacing
    // =========================================================
    // Test elliptical spacing against already placed walkable vegetation.
    public static bool IsCrowded(
        Vector2 point, List<Vector2> placed, Vector2 spacing)
    {
        float width = Mathf.Max(1f, spacing.X);
        float depth = Mathf.Max(1f, spacing.Y);

        foreach (Vector2 existing in placed)
        {
            Vector2 difference = point - existing;

            if (difference.X * difference.X / (width * width) +
                difference.Y * difference.Y / (depth * depth) < 1f)
                return true;
        }

        return false;
    }

    // =========================================================
    // Respect both tree species' spacing across currently loaded chunks.
    public static bool NearTree(
        Vector2 point, TreeDefinition definition,
        float size, List<Obstacle> obstacles)
    {
        foreach (Obstacle obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion() ||
                obstacle is not Tree tree) continue;

            Vector2 spacing = (
                definition.Spacing * size +
                tree.Definition.Spacing * tree.SizeMultiplier) * 0.5f;

            float width = Mathf.Max(1f, spacing.X);
            float depth = Mathf.Max(1f, spacing.Y);
            Vector2 difference = point - tree.GlobalPosition;

            if (difference.X * difference.X / (width * width) +
                difference.Y * difference.Y / (depth * depth) < 1f)
                return true;
        }

        return false;
    }
    #endregion
}