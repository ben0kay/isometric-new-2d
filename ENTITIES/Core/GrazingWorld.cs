// Indexes nearby grass, reserves feeding targets and remembers consumed tufts.
// Session depletion survives chunk rebuilding; timed regrowth is not implemented.
using Godot;
using System.Collections.Generic;

public partial class GrazingWorld : Node
{
    #region State
    private const float CellSize = 256f;
    private readonly Dictionary<Vector2I, HashSet<Grass>> _cells = new();
    private readonly Dictionary<Grass, Vector2I> _locations = new();
    private readonly Dictionary<Grass, Entity> _reservations = new();
    private readonly HashSet<string> _consumed = new();
    private SceneTree _tree;
    #endregion

    #region Lifecycle
    // =========================================================
    // Index existing grass and follow streaming additions/removals.
    public override void _Ready()
    {
        _tree = GetTree();
        _tree.NodeAdded += OnAdded;
        _tree.NodeRemoved += OnRemoved;
        IndexBranch(GetParent().GetParent());
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remove tree subscriptions when this world ends.
    public override void _ExitTree()
    {
        if (_tree == null) return;
        _tree.NodeAdded -= OnAdded;
        _tree.NodeRemoved -= OnRemoved;
    }

    // =========================================================
    // Reuse one index beneath the current world's Systems node.
    public static GrazingWorld GetOrCreate(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        Node systems = world.GetNode("Systems");
        GrazingWorld existing =
            systems.GetNodeOrNull<GrazingWorld>("GrazingWorld");
        if (existing != null) return existing;

        GrazingWorld created = new() { Name = "GrazingWorld" };
        systems.AddChild(created);
        return created;
    }

    // =========================================================
    // Register initial grass without depending on its artwork ready callback.
    private void IndexBranch(Node node)
    {
        OnAdded(node);
        foreach (Node child in node.GetChildren())
            IndexBranch(child);
    }

    // =========================================================
    // Reject previously consumed tufts and index new stationary grass.
    private void OnAdded(Node node)
    {
        if (node is not Grass grass || _locations.ContainsKey(grass))
            return;

        if (_consumed.Contains(Key(grass)))
        {
            grass.QueueFree();
            return;
        }

        Vector2I cell = Cell(grass.GlobalPosition);
        if (!_cells.TryGetValue(cell, out HashSet<Grass> members))
            _cells[cell] = members = new();

        members.Add(grass);
        _locations.Add(grass, cell);
    }

    // =========================================================
    // Remove streamed grass and release its reservation.
    private void OnRemoved(Node node)
    {
        if (node is not Grass grass ||
            !_locations.Remove(grass, out Vector2I cell))
            return;

        if (_cells.TryGetValue(cell, out HashSet<Grass> members))
        {
            members.Remove(grass);
            if (members.Count == 0) _cells.Remove(cell);
        }
        _reservations.Remove(grass);
    }
    #endregion

    #region Feeding
    // =========================================================
    // Reserve nearby grass inside the wander zone with a clear approach.
    public Grass Reserve(Entity actor, Vector2 centre, float radius)
    {
        WorldNavigation navigation = WorldNavigation.For(actor);
        if (navigation == null) return null;

        List<Grass> candidates = new();
        Vector2I low = Cell(centre - Vector2.One * radius);
        Vector2I high = Cell(centre + Vector2.One * radius);

        for (int y = low.Y; y <= high.Y; y++)
        for (int x = low.X; x <= high.X; x++)
        {
            if (!_cells.TryGetValue(new Vector2I(x, y), out var members))
                continue;

            foreach (Grass grass in members)
            {
                if (!GodotObject.IsInstanceValid(grass) ||
                    grass.IsQueuedForDeletion() ||
                    !WorldLayerMember.Same(actor, grass) ||
                    centre.DistanceSquaredTo(grass.GlobalPosition) >
                        radius * radius)
                    continue;

                if (_reservations.TryGetValue(grass, out Entity holder))
                {
                    if (GodotObject.IsInstanceValid(holder) &&
                        !holder.IsQueuedForDeletion() && holder.Health.IsAlive)
                        continue;
                    _reservations.Remove(grass);
                }

                candidates.Add(grass);
            }
        }

        candidates.Sort((a, b) =>
            actor.GlobalPosition.DistanceSquaredTo(a.GlobalPosition).CompareTo(
            actor.GlobalPosition.DistanceSquaredTo(b.GlobalPosition)));

        int budget = System.Math.Min(6, candidates.Count);
        for (int i = 0; i < budget; i++)
        {
            Grass grass = candidates[i];
            if (!navigation.CanTravelDirectly(
                actor.GlobalPosition, grass.GlobalPosition))
                continue;

            _reservations[grass] = actor;
            return grass;
        }
        return null;
    }

    // =========================================================
    // Release only a reservation belonging to this creature.
    public void Release(Entity actor, Grass grass)
    {
        if (grass != null &&
            _reservations.TryGetValue(grass, out Entity holder) &&
            holder == actor)
            _reservations.Remove(grass);
    }

    // =========================================================
    // Consume once after the completed grazing timer.
    public bool Consume(Entity actor, Grass grass)
    {
        if (!GodotObject.IsInstanceValid(grass) ||
            grass.IsQueuedForDeletion() ||
            !_reservations.TryGetValue(grass, out Entity holder) ||
            holder != actor)
            return false;

        _consumed.Add(Key(grass));
        _reservations.Remove(grass);
        grass.QueueFree();
        return true;
    }

    // =========================================================
    // Use the spawner's stable tuft name within its world layer.
    private static string Key(Grass grass)
    {
        return $"{WorldLayerMember.For(grass)}:{grass.Name}";
    }

    // =========================================================
    // Convert world coordinates into a small local lookup cell.
    private static Vector2I Cell(Vector2 point)
    {
        return new Vector2I(
            Mathf.FloorToInt(point.X / CellSize),
            Mathf.FloorToInt(point.Y / CellSize));
    }
    #endregion
}