// Shares footprint and spacing checks used when populating streamed chunks.
// Operates on logical ground positions, independently of elevated artwork.
using Godot;
using System.Collections.Generic;

public static class WorldPlacement
{
    #region Obstacles
    // =========================================================
    // Collect solids throughout the world hierarchy, including nested presets.
    public static List<Obstacle> CollectObstacles(Node2D objects)
    {
        List<Obstacle> result = new();
        foreach (Node node in objects.GetTree().GetNodesInGroup("world_obstacles"))
        {
            if (node is not Obstacle obstacle ||
                obstacle.IsQueuedForDeletion() ||
                !objects.IsAncestorOf(obstacle)) continue;
            result.Add(obstacle);
        }
        return result;
    }

    // =========================================================
    // Test an axis-aligned footprint against loaded obstacles with extra separation.
    public static bool IsBlocked(
        Vector2 point, Vector2 footprint,
        List<Obstacle> obstacles, Vector2 padding)
    {
        foreach (Obstacle obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion()) continue;

            Vector2 difference = point - obstacle.GlobalPosition;
            Vector2 separation = (footprint + obstacle.Footprint) * 0.5f + padding;
            if (Mathf.Abs(difference.X) < separation.X &&
                Mathf.Abs(difference.Y) < separation.Y) return true;
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
            Vector2 d = point - existing;
            if (d.X * d.X / (width * width)
                + d.Y * d.Y / (depth * depth) < 1f) return true;
        }
        return false;
    }

    // =========================================================
    // Respect both tree species' spacing across the currently loaded chunks.
    public static bool NearTree(
        Vector2 point, TreeDefinition definition,
        float size, List<Obstacle> obstacles)
    {
        foreach (Obstacle obstacle in obstacles)
        {
            if (obstacle is not Tree tree ||
                !GodotObject.IsInstanceValid(tree) ||
                tree.IsQueuedForDeletion()) continue;

            Vector2 spacing = (definition.Spacing * size
                + tree.Definition.Spacing * tree.SizeMultiplier) * 0.5f;
            float width = Mathf.Max(1f, spacing.X);
            float depth = Mathf.Max(1f, spacing.Y);
            Vector2 d = point - tree.GlobalPosition;
            if (d.X * d.X / (width * width)
                + d.Y * d.Y / (depth * depth) < 1f) return true;
        }
        return false;
    }
    #endregion
}
