// Streams a finite isometric world around the camera.
// Ground lives under GroundChunks; obstacles share WorldObjects for Y-sorting.
using Godot;
using System.Collections.Generic;

public partial class ChunkController : Node
{


#region Configuration
[ExportGroup("World")]
[Export] public Vector2 TileSize { get; set; } = new(128, 64);
[Export] public int ChunkSize { get; set; } = 16;
[Export] public int WorldChunksPerAxis { get; set; } = 8;
[Export] public uint WorldSeed { get; set; } = 64;

[ExportGroup("Streaming")]
[Export] public int ChunksLoadedPerFrame { get; set; } = 2;
[Export] public bool ShowChunkBoundaries { get; set; }

[ExportGroup("Spawn")]
[Export] public float SpawnClearRadius { get; set; } = 320f;

[ExportGroup("Test Props")]
[Export] public int CratesPerChunk { get; set; } = 2;
[Export] public VisualDefinition CrateVisual { get; set; }
#endregion

	#region State
	private sealed class LoadedChunk
	{
		public WorldChunk Ground;
		public readonly List<Obstacle> Obstacles = new();
	}

	private readonly Dictionary<Vector2I, LoadedChunk> _loaded = new();
	private readonly HashSet<Vector2I> _wanted = new();
	private readonly List<Vector2I> _pending = new();
	private readonly List<Vector2I> _remove = new();

	private Node2D _groundRoot, _objects;
	private Player _player;
	private Camera2D _camera;
	private Label _debug;
	private Vector2 _spawnPoint;
	private Vector2I _viewMin, _viewMax;
	private int _worldMin, _worldMax;
	private double _refreshTimer;
	private VegetationSpawner _vegetation;
	private GrassSpawner _grass;
private WorldGenerator _generator;
private RockSpawner _rocks;

	#endregion

	#region Lifecycle
// =========================================================
// Prepare cached artwork and shared biome-aware helpers before streaming.
public override async void _Ready()
{
    SetProcess(false);
    TileSize = new Vector2(Mathf.Max(16f, TileSize.X), Mathf.Max(8f, TileSize.Y));
    ChunkSize = Mathf.Max(1, ChunkSize);
    WorldChunksPerAxis = Mathf.Max(1, WorldChunksPerAxis);
    ChunksLoadedPerFrame = Mathf.Max(1, ChunksLoadedPerFrame);

    _groundRoot = GetNode<Node2D>("../../GroundChunks");
    _objects = GetNode<Node2D>("../../WorldObjects");
    _generator = GetNode<WorldGenerator>("../WorldGenerator");
    _player = _objects.GetNode<Player>("Player");
    _camera = _player.GetNode<Camera2D>("Camera2D");
    _debug = GetNode<Label>("../../HUD/ChunkInfo");
    _spawnPoint = _player.GlobalPosition;
    _worldMin = -(WorldChunksPerAxis / 2);
    _worldMax = _worldMin + WorldChunksPerAxis - 1;
    _debug.Text = "Loading world artwork...";

    try
    {
        await PlaceholderAtlas.EnsureReady(this);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        await VegetationAtlas.EnsureReady(this);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        await TreeAtlas.EnsureReady(this);
        if (!IsInsideTree() || IsQueuedForDeletion()) return;

        _rocks = new RockSpawner
        {
            Name = "RockSpawner", Generator = _generator
        };
        _vegetation = new VegetationSpawner
        {
            Name = "VegetationSpawner", Generator = _generator
        };
        _grass = new GrassSpawner
        {
            Name = "GrassSpawner", Generator = _generator
        };
        AddChild(_rocks);
        AddChild(_vegetation);
        AddChild(_grass);

        CreateWorldBoundary();
        _camera.ResetSmoothing();
        _camera.ForceUpdateScroll();
        RefreshWantedChunks();

        while (_pending.Count > 0) LoadNextChunk();
        UpdateDebug();
        SetProcess(true);
    }
    catch (System.Exception error)
    {
        _debug.Text = "World initialization failed — see Errors.";
        GD.PushError($"World initialization failed: {error}");
    }
}

	// =========================================================
	// Check the visible area periodically and spread later loading across frames.
	public override void _Process(double delta)
	{
		_refreshTimer -= delta;
		if (_refreshTimer <= 0.0)
		{
			_refreshTimer = 0.15;
			RefreshWantedChunks();
			UpdateDebug();
		}

		for (int i = 0; i < ChunksLoadedPerFrame && _pending.Count > 0; i++)
			LoadNextChunk();
	}
	#endregion

