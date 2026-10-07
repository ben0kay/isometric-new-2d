// Streams small biome enemy populations independently from terrain generation.
// Actors prepare hidden, activate off screen, and retain session state after retirement.
using Godot;
using System.Collections.Generic;

public partial class EnemyPopulation : Node
{
    #region Configuration
    [ExportGroup("Population")]
    [Export] public PackedScene EnemyScene { get; set; }
    [Export] public bool Enabled { get; set; } = true;
    [Export] public int MaximumLiveEnemies { get; set; } = 16;

    [ExportGroup("Streaming")]
    [Export] public int ScanRadiusChunks { get; set; } = 3;
    [Export] public int ChunksPerUpdate { get; set; } = 3;
    [Export] public double UpdateInterval { get; set; } = 0.25;
    [Export] public float RetireDistance { get; set; } = 2200f;

    [ExportGroup("Spawn Safety")]
    [Export] public float MinimumPlayerDistance { get; set; } = 700f;
    [Export] public float ScreenMarginPixels { get; set; } = 128f;
    [Export] public int PlacementAttempts { get; set; } = 4;
    [Export] public int PhysicsChecksPerUpdate { get; set; } = 4;
    #endregion

    #region Records And State
    private sealed class SpawnRecord
    {
        public Vector2I Coordinate;
        public int Slot;
        public Vector2[] Candidates;
        public EnemyDefinition Definition;
        public Vector2 Position, Home;
        public int Vitality;
        public bool Chosen, Dead;
        public Enemy Actor;
    }

    private readonly Dictionary<Vector3I, SpawnRecord> _records = new();
    private readonly List<SpawnRecord> _active = new();
    private readonly List<Player> _players = new();
    private readonly List<Vector2I> _scanOffsets = new();
    private readonly RandomNumberGenerator _rng = new();
    private readonly CircleShape2D _spawnShape = new() { Radius = 12f };
    private readonly PhysicsShapeQueryParameters2D _spawnQuery = new();

    private ChunkController _chunks;
    private WorldGenerator _generator;
    private TerrainElevation _elevation;
    private Node2D _objects, _ground;
    private double _timer;
    private int _cursor, _checksRemaining, _created;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve world services and prepare a near-to-far chunk scan.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _generator = GetNode<WorldGenerator>("../WorldGenerator");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _elevation = GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

        _spawnQuery.Shape = _spawnShape;
        _spawnQuery.CollisionMask = 15u;
        _spawnQuery.CollideWithAreas = false;
        _spawnQuery.Margin = 0f;

        int radius = System.Math.Clamp(ScanRadiusChunks, 1, 8);
        for (int y = -radius; y <= radius; y++)
        for (int x = -radius; x <= radius; x++)
            _scanOffsets.Add(new Vector2I(x, y));

        _scanOffsets.Sort((a, b) =>
        {
            int order = a.LengthSquared().CompareTo(b.LengthSquared());
            if (order != 0) return order;
            order = a.Y.CompareTo(b.Y);
            return order != 0 ? order : a.X.CompareTo(b.X);
        });

