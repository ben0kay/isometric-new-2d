// Prepares distant terrain, activates nearby chunks, and retires old instances gradually.
// Each iterator resumes under a frame budget; objects stay under the shared Y-sort root.
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;

public partial class ChunkController : Node
{
	#region Configuration
	[ExportGroup("World")]
	[Export] public Vector2 TileSize { get; set; } = new(128, 64);
	[Export] public int ChunkSize { get; set; } = 16;
	[Export] public int WorldChunksPerAxis { get; set; } = 32;
	[Export] public uint WorldSeed { get; set; } = 64;

	[ExportGroup("Streaming")]
	[ExportSubgroup("Distances — extra chunks beyond the viewport")]
	[Export] public int ActivationMargin { get; set; } = 1;
	[Export] public int PreparationMargin { get; set; } = 2;
	[Export] public int RetentionMargin { get; set; } = 3;
	[Export] public double RetireDelaySeconds { get; set; } = 3.0;
	[ExportSubgroup("Work budgets")]
	[Export] public double BuildBudgetMs { get; set; } = 0.8;
	[Export] public double RetirementBudgetMs { get; set; } = 0.2;
	[Export] public double StartupBudgetMs { get; set; } = 6.0;
	[Export] public int MaxRetireStepsPerFrame { get; set; } = 6;
	[Export] public double CoverageInterval { get; set; } = 0.1;

	[ExportGroup("Debug")]
	[Export] public bool ShowChunkBoundaries { get; set; }
	[ExportGroup("Spawn")]
	[Export] public float SpawnClearRadius { get; set; } = 320f;
	[ExportGroup("Test Props")]
	[Export] public int CratesPerChunk { get; set; } = 2;
	[Export] public VisualDefinition CrateVisual { get; set; }
	#endregion

	#region Chunk Records
	private sealed class ChunkRecord
	{
		public Vector2I Coordinate;
		public WorldChunk Ground;
		public readonly List<Obstacle> Obstacles = new();
		public IEnumerator<ChunkBuildStage> Work;
		public ChunkBuildStage Stage = ChunkBuildStage.Queued;
		public bool Prepared, Ready, Retiring;
		public double LastRetained;
	}
	private readonly Dictionary<Vector2I, ChunkRecord> _chunks = new();
	private ChunkRecord _building, _retiring;
	public event Action<Vector2I> ChunkAvailabilityChanged;
	public bool WorldReady { get; private set; }
	#endregion

	#region References And Timing
	private Node2D _groundRoot, _objects;
	private Player _player;
	private Camera2D _camera;
	private Label _debug;
	private WorldGenerator _generator;
	private RockSpawner _rocks;
	private VegetationSpawner _vegetation;
	private GrassSpawner _grass;
	private WorldNavigation _navigation;
	private Vector2 _spawnPoint;
	private Vector2I _viewMin, _viewMax;
	private int _worldMin, _worldMax;
	private double _coverageTimer, _debugTimer;
	private double _lastWorkMs, _peakStepMs;
	private ChunkBuildStage _peakStage;
	private ProcessModeEnum _previousObjectsMode;
	#endregion