	#region Streaming
	// =========================================================
	// Derive chunk coverage from all four visible viewport corners.
	private void RefreshWantedChunks()
	{
		Transform2D inverse = GetViewport().GetCanvasTransform().AffineInverse();
		Vector2 size = GetViewport().GetVisibleRect().Size;
		Vector2[] corners = { Vector2.Zero, new(size.X, 0), size, new(0, size.Y) };
		Vector2 min = new(float.MaxValue, float.MaxValue);
		Vector2 max = new(float.MinValue, float.MinValue);

		foreach (Vector2 corner in corners)
		{
			Vector2 tile = IsoGrid.WorldToTile(_groundRoot.ToLocal(inverse * corner), TileSize);
			min = new Vector2(Mathf.Min(min.X, tile.X), Mathf.Min(min.Y, tile.Y));
			max = new Vector2(Mathf.Max(max.X, tile.X), Mathf.Max(max.Y, tile.Y));
		}

		// A full chunk margin covers obstacle height and upcoming movement.
		_viewMin = new Vector2I(
			Mathf.FloorToInt((min.X + 0.5f) / ChunkSize) - 1,
			Mathf.FloorToInt((min.Y + 0.5f) / ChunkSize) - 1
		);
		_viewMax = new Vector2I(
			Mathf.FloorToInt((max.X + 0.5f) / ChunkSize) + 1,
			Mathf.FloorToInt((max.Y + 0.5f) / ChunkSize) + 1
		);

		_wanted.Clear();
		_pending.Clear();
		for (int y = Mathf.Max(_worldMin, _viewMin.Y); y <= Mathf.Min(_worldMax, _viewMax.Y); y++)
		for (int x = Mathf.Max(_worldMin, _viewMin.X); x <= Mathf.Min(_worldMax, _viewMax.X); x++)
		{
			Vector2I coordinate = new(x, y);
			_wanted.Add(coordinate);
			if (!_loaded.ContainsKey(coordinate)) _pending.Add(coordinate);
		}

		Vector2 playerPosition = _groundRoot.ToLocal(_player.GlobalPosition);
		_pending.Sort((a, b) =>
			ChunkCentre(b).DistanceSquaredTo(playerPosition).CompareTo(
			ChunkCentre(a).DistanceSquaredTo(playerPosition)));

		_remove.Clear();
		foreach (Vector2I coordinate in _loaded.Keys)
		{
			if (coordinate.X < _viewMin.X - 1 || coordinate.X > _viewMax.X + 1 ||
				coordinate.Y < _viewMin.Y - 1 || coordinate.Y > _viewMax.Y + 1)
				_remove.Add(coordinate);
		}

		// Limit destruction per refresh to avoid a large unload spike.
		for (int i = 0; i < Mathf.Min(2, _remove.Count); i++)
			UnloadChunk(_remove[i]);
	}

	// =========================================================
	// Return a chunk's centre in ground-local coordinates.
	private Vector2 ChunkCentre(Vector2I coordinate)
	{
		float half = (ChunkSize - 1) * 0.5f;
		return IsoGrid.TileToWorld(
			new Vector2(coordinate.X * ChunkSize + half, coordinate.Y * ChunkSize + half),
			TileSize
		);
	}

// =========================================================
// Load terrain, biome rocks, test crates, trees, plants and grass in that order.
private void LoadNextChunk()
{
    int last = _pending.Count - 1;
    Vector2I coordinate = _pending[last];
    _pending.RemoveAt(last);

    WorldChunk ground = new()
    {
        Name = $"Chunk_{coordinate.X}_{coordinate.Y}",
        Coordinate = coordinate,
        ChunkSize = ChunkSize,
        TileSize = TileSize,
        Seed = WorldSeed,
        ShowBoundary = ShowChunkBoundaries,
        Position = IsoGrid.TileToWorld(
            new Vector2(coordinate.X * ChunkSize, coordinate.Y * ChunkSize),
            TileSize)
    };
    LoadedChunk chunk = new() { Ground = ground };
    _groundRoot.AddChild(ground);
    _loaded.Add(coordinate, chunk);

    _rocks.Populate(
        coordinate, ChunkSize, TileSize, WorldSeed,
        _groundRoot, _objects, _spawnPoint,
        SpawnClearRadius, chunk.Obstacles);
    CreateObstacles(coordinate, chunk);

    _vegetation.Populate(
        coordinate, ChunkSize, TileSize, WorldSeed,
        _groundRoot, _objects, _spawnPoint,
        SpawnClearRadius, chunk.Obstacles);
    _grass.Populate(
        coordinate, ChunkSize, TileSize, WorldSeed,
        _groundRoot, _objects, _spawnPoint);
}

// =========================================================
// Remove terrain and all artwork instances owned by one streamed chunk.
private void UnloadChunk(Vector2I coordinate)
{
	LoadedChunk chunk = _loaded[coordinate];
	_grass.RemoveChunk(coordinate);
	_vegetation.RemoveChunk(coordinate);
	foreach (Obstacle obstacle in chunk.Obstacles) obstacle.QueueFree();
	chunk.Ground.QueueFree();
	_loaded.Remove(coordinate);
}
	#endregion

