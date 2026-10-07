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

			_camera.ResetSmoothing(); _camera.ForceUpdateScroll();
			RefreshCoverage();
			SetProcess(true);
		}
		catch (Exception error) { FailStreaming(error); }
	}

	// =========================================================
	// Wait for entrance reservations, then resume normal surface streaming.
	public override void _Process(double delta)
	{


		try
		{
			_coverageTimer -= delta;
			_debugTimer -= delta;

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
				_peakStepMs = 0;
				_peakStage = ChunkBuildStage.Ready;
			}

			if (_debugTimer <= 0)
			{
				_debugTimer = 0.25;
				UpdateDebug();
			}
		}
		catch (Exception error)
		{
			FailStreaming(error);
		}
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
	// Prepare viewport coverage without finite world clamps.
	private void RefreshCoverage(Vector2? destination = null)
	{
		Transform2D inverse =
			GetViewport().GetCanvasTransform().AffineInverse();
		Vector2 size = GetViewport().GetVisibleRect().Size;
		Vector2 offset = destination.HasValue
			? destination.Value - inverse * (size * 0.5f)
			: Vector2.Zero;

		Vector2 min = new(float.MaxValue, float.MaxValue);
		Vector2 max = new(float.MinValue, float.MinValue);

		for (int i = 0; i < 4; i++)
		{
			Vector2 corner = new(
				(i == 1 || i == 2) ? size.X : 0f,
				i >= 2 ? size.Y : 0f);

			Vector2 tile = IsoGrid.WorldToTile(
				_groundRoot.ToLocal(inverse * corner + offset), TileSize);

			min = new(Mathf.Min(min.X, tile.X), Mathf.Min(min.Y, tile.Y));
			max = new(Mathf.Max(max.X, tile.X), Mathf.Max(max.Y, tile.Y));
		}

		_viewMin = new(
			Mathf.FloorToInt((min.X + 0.5f) / ChunkSize),
			Mathf.FloorToInt((min.Y + 0.5f) / ChunkSize));
		_viewMax = new(
			Mathf.FloorToInt((max.X + 0.5f) / ChunkSize),
			Mathf.FloorToInt((max.Y + 0.5f) / ChunkSize));

		double now = Time.GetTicksMsec() / 1000.0;

		for (int y = _viewMin.Y - PreparationMargin;
			y <= _viewMax.Y + PreparationMargin; y++)
		for (int x = _viewMin.X - PreparationMargin;
			x <= _viewMax.X + PreparationMargin; x++)
		{
			Vector2I coordinate = new(x, y);
			if (!_chunks.ContainsKey(coordinate))
				_chunks.Add(coordinate, new ChunkRecord
				{
					Coordinate = coordinate,
					LastRetained = now
				});
		}

		foreach (ChunkRecord chunk in _chunks.Values)
			if (WithinMargin(chunk.Coordinate, RetentionMargin))
				chunk.LastRetained = now;
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
// Retire old buffers while preserving loaded routes used by surface pursuers.
private void RunRetirementBudget()
{
	long started = Stopwatch.GetTimestamp();
	int steps = 0;
	CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(this);

	while (steps < Mathf.Max(1, MaxRetireStepsPerFrame) &&
		ElapsedMs(started) < Math.Max(0.01, RetirementBudgetMs))
	{
		if (_retiring == null)
		{
			double now = Time.GetTicksMsec() / 1000.0;

			foreach (ChunkRecord chunk in _chunks.Values)
			{
				if (chunk == _building ||
					WithinMargin(chunk.Coordinate, RetentionMargin) ||
					now - chunk.LastRetained < Math.Max(0, RetireDelaySeconds) ||
					pursuit?.RetainSurface(chunk.Coordinate) == true)
					continue;

				_retiring = chunk;
				chunk.Work?.Dispose();
				chunk.Retiring = true;
				chunk.Ready = false;
				chunk.Stage = ChunkBuildStage.Retiring;
				ChunkAvailabilityChanged?.Invoke(chunk.Coordinate);
				chunk.Work = RetireChunk(chunk).GetEnumerator();
				break;
			}

			if (_retiring == null) break;
		}

		steps++;
		if (Advance(_retiring)) continue;

		_retiring.Work.Dispose();
		_retiring.Work = null;
		_chunks.Remove(_retiring.Coordinate);
		_retiring = null;
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
	// Hold metadata for the chunk's lifetime and finish it before sampling terrain.
	private IEnumerable<ChunkBuildStage> PrepareChunk(ChunkRecord chunk)
	{
		InfiniteWorldGeneration generation =
			InfiniteWorldGeneration.Find(this);

		if (generation == null)
			throw new InvalidOperationException(
				"World requires InfiniteWorldGeneration.");

		Rect2 area = new(
			new Vector2(chunk.Coordinate.X * ChunkSize - 0.5f,
				chunk.Coordinate.Y * ChunkSize - 0.5f),
			Vector2.One * ChunkSize);

		chunk.Ground = new WorldChunk
		{
			Name = $"Chunk_{chunk.Coordinate.X}_{chunk.Coordinate.Y}",
			Coordinate = chunk.Coordinate,
			ChunkSize = ChunkSize,
			TileSize = TileSize,
			Seed = WorldSeed,
			ShowBoundary = ShowChunkBoundaries,
			Visible = false,
			Position = IsoGrid.TileToWorld(new Vector2(
				chunk.Coordinate.X * ChunkSize,
				chunk.Coordinate.Y * ChunkSize), TileSize)
		};

		_groundRoot.AddChild(chunk.Ground);
		GenerationMetadataLease.Attach(chunk.Ground, generation, area);

		foreach (int step in generation.PrepareArea(area))
			yield return ChunkBuildStage.Queued;

		foreach (ChunkBuildStage stage in chunk.Ground.PrepareSteps())
			yield return stage;

		chunk.Prepared = true;
		chunk.Stage = ChunkBuildStage.Prepared;
	}
	#endregion

	#region Stages 2–7 — Activate Nearby Chunks
	// =========================================================
	// Activate terrain, solids, vegetation and local water artwork.
	private IEnumerable<ChunkBuildStage> ActivateChunk(ChunkRecord chunk)
	{
		foreach (ChunkBuildStage stage in chunk.Ground.UploadSteps())
			yield return stage;
		chunk.Ground.Visible = true;

		foreach (ChunkBuildStage stage in _rocks.PopulateSteps(
			chunk.Coordinate, ChunkSize, TileSize, WorldSeed,
			_groundRoot, _objects, _spawnPoint,
			SpawnClearRadius, chunk.Obstacles))
			yield return stage;

		foreach (ChunkBuildStage stage in CreateCrateSteps(chunk))
			yield return stage;

		foreach (ChunkBuildStage stage in _vegetation.PopulateSteps(
			chunk.Coordinate, ChunkSize, TileSize, WorldSeed,
			_groundRoot, _objects, _spawnPoint, SpawnClearRadius))
			yield return stage;

		foreach (ChunkBuildStage stage in _grass.PopulateSteps(
			chunk.Coordinate, ChunkSize, TileSize, WorldSeed,
			_groundRoot, _objects, _spawnPoint))
			yield return stage;

		SurfaceWorld surfaces = SurfaceWorld.Find(this);
		if (surfaces != null)
		{
			Rect2 area = new(
				new Vector2(chunk.Coordinate.X * ChunkSize - 0.5f,
					chunk.Coordinate.Y * ChunkSize - 0.5f),
				Vector2.One * ChunkSize);

			foreach (int step in surfaces.PrepareFills(area))
				yield return ChunkBuildStage.Prepared;
		}

		chunk.Ready = true;
		chunk.Stage = ChunkBuildStage.Ready;
		ChunkAvailabilityChanged?.Invoke(chunk.Coordinate);
	}

// =========================================================
// Spawn world loot using CONFIG's shared frequency and stable chunk identities.
private IEnumerable<ChunkBuildStage> CreateCrateSteps(ChunkRecord chunk)
{
	WorldConfig config = WorldConfig.Find(this);
	Vector2I coordinate = chunk.Coordinate;

	int attempts = config.GetLootSpawnAttempts(
		CratesPerChunk,
		IsoGrid.Hash(
			coordinate.X, coordinate.Y, WorldSeed ^ 0x10A7u));

	if (attempts == 0) yield break;

	LootWorld.GetOrCreate(this);

	PackedScene scene = GD.Load<PackedScene>(
		"res://WORLDABLES/Objects/Storage/ArmouredCrate/ArmouredCrateLoot.tscn");

	if (scene == null)
		throw new InvalidOperationException(
			"ArmouredCrateLoot.tscn is missing.");

	List<Obstacle> obstacles = WorldPlacement.CollectObstacles(_objects);
	using RandomNumberGenerator rng = new();

	rng.Seed = IsoGrid.Hash(
		coordinate.X, coordinate.Y, WorldSeed ^ 0xC8A7u);

	float lowX = coordinate.X * ChunkSize - 0.5f;
	float lowY = coordinate.Y * ChunkSize - 0.5f;

	for (int i = 0; i < attempts; i++)
	{
		yield return ChunkBuildStage.Crates;

		Vector2 tile = new(
			rng.RandfRange(lowX, lowX + ChunkSize),
			rng.RandfRange(lowY, lowY + ChunkSize));

		Vector2 local = IsoGrid.TileToWorld(tile, TileSize);
		Vector2 global = _groundRoot.ToGlobal(local);

		Obstacle crate = scene.Instantiate<Obstacle>();
		Vector2 footprint = crate.Footprint;

		if (global.DistanceSquaredTo(_spawnPoint) <
				SpawnClearRadius * SpawnClearRadius ||
			!ChasmFeature.HasGroundClearance(
				local, TileSize, footprint.Length() * 0.5f + 12f) ||
			WorldPlacement.IsBlocked(
				_objects, global, footprint,
				obstacles, new Vector2(12, 8)))
		{
			crate.Free();
			continue;
		}

		crate.Name =
			$"ArmouredLootCrate_{coordinate.X}_{coordinate.Y}_{i}";

		crate.Position = _objects.ToLocal(global);
		crate.GetNode<LootContainer>("Systems/Loot").PersistentId =
			$"armoured_crate:{coordinate.X}:{coordinate.Y}:{i}";

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
	// Require loaded terrain and local clearance rather than world boundaries.
	public bool IsNavigationPointAvailable(
		Vector2 globalPoint, float clearance = 0f)
	{
		if (_groundRoot == null || !WorldReady) return false;

		Vector2 local = _groundRoot.ToLocal(globalPoint);

		if (!_chunks.TryGetValue(
			IsoGrid.WorldToChunk(local, TileSize, ChunkSize),
			out ChunkRecord chunk) ||
			!chunk.Ready || chunk.Retiring)
			return false;

		if (!ChasmFeature.HasGroundClearance(local, TileSize, clearance))
			return false;

		return TerrainSlopeWorld.Ensure(this)
			.HasClearance(globalPoint, clearance);
	}



// =========================================================
	// Accept valid coordinates without imposing a finite planet boundary.
	public bool IsDestinationWithinBounds(
		Vector2 point, float clearance = 120f)
	{
		return _groundRoot != null &&
			float.IsFinite(point.X) && float.IsFinite(point.Y) &&
			float.IsFinite(clearance) && clearance >= 0f;
	}

	// =========================================================
	// Advance local destination loading and retire old surface buffers.
	public bool PrepareDestination(Vector2 point)
	{
		if (GetMeta("destination_preload_failed", false).AsBool())
			return false;
		if (!IsDestinationWithinBounds(point)) return false;

		try
		{
			RefreshCoverage(point);
			_coverageTimer = 0;

			// =========================================================
			// Require every activation chunk and the actual landing chunk.
			bool CheckBuffer(out int ready, out int total)
			{
				ready = total = 0;

				for (int y = _viewMin.Y - ActivationMargin;
					y <= _viewMax.Y + ActivationMargin; y++)
				for (int x = _viewMin.X - ActivationMargin;
					x <= _viewMax.X + ActivationMargin; x++)
				{
					total++;
					if (_chunks.TryGetValue(
						new Vector2I(x, y), out ChunkRecord record) &&
						record.Ready && !record.Retiring)
						ready++;
				}

				Vector2I landing = IsoGrid.WorldToChunk(
					_groundRoot.ToLocal(point), TileSize, ChunkSize);

				return total > 0 && ready == total &&
					_chunks.TryGetValue(landing, out ChunkRecord target) &&
					target.Ready && !target.Retiring;
			}

			bool complete = CheckBuffer(out int ready, out int total);

			if (!complete)
			{
				long started = Stopwatch.GetTimestamp();
				RunBuildBudget(BuildBudgetMs);
				_lastWorkMs = ElapsedMs(started);
				complete = CheckBuffer(out ready, out total);
			}

			string stage = _building?.Stage.ToString() ?? "Idle";
			SetMeta("destination_preload_status",
				complete ? "surface buffer ready" :
				$"{ready}/{total} chunks | {stage}");

			return complete;
		}
		catch (Exception error)
		{
			SetMeta("destination_preload_failed", true);
			SetMeta("destination_preload_status", "loading failed — see Errors");
			GD.PushError($"Surface destination preload failed: {error}");
			return false;
		}
	}

	// =========================================================
	// Continue releasing obsolete surface buffers while surface simulation is paused.
	public void RetireUnusedChunks()
	{
		RunRetirementBudget();
	}
	#endregion

	#region Debug

// =========================================================
// Show world diagnostics and the terrain classification beneath the player.
private void UpdateDebug()
{
	int ready = 0, prepared = 0;
	foreach (ChunkRecord chunk in _chunks.Values)
	{
		if (chunk.Ready) ready++;
		else if (chunk.Prepared && !chunk.Retiring) prepared++;
	}

	Vector2 position = _player.GlobalPosition;
	Vector2 tile = IsoGrid.WorldToTile(
		_groundRoot.ToLocal(position), TileSize);

	TerrainElevation elevation =
		GetNode<TerrainElevation>("../TerrainElevation");
	TerrainSlopeWorld slopes = TerrainSlopeWorld.Ensure(this);
	TerrainSlopeWorld.SlopeSample sample = slopes.AtWorld(position);

	float height = elevation.SampleWorldHeight(position);
	string stage = _building == null
		? "Idle" : _building.Stage.ToString();

	_debug.Text =
		$"{(WorldReady ? "WORLD READY" : "PREPARING START AREA")} | " +
		$"{_generator.GetBiome(tile).DisplayName}\n" +
		$"HEIGHT {height:F1} | SIM SLOPE {sample.Angle:F1}° | " +
		$"{(sample.Blocked ? "TOO STEEP" : "WALKABLE")} " +
		$"(LIMIT {slopes.MaximumAngle:F0}°)\n" +
		$"READY {ready} | PREPARED {prepared} | RECORDS {_chunks.Count}\n" +
		$"BUILD {stage} | RETIRE " +
		$"{(_retiring == null ? "Idle" : _retiring.Coordinate.ToString())}\n" +
		$"WORK {_lastWorkMs:F2} ms | PEAK STEP {_peakStepMs:F2} ms " +
		$"({_peakStage})\n" +
		$"NAV {(_navigation.IsBuilding ? "Building" : "Idle")} | " +
		$"PEAK NAV STEP {_navigation.PeakStepMs:F2} ms";
}

	#endregion

		// =========================================================
	// Check loaded surface ownership without invoking slope or basin queries.
	public bool HasReadySurface(Rect2 tileArea)
	{
		if (!WorldReady) return false;

		Vector2I first = new(
			Mathf.FloorToInt((tileArea.Position.X + 0.5f) / ChunkSize),
			Mathf.FloorToInt((tileArea.Position.Y + 0.5f) / ChunkSize));
		Vector2I last = new(
			Mathf.FloorToInt((tileArea.End.X + 0.5f) / ChunkSize),
			Mathf.FloorToInt((tileArea.End.Y + 0.5f) / ChunkSize));

		for (int y = first.Y; y <= last.Y; y++)
		for (int x = first.X; x <= last.X; x++)
			if (_chunks.TryGetValue(new Vector2I(x, y), out ChunkRecord chunk) &&
				chunk.Ready && !chunk.Retiring)
				return true;

		return false;
	}
}