	#region Lifecycle
	// =========================================================
	// Cache artwork, freeze gameplay, and let later frames build the starting area.
	public override async void _Ready()
	{
		SetProcess(false);
		TileSize = new(Mathf.Max(16f, TileSize.X), Mathf.Max(8f, TileSize.Y));
		ChunkSize = Mathf.Max(1, ChunkSize);
		WorldChunksPerAxis = Mathf.Max(1, WorldChunksPerAxis);
		ActivationMargin = Mathf.Max(1, ActivationMargin);
		PreparationMargin = Mathf.Max(ActivationMargin + 1, PreparationMargin);
		RetentionMargin = Mathf.Max(PreparationMargin, RetentionMargin);

		_groundRoot = GetNode<Node2D>("../../GroundChunks");
		_objects = GetNode<Node2D>("../../WorldObjects");
		_generator = GetNode<WorldGenerator>("../WorldGenerator");
		_navigation = GetNode<WorldNavigation>("../WorldNavigation");
		_player = _objects.GetNode<Player>("Player");
		_camera = _player.GetNode<Camera2D>("Camera2D");
		_debug = GetNode<Label>("../../HUD/ChunkInfo");
		_spawnPoint = _player.GlobalPosition;
		_worldMin = -(WorldChunksPerAxis / 2);
		_worldMax = _worldMin + WorldChunksPerAxis - 1;
		_previousObjectsMode = _objects.ProcessMode;
		_objects.ProcessMode = ProcessModeEnum.Disabled;
		_debug.Text = "Loading cached artwork...";
		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (!IsInsideTree() || IsQueuedForDeletion()) return;
			await VegetationAtlas.EnsureReady(this);
			if (!IsInsideTree() || IsQueuedForDeletion()) return;
			await TreeAtlas.EnsureReady(this);
			if (!IsInsideTree() || IsQueuedForDeletion()) return;

			_rocks = new() { Name = "RockSpawner", Generator = _generator };
			_vegetation = new() { Name = "VegetationSpawner", Generator = _generator };
			_grass = new() { Name = "GrassSpawner", Generator = _generator };
			AddChild(_rocks); AddChild(_vegetation); AddChild(_grass);
			CreateWorldBoundary();
			_camera.ResetSmoothing(); _camera.ForceUpdateScroll();
			RefreshCoverage();
			SetProcess(true);
		}
		catch (Exception error) { FailStreaming(error); }
	}

	// =========================================================
	// Schedule build and retirement work separately; release gameplay when the buffer is ready.
	public override void _Process(double delta)
	{
		try
		{
			_coverageTimer -= delta; _debugTimer -= delta;
			if (_coverageTimer <= 0)
			{
				_coverageTimer = Math.Max(0.03, CoverageInterval);
				RefreshCoverage();
			}
			long started = Stopwatch.GetTimestamp();
			RunBuildBudget(WorldReady ? BuildBudgetMs : StartupBudgetMs);
			if (WorldReady) RunRetirementBudget();
			_lastWorkMs = ElapsedMs(started);
			if (!WorldReady && StartingAreaReady())
			{
				WorldReady = true;
				_objects.ProcessMode = _previousObjectsMode;
				_camera.ResetSmoothing();
				_peakStepMs = 0; _peakStage = ChunkBuildStage.Ready;
			}
			if (_debugTimer <= 0)
			{
				_debugTimer = 0.25;
				UpdateDebug();
			}
		}
		catch (Exception error) { FailStreaming(error); }
	}

	// =========================================================
	// Dispose suspended iterators so their temporary RNGs and buffers can be released.
	public override void _ExitTree()
	{
		foreach (ChunkRecord chunk in _chunks.Values) chunk.Work?.Dispose();
		_chunks.Clear();
	}

	// =========================================================
	// Stop on a real initialization error instead of repeatedly generating secondary errors.
	private void FailStreaming(Exception error)
	{
		SetProcess(false);
		if (GodotObject.IsInstanceValid(_objects)) _objects.ProcessMode = ProcessModeEnum.Disabled;
		if (GodotObject.IsInstanceValid(_debug)) _debug.Text = "Streaming failed — see Errors.";
		GD.PushError($"Chunk streaming failed: {error}");
	}
	#endregion

	#region Coverage And Priority
	// =========================================================
	// Derive visible bounds, register the outer preparation ring, and keep nearby records warm.
	private void RefreshCoverage()
	{
		Transform2D inverse = GetViewport().GetCanvasTransform().AffineInverse();
		Vector2 size = GetViewport().GetVisibleRect().Size;
		Vector2 min = new(float.MaxValue, float.MaxValue), max = new(float.MinValue, float.MinValue);
		for (int i = 0; i < 4; i++)
		{
			Vector2 corner = new((i == 1 || i == 2) ? size.X : 0, i >= 2 ? size.Y : 0);
			Vector2 tile = IsoGrid.WorldToTile(_groundRoot.ToLocal(inverse * corner), TileSize);
			min = new(Mathf.Min(min.X, tile.X), Mathf.Min(min.Y, tile.Y));
			max = new(Mathf.Max(max.X, tile.X), Mathf.Max(max.Y, tile.Y));
		}
		_viewMin = new(Mathf.FloorToInt((min.X + 0.5f) / ChunkSize), Mathf.FloorToInt((min.Y + 0.5f) / ChunkSize));
		_viewMax = new(Mathf.FloorToInt((max.X + 0.5f) / ChunkSize), Mathf.FloorToInt((max.Y + 0.5f) / ChunkSize));
		double now = Time.GetTicksMsec() / 1000.0;
		for (int y = Mathf.Max(_worldMin, _viewMin.Y - PreparationMargin); y <= Mathf.Min(_worldMax, _viewMax.Y + PreparationMargin); y++)
		for (int x = Mathf.Max(_worldMin, _viewMin.X - PreparationMargin); x <= Mathf.Min(_worldMax, _viewMax.X + PreparationMargin); x++)
		{
			Vector2I coordinate = new(x, y);
			if (!_chunks.ContainsKey(coordinate))
				_chunks.Add(coordinate, new ChunkRecord { Coordinate = coordinate, LastRetained = now });
		}
		foreach (ChunkRecord chunk in _chunks.Values)
			if (WithinMargin(chunk.Coordinate, RetentionMargin)) chunk.LastRetained = now;
	}

	// =========================================================
	// Test coverage in chunk coordinates; the map boundary is enforced when records are created.
	private bool WithinMargin(Vector2I coordinate, int margin)
	{
		return coordinate.X >= _viewMin.X - margin && coordinate.X <= _viewMax.X + margin
			&& coordinate.Y >= _viewMin.Y - margin && coordinate.Y <= _viewMax.Y + margin;
	}

	// =========================================================
	// Prioritize visible chunks, then nearby activation, then distant terrain preparation.
	private ChunkRecord SelectBuild()
	{
		ChunkRecord best = null;
		int bestPriority = int.MaxValue;
		float bestDistance = float.MaxValue;
		Vector2 player = _groundRoot.ToLocal(_player.GlobalPosition);
		foreach (ChunkRecord chunk in _chunks.Values)
		{
			if (chunk.Ready || chunk.Retiring) continue;
			bool nearby = WithinMargin(chunk.Coordinate, ActivationMargin);
			if (!nearby && (chunk.Prepared || !WithinMargin(chunk.Coordinate, PreparationMargin))) continue;
			int priority = WithinMargin(chunk.Coordinate, 0) ? 0 : nearby ? 1 : 2;
			float half = (ChunkSize - 1) * 0.5f;
			Vector2 centre = IsoGrid.TileToWorld(new Vector2(
				chunk.Coordinate.X * ChunkSize + half, chunk.Coordinate.Y * ChunkSize + half), TileSize);
			float distance = centre.DistanceSquaredTo(player);
			if (priority > bestPriority || (priority == bestPriority && distance >= bestDistance)) continue;
			best = chunk; bestPriority = priority; bestDistance = distance;
		}
		return best;
	}

	// =========================================================
	// Keep the player frozen until every chunk in the initial activation buffer is complete.
	private bool StartingAreaReady()
	{
		foreach (ChunkRecord chunk in _chunks.Values)
			if (WithinMargin(chunk.Coordinate, ActivationMargin) && !chunk.Ready) return false;
		return _chunks.Count > 0;
	}
	#endregion

	#region Frame Budgets
	// =========================================================
	// Resume one phase at a time; preserving phase ownership keeps placement snapshots coherent.
	private void RunBuildBudget(double budgetMs)
	{
		long started = Stopwatch.GetTimestamp();
		double budget = Math.Max(0.05, budgetMs);
		while (ElapsedMs(started) < budget)
		{
			_building ??= SelectBuild();
			if (_building == null) break;
			_building.Work ??= (_building.Prepared ? ActivateChunk(_building) : PrepareChunk(_building)).GetEnumerator();
			if (Advance(_building)) continue;
			_building.Work.Dispose(); _building.Work = null; _building = null;
		}
	}

	// =========================================================
	// Retire a limited number of instances, even while new chunks are being built.
	private void RunRetirementBudget()
	{
		long started = Stopwatch.GetTimestamp();
		int steps = 0;
		while (steps < Mathf.Max(1, MaxRetireStepsPerFrame) && ElapsedMs(started) < Math.Max(0.01, RetirementBudgetMs))
		{
			if (_retiring == null)
			{
				double now = Time.GetTicksMsec() / 1000.0;
				foreach (ChunkRecord chunk in _chunks.Values)
				{
					if (chunk == _building || WithinMargin(chunk.Coordinate, RetentionMargin)
						|| now - chunk.LastRetained < Math.Max(0, RetireDelaySeconds)) continue;
					_retiring = chunk;
					chunk.Work?.Dispose();
					chunk.Retiring = true; chunk.Ready = false;
					chunk.Stage = ChunkBuildStage.Retiring;
					ChunkAvailabilityChanged?.Invoke(chunk.Coordinate);
					chunk.Work = RetireChunk(chunk).GetEnumerator();
					break;
				}
				if (_retiring == null) break;
			}
			steps++;
			if (Advance(_retiring)) continue;
			_retiring.Work.Dispose(); _retiring.Work = null;
			_chunks.Remove(_retiring.Coordinate); _retiring = null;
		}
	}

	// =========================================================
	// Measure one resumable step; a single mesh upload can still exceed the requested budget.
	private bool Advance(ChunkRecord chunk)
	{
		long started = Stopwatch.GetTimestamp();
		bool more = chunk.Work.MoveNext();
		double elapsed = ElapsedMs(started);
		if (more) chunk.Stage = chunk.Work.Current;
		if (elapsed > _peakStepMs) { _peakStepMs = elapsed; _peakStage = chunk.Stage; }
		return more;
	}

	// =========================================================
	// Convert monotonic stopwatch ticks to milliseconds without allocating a Stopwatch object.
	private static double ElapsedMs(long started)
	{
		return (Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency;
	}
	#endregion

	#region Stage 1 — Prepare Distant Terrain
	// =========================================================
	// Prepare CPU mesh data outside the activation buffer; do not upload or spawn objects yet.
	private IEnumerable<ChunkBuildStage> PrepareChunk(ChunkRecord chunk)
	{
		chunk.Ground = new WorldChunk
		{
			Name = $"Chunk_{chunk.Coordinate.X}_{chunk.Coordinate.Y}",
			Coordinate = chunk.Coordinate, ChunkSize = ChunkSize, TileSize = TileSize,
			Seed = WorldSeed, ShowBoundary = ShowChunkBoundaries, Visible = false,
			Position = IsoGrid.TileToWorld(new Vector2(
				chunk.Coordinate.X * ChunkSize, chunk.Coordinate.Y * ChunkSize), TileSize)
		};
		_groundRoot.AddChild(chunk.Ground);
		foreach (ChunkBuildStage stage in chunk.Ground.PrepareSteps()) yield return stage;
		chunk.Prepared = true; chunk.Stage = ChunkBuildStage.Prepared;
	}
	#endregion

	#region Stages 2–7 — Activate Nearby Chunks
	// =========================================================
	// Finish terrain first, then solids, then walkable vegetation; navigation opens at completion.
	private IEnumerable<ChunkBuildStage> ActivateChunk(ChunkRecord chunk)
	{
		foreach (ChunkBuildStage stage in chunk.Ground.UploadSteps()) yield return stage;
		chunk.Ground.Visible = true;
		foreach (ChunkBuildStage stage in _rocks.PopulateSteps(chunk.Coordinate, ChunkSize,
			TileSize, WorldSeed, _groundRoot, _objects, _spawnPoint, SpawnClearRadius, chunk.Obstacles)) yield return stage;
		foreach (ChunkBuildStage stage in CreateCrateSteps(chunk)) yield return stage;
		foreach (ChunkBuildStage stage in _vegetation.PopulateSteps(chunk.Coordinate, ChunkSize,
			TileSize, WorldSeed, _groundRoot, _objects, _spawnPoint, SpawnClearRadius)) yield return stage;
		foreach (ChunkBuildStage stage in _grass.PopulateSteps(chunk.Coordinate, ChunkSize,
			TileSize, WorldSeed, _groundRoot, _objects, _spawnPoint)) yield return stage;
		chunk.Ready = true; chunk.Stage = ChunkBuildStage.Ready;
		ChunkAvailabilityChanged?.Invoke(chunk.Coordinate);
	}

// =========================================================
// Place test crates outside basin reservations and existing solid obstacles.
private IEnumerable<ChunkBuildStage> CreateCrateSteps(ChunkRecord chunk)
{
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);
    using RandomNumberGenerator rng = new();

    Vector2I coordinate = chunk.Coordinate;
    rng.Seed = IsoGrid.Hash(coordinate.X, coordinate.Y, WorldSeed ^ 0xC8A7u);

    float lowX = coordinate.X * ChunkSize - 0.5f;
    float lowY = coordinate.Y * ChunkSize - 0.5f;
    Vector2 footprint = new(72, 36);

    for (int i = 0; i < Mathf.Max(0, CratesPerChunk); i++)
    {
        yield return ChunkBuildStage.Crates;

        Vector2 tile = new(
            rng.RandfRange(lowX, lowX + ChunkSize),
            rng.RandfRange(lowY, lowY + ChunkSize));
        Vector2 local = IsoGrid.TileToWorld(tile, TileSize);
        Vector2 global = _groundRoot.ToGlobal(local);

        if (global.DistanceSquaredTo(_spawnPoint) <
                SpawnClearRadius * SpawnClearRadius ||
            !ChasmFeature.HasGroundClearance(local, TileSize, 48f) ||
            WorldPlacement.IsBlocked(
                _objects, global, footprint, obstacles, new Vector2(12, 8)))
            continue;

        Obstacle crate = new()
        {
            Name = $"Crate_{coordinate.X}_{coordinate.Y}_{i}",
            Position = _objects.ToLocal(global),
            Kind = Obstacle.ObstacleKind.Crate,
            Footprint = footprint,
            Height = 48f,
            VisualOverride = CrateVisual
        };

        _objects.AddChild(crate);
        chunk.Obstacles.Add(crate);
        obstacles.Add(crate);
    }
}
	#endregion

	#region Stage 8 — Retire Old Chunks
	// =========================================================
	// Queue individual objects for deletion before releasing the terrain node and its meshes.
	private IEnumerable<ChunkBuildStage> RetireChunk(ChunkRecord chunk)
	{
		foreach (ChunkBuildStage stage in _grass.RemoveSteps(chunk.Coordinate)) yield return stage;
		foreach (ChunkBuildStage stage in _vegetation.RemoveSteps(chunk.Coordinate)) yield return stage;
		foreach (Obstacle obstacle in chunk.Obstacles)
		{
			if (GodotObject.IsInstanceValid(obstacle)) obstacle.QueueFree();
			yield return ChunkBuildStage.Retiring;
		}
		if (GodotObject.IsInstanceValid(chunk.Ground)) chunk.Ground.QueueFree();
		yield return ChunkBuildStage.Retiring;
	}
	#endregion

	#region Navigation And Boundary
	// =========================================================
	// Expose complete chunks only; prepared terrain is not yet navigable.
	public bool IsNavigationPointAvailable(Vector2 globalPoint, float clearance = 0f)
	{
		if (_groundRoot == null || !WorldReady) return false;
		Vector2 local = _groundRoot.ToLocal(globalPoint), tile = IsoGrid.WorldToTile(local, TileSize);
		float low = _worldMin * ChunkSize - 0.5f, high = (_worldMax + 1) * ChunkSize - 0.5f;
		if (tile.X < low || tile.Y < low || tile.X >= high || tile.Y >= high) return false;
		if (!ChasmFeature.HasGroundClearance(local, TileSize, clearance)) return false;
		return _chunks.TryGetValue(IsoGrid.WorldToChunk(local, TileSize, ChunkSize), out ChunkRecord chunk)
			&& chunk.Ready && !chunk.Retiring;
	}

	// =========================================================
	// Preserve the finite world's four collision edges; streaming does not change world size.
	private void CreateWorldBoundary()
	{
		float low = _worldMin * ChunkSize - 0.5f, high = (_worldMax + 1) * ChunkSize - 0.5f;
		Vector2[] corners = { IsoGrid.TileToWorld(new(low, low), TileSize), IsoGrid.TileToWorld(new(high, low), TileSize),
			IsoGrid.TileToWorld(new(high, high), TileSize), IsoGrid.TileToWorld(new(low, high), TileSize) };
		StaticBody2D boundary = new() { Name = "WorldBoundary", CollisionLayer = 1, CollisionMask = 0 };
		_groundRoot.AddChild(boundary);
		for (int i = 0; i < 4; i++)
			boundary.AddChild(new CollisionShape2D { Name = $"Edge_{i}",
				Shape = new SegmentShape2D { A = corners[i], B = corners[(i + 1) % 4] } });
	}
	#endregion

	#region Debug
	// =========================================================
	// Show preparation, activation, retirement and the slowest scheduled step since startup.
	private void UpdateDebug()
	{
		int ready = 0, prepared = 0;
		foreach (ChunkRecord chunk in _chunks.Values)
		{
			if (chunk.Ready) ready++;
			else if (chunk.Prepared && !chunk.Retiring) prepared++;
		}
		Vector2 tile = IsoGrid.WorldToTile(_groundRoot.ToLocal(_player.GlobalPosition), TileSize);
		string stage = _building == null ? "Idle" : _building.Stage.ToString();
		_debug.Text = $"{(WorldReady ? "WORLD READY" : "PREPARING START AREA")} | {_generator.GetBiome(tile).DisplayName}\n"
			+ $"READY {ready} | PREPARED {prepared} | RECORDS {_chunks.Count}\n"
			+ $"BUILD {stage} | RETIRE {(_retiring == null ? "Idle" : _retiring.Coordinate.ToString())}\n"
			+ $"WORK {_lastWorkMs:F2} ms | PEAK STEP {_peakStepMs:F2} ms ({_peakStage})\n"
			+ $"NAV {(_navigation.IsBuilding ? "Building" : "Idle")} | PEAK NAV STEP {_navigation.PeakStepMs:F2} ms";
	}
	#endregion
}
