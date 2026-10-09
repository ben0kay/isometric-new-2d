// Manages layer activation for living entities and keeps existing pursuers active.
// Actors cross known layer connections rather than teleporting through terrain.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldLayerPursuit : Node
{
    #region State
    private sealed class Record
    {
        public Entity Actor;
        public WorldLayerMember Member;
        public WorldLayerConnection Portal;
        public string TargetLayer;
        public IDisposable Lease;
        public bool Simulating = true;
        public bool Colliding = true;
    }

    private WorldLayerController _layers;
    private Node2D _surfaceGround;
    private ChunkController _surfaceChunks;
    private InfiniteWorldGeneration _generation;
    private WorldLayerLanding _landing;
    private Node2D _sharedActors;
    private double _timer;

    private readonly Dictionary<Entity, Record> _records = new();
    private readonly List<Entity> _remove = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the shared pursuit service.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_pursuit");
    }

    // =========================================================
    // Create pursuit coordination; each runtime world owns its navigation.
    public static void Ensure(WorldLayerController layers, Node world)
    {
        if (Find(layers) != null) return;

        WorldLayerPursuit helper = new()
        {
            Name = "EnemyPursuit",
            _layers = layers,
            _surfaceGround = world.GetNode<Node2D>("GroundChunks"),
            _surfaceChunks = world.GetNode<ChunkController>(
                "Systems/ChunkController"),
            _generation = InfiniteWorldGeneration.Find(world),
            _landing = new WorldLayerLanding(layers.Worlds, world),
            _sharedActors = world.GetNode<Node2D>("WorldObjects")
        };
        layers.AddChild(helper);
    }

    // =========================================================
    // Find the optional service without introducing a second enemy system.
    public static WorldLayerPursuit Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_pursuit") as WorldLayerPursuit;
    }

    // =========================================================
    // Refresh the live entity set and layer presentation at ten hertz.
    public override void _Process(double delta)
    {
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = 0.1;

        TrackActors();
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
    // Capture each activated entity once, independently from surface scenery.
    private void TrackActors()
    {
        foreach (Node node in GetTree().GetNodesInGroup("entities"))
        {
            if (node is not Entity actor || _records.ContainsKey(actor) ||
                actor.IsQueuedForDeletion() || !actor.Initialized ||
                !actor.IsActivated || actor.SpawnPending ||
                actor.Health?.IsAlive != true)
                continue;

            string layer = WorldLayerMember.For(actor);
            WorldLayerMember member = WorldLayerMember.Attach(actor, layer);
            // Living actors use a neutral parent so a hidden departure root cannot hide a pursuer.
            if (actor.GetParent() != _sharedActors) actor.Reparent(_sharedActors, true);

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
        else
        {
            string targetLayer = WorldLayerMember.For(actor.Target);
            if (record.TargetLayer != targetLayer) SelectPortal(record, targetLayer);
            if (record.Portal != null) TryCross(record);
        }

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
    // Update pursuit routes for entities already tracking this player.
    public void PlayerCrossed(WorldLayerConnection connection, string destination, Player player)
    {
        TrackActors();
        foreach (Record record in _records.Values)
        {
            Entity actor = record.Actor;
            if (!GodotObject.IsInstanceValid(actor) || actor.IsQueuedForDeletion()) continue;
            if (actor.HasTarget && actor.Target == player)
            {
                actor.ResetPursuitMovement();
                ClearPortal(record);
                if (connection != null) SelectPortal(record, destination);
            }
            // Include passive wildlife immediately, rather than waiting for the next refresh.
            UpdateRecord(record);
        }
    }

    // =========================================================
    // Select the next adjacent connection, even when the target has moved two depths away.
    private void SelectPortal(Record record, string destination)
    {
        ClearPortal(record);
        record.TargetLayer = destination;
        record.Portal = _layers.Worlds.Connections.Next(record.Member.Layer,
            destination, record.Actor.GlobalPosition);
        if (record.Portal?.UpperLayer == WorldLayerId.Surface)
            record.Lease = _generation.PinArea(new Rect2(
                record.Portal.MouthTile - Vector2.One * 4f, Vector2.One * 8f));
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

        goal = record.Member.Layer == record.Portal.UpperLayer
            ? record.Portal.UpperPosition : record.Portal.OutsidePosition(
                _layers.Worlds.GetUnderground(record.Portal.LowerLayer).TileSize);
        return true;
    }

    // =========================================================
    // Cross one adjacent connection only at its mouth and after safe destination preparation.
    private void TryCross(Record record)
    {
        Entity actor = record.Actor;
        WorldLayerConnection connection = record.Portal;
        string from = record.Member.Layer;
        if (from != connection.UpperLayer && from != connection.LowerLayer)
        { ClearPortal(record); return; }
        if (from == WorldLayerMember.For(actor.Target))
        { ClearPortal(record); return; }
        Vector2 mouth = from == connection.UpperLayer ? connection.UpperPosition :
            connection.OutsidePosition(_layers.Worlds.GetUnderground(connection.LowerLayer).TileSize);
        if (actor.GlobalPosition.DistanceSquaredTo(mouth) > 20f * 20f) return;
        if (!_landing.Prepare(connection, from) ||
            !_landing.TryReady(connection, from, out Vector2 landing)) return;
        actor.CrossWorldLayer(connection.Other(from), landing);
        ClearPortal(record);
        actor.ResetPursuitMovement();
    }

    // =========================================================
    // Release the cached entrance once pursuit no longer needs it.
    private static void ClearPortal(Record record)
    {
        record.Lease?.Dispose();
        record.Lease = null;
        record.Portal = null;
        record.TargetLayer = null;
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
                record.Member.Layer != WorldLayerId.Surface ||
                record.Portal == null)
                continue;

            Vector2 from = IsoGrid.WorldToTile(
                _surfaceGround.ToLocal(record.Actor.GlobalPosition),
                _surfaceChunks.TileSize);
            Vector2 to = IsoGrid.WorldToTile(
                _surfaceGround.ToLocal(record.Portal.UpperPosition),
                _surfaceChunks.TileSize);

            if (new Rect2(from, Vector2.Zero).Expand(to)
                .Grow(_surfaceChunks.ChunkSize).Intersects(chunk))
                return true;
        }
        return false;
    }

    // =========================================================
    // Protect loaded cave floor along an active pursuer's local route.
    public bool RetainCave(CaveWorld world, Vector2I coordinate)
    {
        int size = world.Settings.ChunkSize;
        Rect2 chunk = new(
            new Vector2(coordinate.X * size, coordinate.Y * size),
            Vector2.One * size);

        foreach (Record record in _records.Values)
        {
            if (!IsPursuing(record) ||
                record.Member.Layer != world.LayerId)
                continue;

            Vector2 from = world.WorldToTile(
                record.Actor.GlobalPosition);
            Vector2 destination = record.Portal != null
                ? (record.Member.Layer == record.Portal.UpperLayer ? record.Portal.UpperPosition :
                    record.Portal.OutsidePosition(_layers.Worlds.GetUnderground(record.Portal.LowerLayer).TileSize))
                : record.Actor.Target.GlobalPosition;
            Vector2 to = world.WorldToTile(destination);

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
