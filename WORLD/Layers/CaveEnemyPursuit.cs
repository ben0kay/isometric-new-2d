// Keeps existing pursuers active across layer changes.
// Enemies cross through the player's registered entrance, never through the ground.
using Godot;
using System;
using System.Collections.Generic;

public partial class CaveEnemyPursuit : Node
{
    #region State
    private sealed class Record
    {
        public Entity Actor;
        public WorldLayerMember Member;
        public CaveHole Portal;
        public IDisposable Lease;
        public bool Simulating = true;
        public bool Colliding = true;
    }

    private WorldLayerController _layers;
    private Node2D _surfaceGround;
    private ChunkController _surfaceChunks;
    private InfiniteWorldGeneration _generation;
    private double _timer;

    private readonly Dictionary<Entity, Record> _records = new();
    private readonly List<Entity> _remove = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the shared pursuit service.
    public override void _EnterTree()
    {
        AddToGroup("cave_enemy_pursuit");
    }

    // =========================================================
    // Create one helper and one cave instance of the shared navigation service.
    public static void Ensure(WorldLayerController layers, Node world)
    {
        if (Find(layers) != null) return;

        if (layers.Cave.GetNodeOrNull<WorldNavigation>("Navigation") == null)
            layers.Cave.AddChild(new WorldNavigation
            {
                Name = "Navigation",
                Cave = layers.Cave
            });

        CaveEnemyPursuit helper = new()
        {
            Name = "EnemyPursuit",
            _layers = layers,
            _surfaceGround = world.GetNode<Node2D>("GroundChunks"),
            _surfaceChunks = world.GetNode<ChunkController>(
                "Systems/ChunkController"),
            _generation = InfiniteWorldGeneration.Find(world)
        };
        layers.AddChild(helper);
    }