	#region Generation
// =========================================================
// Generate optional test crates independently from natural biome rock selection.
private void CreateObstacles(Vector2I coordinate, LoadedChunk chunk)
{
    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, WorldSeed ^ 0xC8A7u);

    float lowX = coordinate.X * ChunkSize - 0.5f;
    float lowY = coordinate.Y * ChunkSize - 0.5f;
    Vector2 footprint = new(72, 36);

    for (int i = 0; i < Mathf.Max(0, CratesPerChunk); i++)
    {
        Vector2 tile = new(
            rng.RandfRange(lowX, lowX + ChunkSize),
            rng.RandfRange(lowY, lowY + ChunkSize));
        Vector2 localPoint = IsoGrid.TileToWorld(tile, TileSize);
        Vector2 globalPoint = _groundRoot.ToGlobal(localPoint);
        if (globalPoint.DistanceSquaredTo(_spawnPoint)
            < SpawnClearRadius * SpawnClearRadius) continue;
        if (!ChasmFeature.HasGroundClearance(localPoint, TileSize, 48f)) continue;
        if (!RockSpawner.CanPlace(_objects, globalPoint, footprint)) continue;

        Obstacle crate = new()
        {
            Name = $"Crate_{coordinate.X}_{coordinate.Y}_{i}",
            Position = _objects.ToLocal(globalPoint),
            Kind = Obstacle.ObstacleKind.Crate,
            Footprint = footprint,
            Height = 48f,
            VisualOverride = CrateVisual
        };
        _objects.AddChild(crate);
        chunk.Obstacles.Add(crate);
    }
}

	// =========================================================
	// Add four solid edges around the finite diamond-shaped map.
	private void CreateWorldBoundary()
	{
		float low = _worldMin * ChunkSize - 0.5f;
		float high = (_worldMax + 1) * ChunkSize - 0.5f;
		Vector2[] corners =
		{
			IsoGrid.TileToWorld(new(low, low), TileSize),
			IsoGrid.TileToWorld(new(high, low), TileSize),
			IsoGrid.TileToWorld(new(high, high), TileSize),
			IsoGrid.TileToWorld(new(low, high), TileSize)
		};

		StaticBody2D boundary = new() { Name = "WorldBoundary" };
		_groundRoot.AddChild(boundary);
		for (int i = 0; i < 4; i++)
		{
			boundary.AddChild(new CollisionShape2D
			{
				Name = $"Edge_{i}",
				Shape = new SegmentShape2D { A = corners[i], B = corners[(i + 1) % 4] }
			});
		}
	}
	#endregion

	#region Navigation Access
// =========================================================
// Require loaded ground inside the map with optional clearance from chasms.
public bool IsNavigationPointAvailable(Vector2 globalPoint, float clearance = 0f)
{
	if (_groundRoot == null || !IsProcessing()) return false;
	Vector2 localPoint = _groundRoot.ToLocal(globalPoint);
	Vector2 tile = IsoGrid.WorldToTile(localPoint, TileSize);
	float low = _worldMin * ChunkSize - 0.5f;
	float high = (_worldMax + 1) * ChunkSize - 0.5f;

	if (tile.X < low || tile.Y < low || tile.X >= high || tile.Y >= high) return false;
	if (!ChasmFeature.HasGroundClearance(localPoint, TileSize, clearance)) return false;
	return _loaded.ContainsKey(IsoGrid.WorldToChunk(localPoint, TileSize, ChunkSize));
}
#endregion

	#region Debug
// =========================================================
// Show the player's current biome alongside chunk streaming information.
private void UpdateDebug()
{
    Vector2 localPoint = _groundRoot.ToLocal(_player.GlobalPosition);
    Vector2 tile = IsoGrid.WorldToTile(localPoint, TileSize);
    Vector2I coordinate = IsoGrid.WorldToChunk(localPoint, TileSize, ChunkSize);
    string biome = _generator.GetBiome(tile).DisplayName;
    _debug.Text = $"{biome}  |  CHUNK {coordinate.X}, {coordinate.Y}"
        + $"  |  LOADED {_loaded.Count}  |  QUEUED {_pending.Count}";
}
	#endregion
}
