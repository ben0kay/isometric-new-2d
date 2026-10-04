// Streams a finite isometric world around the camera.
// Ground lives under GroundChunks; obstacles share WorldObjects for Y-sorting.
using Godot;
using System.Collections.Generic;

public partial class ChunkController : Node
{
#region Configuration
[Export] public Vector2 TileSize { get; set; } = new(128, 64);
[Export] public int ChunkSize { get; set; } = 16;
[Export] public int WorldChunksPerAxis { get; set; } = 8;
[Export] public uint WorldSeed { get; set; } = 64;
[Export] public int ChunksLoadedPerFrame { get; set; } = 2;
[Export] public int ObstaclesPerChunk { get; set; } = 12;
[Export] public float SpawnClearRadius { get; set; } = 320f;
[Export] public bool ShowChunkBoundaries { get; set; }
[Export] public VisualDefinition RockVisual { get; set; }
[Export] public VisualDefinition CrateVisual { get; set; }
[Export] public int VegetationPatchesPerChunk { get; set; } = 3;
[Export] public int PlantsPerPatch { get; set; } = 4;
[Export] public VisualDefinition FrondVisual { get; set; }
[Export] public VisualDefinition ShrubVisual { get; set; }

[Export] public int TreesPerChunk { get; set; } = 3;
[Export] public VisualDefinition TreeVisual { get; set; }
[Export] public int GrassPatchesPerChunk { get; set; } = 6;
[Export] public int GrassTuftsPerPatch { get; set; } = 10;
[Export(PropertyHint.Range, "0,1,0.01")]
public float MediumGrassChance { get; set; } = 0.12f;
[Export(PropertyHint.Range, "0,1,0.01")]
public float TallGrassChance { get; set; } = 0f;
[Export] public VisualDefinition ShortGrassVisual { get; set; }
[Export] public VisualDefinition MediumGrassVisual { get; set; }
[Export] public VisualDefinition TallGrassVisual { get; set; }
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
	#endregion

	#region Lifecycle
// =========================================================
// Prepare cached artwork and vegetation helpers before loading the initial view.
public override async void _Ready()
{
	SetProcess(false);
	TileSize = new Vector2(Mathf.Max(16f, TileSize.X), Mathf.Max(8f, TileSize.Y));
	ChunkSize = Mathf.Max(1, ChunkSize);
	WorldChunksPerAxis = Mathf.Max(1, WorldChunksPerAxis);
	ChunksLoadedPerFrame = Mathf.Max(1, ChunksLoadedPerFrame);

	_groundRoot = GetNode<Node2D>("../../GroundChunks");
	_objects = GetNode<Node2D>("../../WorldObjects");
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

		_vegetation = new VegetationSpawner
		{
			Name = "VegetationSpawner",
			PatchesPerChunk = Mathf.Max(0, VegetationPatchesPerChunk),
			PlantsPerPatch = Mathf.Max(0, PlantsPerPatch),
			TreesPerChunk = Mathf.Max(0, TreesPerChunk),
			FrondVisual = FrondVisual,
			ShrubVisual = ShrubVisual,
			TreeVisual = TreeVisual
		};
		AddChild(_vegetation);

		_grass = new GrassSpawner
		{
			Name = "GrassSpawner",
			PatchesPerChunk = Mathf.Max(0, GrassPatchesPerChunk),
			TuftsPerPatch = Mathf.Max(0, GrassTuftsPerPatch),
			MediumChance = MediumGrassChance,
			TallChance = TallGrassChance,
			ShortVisual = ShortGrassVisual,
			MediumVisual = MediumGrassVisual,
			TallVisual = TallGrassVisual
		};
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
		_debug.Text = "Artwork load failed — see Errors.";
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
// Load terrain, obstacles, plants and grass for the next requested chunk.
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
// Generate seeded obstacles with optional shared artwork definitions.
private void CreateObstacles(Vector2I coordinate, LoadedChunk chunk)
{
	using RandomNumberGenerator rng = new();
	rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, WorldSeed);
	HashSet<Vector2I> occupied = new();

	for (int i = 0; i < ObstaclesPerChunk; i++)
	{
		Vector2I localTile = new(
			rng.RandiRange(0, (ChunkSize - 1) / 2) * 2,
			rng.RandiRange(0, (ChunkSize - 1) / 2) * 2);
		if (!occupied.Add(localTile)) continue;

		Vector2 tile = new(
			coordinate.X * ChunkSize + localTile.X,
			coordinate.Y * ChunkSize + localTile.Y);
		Vector2 localPoint = IsoGrid.TileToWorld(tile, TileSize);
		Vector2 globalPoint = _groundRoot.ToGlobal(localPoint);
		if (globalPoint.DistanceSquaredTo(_spawnPoint)
			< SpawnClearRadius * SpawnClearRadius) continue;

		bool crate = rng.Randf() < 0.2f;
		float width = crate ? 72f : rng.RandfRange(64f, 104f);
		float height = crate ? 48f : rng.RandfRange(56f, 96f);
		if (!ChasmFeature.HasGroundClearance(
			localPoint, TileSize, width * 0.6f)) continue;

		Obstacle obstacle = new()
		{
			Name = $"Obstacle_{coordinate.X}_{coordinate.Y}_{i}",
			Position = _objects.ToLocal(globalPoint),
			Kind = crate ? Obstacle.ObstacleKind.Crate
				: Obstacle.ObstacleKind.Rock,
			Footprint = new Vector2(width, width * 0.5f),
			Height = height,
			VisualOverride = crate ? CrateVisual : RockVisual
		};
		_objects.AddChild(obstacle);
		chunk.Obstacles.Add(obstacle);
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
	// Show the current player chunk and streaming totals.
	private void UpdateDebug()
	{
		Vector2I coordinate = IsoGrid.WorldToChunk(
			_groundRoot.ToLocal(_player.GlobalPosition), TileSize, ChunkSize);
		_debug.Text = $"CHUNK {coordinate.X}, {coordinate.Y}   |   LOADED {_loaded.Count}   |   QUEUED {_pending.Count}";
	}
	#endregion
}
