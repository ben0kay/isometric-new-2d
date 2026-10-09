// Prepares and validates exact destination terrain using existing streaming budgets.
// Disabled physics is checked through floor data and obstacle footprints.
using Godot;
using System.Collections.Generic;

public sealed class WorldLayerLanding
{
    #region State
    private readonly WorldLayerRuntime _runtime;
    private readonly ChunkController _surface;
    private readonly Node2D _surfaceObjects;
    public string Status { get; private set; } = "";

    // =========================================================
    // Cache the existing surface service and layer registry.
    public WorldLayerLanding(WorldLayerRuntime runtime, Node world)
    {
        _runtime = runtime;
        _surface = world.GetNode<ChunkController>("Systems/ChunkController");
        _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
    }
    #endregion

    #region Preparation
    // =========================================================
    // Resolve the destination's local apron on the shared logical coordinate plane.
    public Vector2 PositionFor(WorldLayerConnection connection, string destination)
    {
        CaveWorld lower = _runtime.GetUnderground(connection.LowerLayer);
        return destination == connection.LowerLayer
            ? lower.TileToWorld(connection.TileAt(0.25f))
            : connection.OutsidePosition(lower.TileSize);
    }

    // =========================================================
    // Start budgeted destination work; no complete world is built synchronously.
    public bool Prepare(WorldLayerConnection connection, string from)
    {
        string destination = connection.Other(from);
        Vector2 point = PositionFor(connection, destination);
        if (destination == WorldLayerId.Surface)
        {
            bool ready = _surface.PrepareDestination(point);
            Status = ready ? "checking landing" : _surface.GetMeta(
                "destination_preload_status", "preparing surface").AsString();
            return ready;
        }
        CaveWorld cave = _runtime.GetUnderground(destination);
        cave.Streaming.RequestPreload(point);
        bool complete = cave.Streaming.AreaReady(point);
        Status = complete ? "checking landing" : $"preparing {cave.Definition.DisplayName}";
        return complete;
    }

    // =========================================================
    // Validate floor and obstacle clearance around the exact reserved endpoint.
    public bool TryReady(WorldLayerConnection connection, string from, out Vector2 landing)
    {
        string destination = connection.Other(from);
        landing = PositionFor(connection, destination);
        Node2D objects = destination == WorldLayerId.Surface ? _surfaceObjects
            : _runtime.GetUnderground(destination).Objects;
        List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
        // Lower arrivals stay inside the ramp; upper arrivals can use their clear apron.
        int alternatives = destination == connection.LowerLayer ? 0 : 8;
        Vector2 centre = landing;
        for (int i = 0; i <= alternatives; i++)
        {
            Vector2 point = i == 0 ? centre : centre +
                Vector2.FromAngle(Mathf.Tau * (i - 1) / 8f) * 20f;
            if (!_runtime.IsAvailable(destination, point, 14f)) continue;
            bool blocked = false;
            foreach (Obstacle obstacle in obstacles)
            {
                if (!GodotObject.IsInstanceValid(obstacle) || obstacle.IsQueuedForDeletion()) continue;
                Vector2 difference = point - obstacle.GlobalPosition;
                Vector2 radius = obstacle.Footprint * 0.5f + Vector2.One * 18f;
                if (Mathf.Abs(difference.X) < radius.X && Mathf.Abs(difference.Y) < radius.Y)
                { blocked = true; break; }
            }
            if (blocked) continue;
            landing = point;
            Status = "destination ready";
            return true;
        }
        Status = "terrain ready; landing blocked";
        return false;
    }
    #endregion
}