        if (EnemyScene == null) GD.PushError("EnemyPopulation requires EnemyScene.");
    }

    // =========================================================
    // Dispose reusable placement queries when the world closes.
    public override void _ExitTree()
    {
        _spawnQuery.Dispose();
        _spawnShape.Dispose();
        _rng.Dispose();
    }

    // =========================================================
    // Budget population work independently from terrain and navigation work.
    public override void _PhysicsProcess(double delta)
    {
        if (!Enabled || EnemyScene == null || !_chunks.WorldReady) return;
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = System.Math.Max(0.1, UpdateInterval);
        _checksRemaining = System.Math.Max(1, PhysicsChecksPerUpdate);
        _created = 0;

        _players.Clear();
        foreach (Node node in GetTree().GetNodesInGroup("players"))
            if (node is Player player &&
                player.GetNodeOrNull<Health>("Systems/Health")?.IsAlive == true)
                _players.Add(player);
        if (_players.Count == 0) return;

        MaintainActors();

        int work = System.Math.Clamp(ChunksPerUpdate, 1, 16);
        for (int i = 0; i < work; i++)
        {
            Player player = _players[_cursor % _players.Count];
            Vector2 tile = IsoGrid.WorldToTile(
                _ground.ToLocal(player.GlobalPosition), _chunks.TileSize);
            Vector2I centre = new(
                Mathf.FloorToInt(tile.X / _chunks.ChunkSize),
                Mathf.FloorToInt(tile.Y / _chunks.ChunkSize));
            Vector2I coordinate = centre + _scanOffsets[_cursor];
            _cursor = (_cursor + 1) % _scanOffsets.Count;
            DiscoverChunk(coordinate);
        }
    }
    #endregion

    #region Stage 1 - Discover Seeded Records
    // =========================================================
    // Discover world-anchored candidates using the chunk's biome population recipe.
    private void DiscoverChunk(Vector2I coordinate)
    {
        Vector2 firstTile = new(
            coordinate.X * _chunks.ChunkSize, coordinate.Y * _chunks.ChunkSize);
        Vector2 centreTile = firstTile + Vector2.One * (_chunks.ChunkSize * 0.5f);
        BiomeEnemies population = _generator.GetBiome(centreTile).Enemies;
        if (population == null) return;

        _rng.Seed = SeedFor(coordinate, 0);
        int count = population.GetCount(_rng);

        for (int slot = 0; slot < count; slot++)
        {
            Vector3I key = new(coordinate.X, coordinate.Y, slot);
            if (!_records.TryGetValue(key, out SpawnRecord record))
            {
                int attempts = System.Math.Clamp(PlacementAttempts, 1, 8);
                Vector2[] candidates = new Vector2[attempts];
                _rng.Seed = SeedFor(coordinate, slot + 1);

                for (int i = 0; i < attempts; i++)
                {
                    Vector2 localTile = firstTile + new Vector2(
                        _rng.RandfRange(0.5f, _chunks.ChunkSize - 0.5f),
                        _rng.RandfRange(0.5f, _chunks.ChunkSize - 0.5f));
                    candidates[i] = _ground.ToGlobal(
                        IsoGrid.TileToWorld(localTile, _chunks.TileSize));
                }

                record = new SpawnRecord
                {
                    Coordinate = coordinate, Slot = slot, Candidates = candidates
                };
                _records.Add(key, record);
            }

            if (!record.Dead && record.Actor == null &&
                _active.Count < System.Math.Max(1, MaximumLiveEnemies) && _created == 0)
                PrepareActor(record);
        }
    }

    // =========================================================
    // Produce stable seeds without relying on randomized string hash codes.
    private ulong SeedFor(Vector2I coordinate, int salt)
    {
        unchecked
        {
            ulong seed = (uint)_chunks.WorldSeed;
            seed ^= (ulong)(uint)coordinate.X * 0x9E3779B185EBCA87UL;
            seed ^= (ulong)(uint)coordinate.Y * 0xC2B2AE3D27D4EB4FUL;
            seed ^= (ulong)(uint)salt * 0x165667B19E3779F9UL;
            return seed;
        }
    }
    #endregion

    #region Stage 2 - Prepare Hidden Actors
    // =========================================================
    // Choose a valid candidate and instantiate at most one hidden actor per update.
    private void PrepareActor(SpawnRecord record)
    {
        if (!record.Chosen)
        {
            for (int i = 0; i < record.Candidates.Length; i++)
            {
                Vector2 point = record.Candidates[i];
                Vector2 tile = IsoGrid.WorldToTile(_ground.ToLocal(point), _chunks.TileSize);
                BiomeEnemies population = _generator.GetBiome(tile).Enemies;
                if (population == null) continue;

                _rng.Seed = SeedFor(record.Coordinate, 100 + record.Slot * 16 + i);
                EnemyDefinition definition = population.Pick(_rng);
                if (definition == null || !CanActivate(definition, point)) continue;

                record.Definition = definition;
                record.Position = record.Home = point;
                record.Vitality = definition.MaxVitality;
                record.Chosen = true;
                break;
            }
        }

        if (!record.Chosen || !CanActivate(record.Definition, record.Position)) return;

        Enemy actor = EnemyScene.Instantiate<Enemy>();
        actor.Definition = record.Definition;
        actor.SpawnPending = true;
        actor.SpawnHome = record.Home;
        actor.RandomSeed = SeedFor(record.Coordinate, record.Slot + 1);
        actor.Position = _objects.ToLocal(record.Position);
        actor.Visible = false;
        actor.Died += () => record.Dead = true;

        record.Actor = actor;
        _objects.AddChild(actor);
        actor.Health.RestoreState(record.Vitality);
        _active.Add(record);
        _created++;
    }
    #endregion

    #region Stage 3 - Activate Off Screen