    // =========================================================
    // Find the optional service without introducing a second enemy system.
    public static CaveEnemyPursuit Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "cave_enemy_pursuit") as CaveEnemyPursuit;
    }

    // =========================================================
    // Refresh the small live enemy set and layer presentation at ten hertz.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = 0.1;

        TrackEnemies();
        _remove.Clear();

        foreach (var pair in _records)
        {
            Record record = pair.Value;
            if (!GodotObject.IsInstanceValid(record.Actor) ||
                record.Actor.IsQueuedForDeletion())
            {
                ClearPortal(record);
                _remove.Add(pair.Key);
                continue;
            }

            UpdateRecord(record);
        }

        foreach (Entity actor in _remove)
            _records.Remove(actor);
    }

    // =========================================================
    // Release entrance protection and restore surviving actors during teardown.
    public override void _ExitTree()
    {
        foreach (Record record in _records.Values)
        {
            ClearPortal(record);
            if (!GodotObject.IsInstanceValid(record.Member) ||
                !GodotObject.IsInstanceValid(record.Actor) ||
                record.Actor.IsQueuedForDeletion())
                continue;

            record.Member.SetPhysicsEnabled(true);
            record.Member.SetActive(true);
            record.Member.SetOpacity(1f);
        }
        _records.Clear();
    }
    #endregion

    #region Ownership And Presentation
    // =========================================================
    // Capture each activated enemy once, independently from surface scenery.
    private void TrackEnemies()
    {
        foreach (Node node in GetTree().GetNodesInGroup("enemies"))
        {
            if (node is not Entity actor || _records.ContainsKey(actor) ||
                actor.IsQueuedForDeletion() || !actor.Initialized ||
                !actor.IsActivated || actor.SpawnPending ||
                actor.Health?.IsAlive != true)
                continue;

            WorldLayer layer = WorldLayerMember.For(actor);
            WorldLayerMember member = WorldLayerMember.Attach(actor, layer);

            // Populate the helper's original collision and presentation snapshot.
            member.SetActive(false);
            member.SetActive(true);

            _records.Add(actor, new Record
            {
                Actor = actor,
                Member = member
            });
        }
    }

    // =========================================================
    // Keep hidden pursuers thinking, but disable their physical interaction.
    private void UpdateRecord(Record record)
    {
        Entity actor = record.Actor;
        if (actor.Health?.IsAlive != true) return;

        if (!actor.HasTarget)
            ClearPortal(record);
        else if (record.Portal != null)
            TryCross(record);

        bool visible = record.Member.Layer == _layers.Current;
        bool simulate = visible || actor.HasTarget;

        if (simulate != record.Simulating)
        {
            // Capture original collision settings before pausing, not hidden zeros.
            if (!simulate)
                record.Member.SetPhysicsEnabled(true);

            record.Member.SetActive(simulate);
            record.Simulating = simulate;
            record.Colliding = simulate;
        }

        bool collide = simulate && visible;
        if (collide != record.Colliding)
        {
            record.Member.SetPhysicsEnabled(collide);
            record.Colliding = collide;
        }

        record.Member.SetOpacity(visible ? 1f : 0f);
    }
    #endregion

    #region Entrance Pursuit
    // =========================================================
    // Remember the entrance only for enemies already tracking this player.
    public void PlayerCrossed(
        CaveHole hole, WorldLayer destination, Player player)
    {
        TrackEnemies();

        foreach (Record record in _records.Values)
        {
            Entity actor = record.Actor;
            if (!GodotObject.IsInstanceValid(actor) ||
                actor.IsQueuedForDeletion() ||
                !actor.HasTarget || actor.Target != player)
                continue;

            ClearPortal(record);
            actor.ResetPursuitMovement();

            if (hole != null && record.Member.Layer != destination)
            {
                record.Portal = hole;

                Vector2 tile = IsoGrid.WorldToTile(
                    _surfaceGround.ToLocal(hole.SurfacePosition),
                    _layers.Cave.TileSize);

                record.Lease = _generation?.PinArea(
                    new Rect2(tile - Vector2.One * 4f, Vector2.One * 8f));
            }

            UpdateRecord(record);
        }
    }

    // =========================================================
    // Supply a reachable mouth goal instead of the target's other-layer position.
    public bool TryGetGoal(Entity actor, out Vector2 goal)
    {
        goal = actor.GlobalPosition;
        if (!_records.TryGetValue(actor, out Record record) ||
            record.Portal == null || !actor.HasTarget ||
            WorldLayerMember.Same(actor, actor.Target))
            return false;

        goal = record.Member.Layer == WorldLayer.Surface
            ? record.Portal.SurfacePosition
            : record.Portal.OutsidePosition(_layers.Cave.TileSize);
        return true;
    }

    // =========================================================
    // Cross only at the mouth and only when destination ground is ready.
    private void TryCross(Record record)
    {
        Entity actor = record.Actor;
        CaveHole hole = record.Portal;
        WorldLayer destination = WorldLayerMember.For(actor.Target);

        if (record.Member.Layer == destination)
        {
            ClearPortal(record);
            return;
        }

        Vector2 mouth = record.Member.Layer == WorldLayer.Surface
            ? hole.SurfacePosition
            : hole.OutsidePosition(_layers.Cave.TileSize);

        if (actor.GlobalPosition.DistanceSquaredTo(mouth) > 20f * 20f)
            return;

        Vector2 landing;
        if (destination == WorldLayer.Cave)
        {
            landing = _layers.Cave.TileToWorld(hole.TileAt(0.25f));
            if (!_layers.Cave.Streaming.EntryReady(hole) ||
                !_layers.Cave.Streaming.IsAvailable(landing, 10f))
                return;
        }
        else
        {
            landing = hole.OutsidePosition(_layers.Cave.TileSize);
            WorldNavigation surface = GetTree().GetFirstNodeInGroup(
                "world_navigation") as WorldNavigation;

            if (!_surfaceChunks.IsNavigationPointAvailable(landing, 10f) ||
                surface == null || !surface.CanTravelDirectly(landing, landing))
                return;
        }

        actor.CrossWorldLayer(destination, landing);
        ClearPortal(record);
    }

    // =========================================================
    // Release the cached entrance once pursuit no longer needs it.
    private static void ClearPortal(Record record)
    {
        record.Lease?.Dispose();
        record.Lease = null;
        record.Portal = null;
    }
    #endregion

    #region Streaming Protection
    // =========================================================
    // Keep the already-loaded surface approach until its pursuer crosses.
    public bool RetainSurface(Vector2I coordinate)
    {
        Rect2 chunk = new(
            new Vector2(coordinate.X * _surfaceChunks.ChunkSize,
                coordinate.Y * _surfaceChunks.ChunkSize),
            Vector2.One * _surfaceChunks.ChunkSize);

        foreach (Record record in _records.Values)
        {
            if (!IsPursuing(record) ||
                record.Member.Layer != WorldLayer.Surface ||
                record.Portal == null)
                continue;

            Vector2 from = IsoGrid.WorldToTile(
                _surfaceGround.ToLocal(record.Actor.GlobalPosition),
                _surfaceChunks.TileSize);
            Vector2 to = IsoGrid.WorldToTile(
                _surfaceGround.ToLocal(record.Portal.SurfacePosition),
                _surfaceChunks.TileSize);

            if (new Rect2(from, Vector2.Zero).Expand(to)
                .Grow(_surfaceChunks.ChunkSize).Intersects(chunk))
                return true;
        }
        return false;
    }

    // =========================================================
    // Protect loaded cave floor along an active pursuer's local route.
    public bool RetainCave(Vector2I coordinate)
    {
        int size = _layers.Cave.Settings.ChunkSize;
        Rect2 chunk = new(
            new Vector2(coordinate.X * size, coordinate.Y * size),
            Vector2.One * size);

        foreach (Record record in _records.Values)
        {
            if (!IsPursuing(record) ||
                record.Member.Layer != WorldLayer.Cave)
                continue;

            Vector2 from = _layers.Cave.WorldToTile(
                record.Actor.GlobalPosition);
            Vector2 destination = record.Portal != null
                ? record.Portal.OutsidePosition(_layers.Cave.TileSize)
                : record.Actor.Target.GlobalPosition;
            Vector2 to = _layers.Cave.WorldToTile(destination);

            if (new Rect2(from, Vector2.Zero).Expand(to)
                .Grow(size).Intersects(chunk))
                return true;
        }
        return false;
    }

    // =========================================================
    // Ignore retired actors when checking temporary streaming protection.
    private static bool IsPursuing(Record record)
    {
        return GodotObject.IsInstanceValid(record.Actor) &&
            !record.Actor.IsQueuedForDeletion() &&
            record.Actor.Health?.IsAlive == true &&
            record.Actor.HasTarget;
    }
    #endregion
}