// =========================================================
// Reject basin spawns before applying visibility and physics placement checks.
private bool CanActivate(EnemyDefinition definition, Vector2 point)
{
    if (WorldPlacement.IsBasinReserved(
            this, point, new Vector2(24f, 24f), Vector2.Zero) ||
        !_chunks.IsNavigationPointAvailable(point, 12f) ||
        NearPlayers(point, MinimumPlayerDistance) ||
        IsOnScreen(definition, point) ||
        _checksRemaining <= 0)
        return false;

    _checksRemaining--;
    _spawnQuery.Transform = new Transform2D(0f, point);
    _spawnQuery.Motion = Vector2.Zero;

    return _objects.GetWorld2D().DirectSpaceState
        .IntersectShape(_spawnQuery, 1).Count == 0;
}

    // =========================================================
    // Reject artwork overlapping the actual camera view plus a conservative margin.
    private bool IsOnScreen(EnemyDefinition definition, Vector2 point)
    {
        Viewport viewport = GetViewport();
        if (viewport.GetCamera2D() == null) return true;

        float height = _elevation?.SampleWorldHeight(point) ?? 0f;
        Vector2 origin = point + Vector2.Up * height;
        Rect2 bounds = new(
            definition.SpawnVisualBounds.Position * definition.VisualScale,
            definition.SpawnVisualBounds.Size * definition.VisualScale);
        Transform2D canvas = _objects.GetCanvasTransform();

        Rect2 screen = new(canvas * (origin + bounds.Position), Vector2.Zero);
        screen = screen.Expand(canvas * (origin + new Vector2(bounds.End.X, bounds.Position.Y)));
        screen = screen.Expand(canvas * (origin + bounds.End));
        screen = screen.Expand(canvas * (origin + new Vector2(bounds.Position.X, bounds.End.Y)));

        return screen.Intersects(viewport.GetVisibleRect().Grow(
            Mathf.Max(32f, ScreenMarginPixels)));
    }

    // =========================================================
    // Test safety distances against every living player in this world.
    private bool NearPlayers(Vector2 point, float distance)
    {
        float squared = distance * distance;
        foreach (Player player in _players)
            if (point.DistanceSquaredTo(player.GlobalPosition) <= squared) return true;
        return false;
    }
    #endregion

    #region Stage 4 - Preserve And Retire
// =========================================================
// Maintain surface population ownership without retiring transferred cave actors.
private void MaintainActors()
{
    for (int i = _active.Count - 1; i >= 0; i--)
    {
        SpawnRecord record = _active[i];
        Enemy actor = record.Actor;

        if (!GodotObject.IsInstanceValid(actor) || actor.IsQueuedForDeletion())
        {
            record.Actor = null;
            _active.RemoveAt(i);
            continue;
        }

        if (WorldLayerMember.For(actor) != WorldLayer.Surface)
            continue;

        Vector2 point = actor.GlobalPosition;
        bool visible = IsOnScreen(record.Definition, point);
        bool retire = !actor.HasTarget && !visible &&
            (!NearPlayers(point, RetireDistance) ||
             !_chunks.IsNavigationPointAvailable(point));

        if (retire)
        {
            record.Position = point;
            record.Home = actor.Home;
            record.Vitality = actor.Health.Current;
            actor.CollisionLayer = 0;
            actor.Hide();
            actor.SetPhysicsProcess(false);
            actor.QueueFree();
            record.Actor = null;
            _active.RemoveAt(i);
            continue;
        }

        if (!actor.IsActivated && actor.Initialized &&
            CanActivate(record.Definition, point))
            actor.Activate();
    }
}
    #endregion
}