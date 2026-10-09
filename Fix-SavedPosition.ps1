# Fixes saved-position startup and keeps original spawn separate. No Git operations.
# Run from your project root with Godot closed. -Preview checks without writing.
[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (!(Test-Path (Join-Path $ProjectRoot 'project.godot'))) { throw 'Choose the Godot project root.' }
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Godot*' }) {
    throw 'Close Godot before running this installer.'
}
$Utf8 = New-Object System.Text.UTF8Encoding($false)
function Normalize([string]$Text) { return $Text.TrimStart([char]0xFEFF).Replace("`r`n", "`n").TrimEnd("`r", "`n") }
function Digest([string]$Text) {
    $Hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($Hasher.ComputeHash($Utf8.GetBytes((Normalize $Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $Hasher.Dispose() }
}
$Changes = [ordered]@{}
$Expected = @{}
$Changes['SYSTEMS/Saving/CampaignSession.cs'] = @'
// Coordinates the partial world/player save pass; later sections extend this owner.
using Godot;
using System;
using System.Diagnostics;
using System.IO;

public partial class CampaignSession : Node
{
    private CampaignData _data;
    private bool _restoring, _finished;
    private Node _world;
    private Player _player;
    private ChunkController _chunks;
    private WorldClock _clock;
    private ProcessModeEnum _objectsMode;
    private readonly Stopwatch _startup = new();

    // =========================================================
    // Instantiate a detached world and apply its recipe before Ready runs.
    public static void Launch(Node menu, bool continueCampaign)
    {
        PlayerProfile profile = ProfileStore.Selected
            ?? throw new InvalidOperationException("Select a profile first.");
        CampaignData data = continueCampaign ? CampaignStore.Load(profile.Id) : new CampaignData
        {
            ProfileId = profile.Id, CampaignId = Guid.NewGuid().ToString("N")
        };
        if (continueCampaign)
        {
            CampaignRecipe.Validate(data);
            if (data.Player.Layer != WorldLayerId.Surface ||
                !float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
                !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY))
                throw new InvalidDataException("This pass restores surface campaigns only.");
        }
        PackedScene scene = GD.Load<PackedScene>(MenuNavigation.CampaignScene)
            ?? throw new IOException("Campaign scene is unavailable.");
        Node world = scene.Instantiate();
        try
        {
            Player player = world.GetNode<Player>("WorldObjects/Player");
            ChunkController chunks = world.GetNode<ChunkController>("Systems/ChunkController");
            if (continueCampaign)
            {
                CampaignRecipe.Apply(world, data);
                // Begin loading at the actual save point, not the original landing site.
                player.Position = new Vector2(data.Player.X, data.Player.Y);
            }
            else
            {
                // A campaign uses a fresh seed rather than the scene's fixed test seed.
                using RandomNumberGenerator random = new();
                random.Randomize();
                chunks.WorldSeed = random.Randi();
                data.Seed = chunks.WorldSeed;
                data.SpawnX = player.Position.X; data.SpawnY = player.Position.Y;
                CampaignRecipe.Capture(world, data);
            }
            world.AddChild(new CampaignSession
                { Name = "CampaignSession", _data = data, _restoring = continueCampaign });
        }
        catch { world.Free(); throw; }
        SceneTree tree = menu.GetTree();
        // Retire the old scene before activating the detached campaign.
        Callable.From(() =>
        {
            Node previous = tree.CurrentScene;
            if (previous != null) { tree.Root.RemoveChild(previous); previous.QueueFree(); }
            tree.Paused = false;
            tree.Root.AddChild(world);
            tree.CurrentScene = world;
        }).CallDeferred();
    }

    // =========================================================
    // Locate this world's campaign without keeping static references to old scenes.
    private static CampaignSession FindCampaign(Node context)
    {
        for (Node node = context; node != null; node = node.GetParent())
        {
            CampaignSession campaign = node.GetNodeOrNull<CampaignSession>("CampaignSession");
            if (campaign != null) return campaign;
        }
        return null;
    }

    // =========================================================
    // Keep respawning and generation clearance anchored to the original landing site.
    public static Vector2? OriginalSpawnFor(Node context)
    {
        if (context is not Player player) return null;
        CampaignSession campaign = FindCampaign(player);
        if (campaign?._data == null) return null;
        Node2D objects = player.GetParent<Node2D>();
        return objects.ToGlobal(new Vector2(campaign._data.SpawnX, campaign._data.SpawnY));
    }

    // =========================================================
    // Show exactly which coordinates were committed by the manual save action.
    public static string SavedPositionFor(Node context)
    {
        CampaignSession campaign = FindCampaign(context);
        if (campaign?._data?.Player == null) return "";
        PlayerSaveData player = campaign._data.Player;
        return $"Saved position: X {player.X:0.##}, Y {player.Y:0.##} ({player.Layer}).";
    }

    // =========================================================
    // Let terrain startup run while player actions and item collection remain frozen.
    public override void _Ready()
    {
        _world = GetParent();
        _player = _world.GetNode<Player>("WorldObjects/Player");
        _chunks = _world.GetNode<ChunkController>("Systems/ChunkController");
        _objectsMode = ProcessModeEnum.Inherit;
        ProcessPriority = 1000;
        _startup.Start();
    }

    // =========================================================
    // Wait for terrain, player artwork and the deferred world clock, then restore once.
    public override void _Process(double delta)
    {
        if (_finished) return;
        Node objects = _world.GetNode("WorldObjects");
        objects.ProcessMode = ProcessModeEnum.Disabled;
        try
        {
            _clock ??= WorldEclipse.Find(this)?.GetNodeOrNull<WorldClock>("WorldClock");
            if (!_chunks.WorldReady || _player.Controls == null || !_player.IsPhysicsProcessing() || _clock == null)
            {
                if (_startup.Elapsed.TotalSeconds > 120)
                    throw new IOException("Campaign initialization did not finish; check Godot's errors.");
                return;
            }
            if (_restoring)
            {
                Vector2 position = new(_data.Player.X, _data.Player.Y);
                _player.GlobalPosition = position;
                if (!_chunks.PrepareDestination(position))
                {
                    if (_startup.Elapsed.TotalSeconds > 120)
                        throw new IOException("Saved destination could not be prepared.");
                    return;
                }
                if (!_chunks.IsNavigationPointAvailable(position, 12f))
                    throw new InvalidDataException("Saved position is no longer on available surface terrain.");
                RestorePlayer();
                _player.GlobalPosition = position;
                _player.Velocity = Vector2.Zero;
                _clock.RestoreSave(_data.WorldSeconds);
            }
            else _clock.RestoreSave(0);
            Camera2D camera = _player.GetNode<Camera2D>("Camera2D");
            camera.ResetSmoothing();
            camera.ForceUpdateScroll();
            if (_restoring)
                GD.Print($"[CampaignLoad] Profile {_data.ProfileId}: restored " +
                    $"{_player.GlobalPosition}, saved ({_data.Player.X}, {_data.Player.Y}).");
            PauseMenu.Attach(_player, _player.Controls).BindSave(Save);
            objects.ProcessMode = _objectsMode;
            _finished = true; SetProcess(false);
            if (_restoring && !string.IsNullOrEmpty(CampaignStore.RecoveryMessage))
                GD.Print(CampaignStore.RecoveryMessage);
        }
        catch (Exception error) { FailLoad(error); }
    }

    // =========================================================
    // Restore capacities first, physical items second, shortcuts and vitals last.
    private void RestorePlayer()
    {
        PlayerSaveData saved = _data.Player;
        _player.GetNode<PlayerStats>("Systems/Stats").RestoreSave(saved);
        ItemCatalog items = ResourceWorld.Find(this).Catalog;
        _player.GetNode<PlayerInventory>("Systems/Inventory").RestoreSave(saved, items);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").RestoreSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").RestoreSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").RestoreSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").RestoreSave(saved);
    }

    // =========================================================
    // Capture supported sections only while paused, on solid surface ground.
    private void Save()
    {
        if (!_finished || !GetTree().Paused || ProfileStore.Selected?.Id != _data.ProfileId)
            throw new InvalidOperationException("No paused campaign belongs to the selected profile.");
        if (WorldLayerMember.For(_player) != WorldLayerId.Surface)
            throw new InvalidOperationException("Underground saving comes with the layer-restoration pass.");
        if (_player.IsAirborne || !_player.GetNode<Health>("Systems/Health").IsAlive)
            throw new InvalidOperationException("Save while alive and standing on the ground.");
        PlayerSaveData saved = new()
        {
            X = _player.GlobalPosition.X, Y = _player.GlobalPosition.Y,
            Layer = WorldLayerId.Surface,
            Health = _player.GetNode<Health>("Systems/Health").Current
        };
        _player.GetNode<PlayerStats>("Systems/Stats").CaptureSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").CaptureSave(saved);
        _player.GetNode<PlayerInventory>("Systems/Inventory").CaptureSave(saved);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").CaptureSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").CaptureSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").CaptureSave(saved);
        _data.Player = saved;
        _data.WorldSeconds = _clock.ElapsedSeconds;
        _data.SavedUtc = DateTime.UtcNow;
        CampaignStore.Write(_data);
        GD.Print($"[CampaignSave] Profile {_data.ProfileId}: " +
            $"({saved.X}, {saved.Y}), original spawn ({_data.SpawnX}, {_data.SpawnY}).");
    }

    // =========================================================
    // Keep failed restoration frozen and show an exit route without saving over it.
    private void FailLoad(Exception error)
    {
        _finished = true; SetProcess(false);
        GetTree().Paused = true;
        AcceptDialog dialog = new()
        {
            Title = "Campaign load failed", DialogText = error.Message +
                "\nYour existing save was preserved.",
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(dialog);
        dialog.GetOkButton().Text = "MAIN MENU";
        dialog.Confirmed += () => MenuNavigation.Open(this, MenuNavigation.MainScene);
        dialog.PopupCentered(new Vector2I(640, 240));
        GD.PushError(error.Message);
    }
}
'@
$Changes['WORLD/Chunks/ChunkController.cs'] = @'
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
		_spawnPoint = CampaignSession.OriginalSpawnFor(_player) ?? _player.GlobalPosition;

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
	WorldLayerPursuit pursuit = WorldLayerPursuit.Find(this);

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
// Activate terrain, rocks, vegetation, grass, and local water artwork.
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
'@
$Changes['COMBAT/CombatLife.cs'] = @'
// Responds to health signals with hit feedback and actor death handling.
// Player instances can respawn; enemy instances are removed on death.
using Godot;

public partial class CombatLife : Node
{
    #region Configuration
    [Export] public bool Respawn { get; set; }
    [Export] public double RespawnDelay { get; set; } = 1.5;
    #endregion

    #region State
    private CharacterBody2D _actor;
    private Health _health;
    private CanvasItem _visual;
    private Label _label;
    private Vector2 _spawn;
    private uint _collisionLayer;
    private double _flash, _respawn;
    private bool _waiting;
    #endregion

    #region Lifecycle
    // =========================================================
    // Connect health feedback and optionally create the player's compact HUD.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<CharacterBody2D>();
        _health = GetNode<Health>("../Health");
        _spawn = CampaignSession.OriginalSpawnFor(_actor) ?? _actor.GlobalPosition;
        _collisionLayer = _actor.CollisionLayer;
        _health.Hit += OnHit;
        _health.Died += OnDeath;
        _health.Changed += OnChanged;
        SetProcess(false);

        if (!Respawn) return;
        CanvasLayer hud = new() { Name = "CombatHUD", Layer = 2 };
        AddChild(hud);
        _label = new Label { Position = new Vector2(16, 72) };
        _label.AddThemeFontSizeOverride("font_size", 16);
        _label.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        hud.AddChild(_label);
        OnChanged(_health.Current, _health.MaxHealth);
    }

    // =========================================================
    // Remove managed event subscriptions when this actor leaves the tree.
    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_health)) return;
        _health.Hit -= OnHit;
        _health.Died -= OnDeath;
        _health.Changed -= OnChanged;
    }

    // =========================================================
    // Process feedback only while a flash or respawn countdown is active.
    public override void _Process(double delta)
    {
        if (_flash > 0.0)
        {
            _flash -= delta;
            if (_flash <= 0.0 && GodotObject.IsInstanceValid(_visual))
                _visual.Modulate = Colors.White;
        }

        if (_waiting)
        {
            _respawn -= delta;
            if (_respawn <= 0.0) Revive();
        }
        if (_flash <= 0.0 && !_waiting) SetProcess(false);
    }
    #endregion

    #region Feedback
    // =========================================================
    // Brighten the existing sprite briefly without rebuilding its artwork.
    private void OnHit()
    {
        _visual = _actor.GetNodeOrNull<CanvasItem>("Visual");
        if (_visual != null) _visual.Modulate = new Color(3f, 3f, 3f, 1f);
        _flash = 0.08;
        SetProcess(true);
    }

// =========================================================
// Display the actor's vitality terminology and current value.
private void OnChanged(int current, int maximum)
{
    if (_label == null) return;
    _label.Text = $"{_health.VitalityLabel.ToUpperInvariant()} {current} / {maximum}";
    _label.Modulate = current <= maximum / 3 ? new Color("#ff7164") : Colors.White;
}
    #endregion

    #region Death And Respawn
    // =========================================================
    // Stop the actor and either remove it or begin its respawn countdown.
    private void OnDeath()
    {
        _actor.Velocity = Vector2.Zero;
        _actor.SetPhysicsProcess(false);
        _actor.CollisionLayer = 0;

        if (!Respawn) { _actor.QueueFree(); return; }
        _actor.Hide();
        _waiting = true;
        _respawn = System.Math.Max(0.1, RespawnDelay);
        _label.Text = "DOWN — respawning...";
        SetProcess(true);
    }

// =========================================================
// Reset world ownership before returning the player to its surface spawn.
private void Revive()
{
    _waiting = false;

    if (_actor is Player)
        WorldLayerController.Find(this)?.ReturnToSurface();

    _actor.GlobalPosition = _spawn;
    _actor.Velocity = Vector2.Zero;
    _actor.CollisionLayer = _collisionLayer;
    _health.Restore();
    _actor.Show();
    _actor.SetPhysicsProcess(true);

    Camera2D camera = _actor.GetNodeOrNull<Camera2D>("Camera2D");
    if (camera == null) return;
    camera.ResetSmoothing();
    camera.ForceUpdateScroll();
}
    #endregion
}
'@
$Changes['WORLD/Contents/Water/WaterBasinWorld.cs'] = @'
// Registers permanent water basins before terrain heights are cached.
// Water fill can change independently while basin geometry remains intact.
using Godot;
using System.Collections.Generic;
using System;
using System.Diagnostics;

public partial class WaterBasinWorld : Node
{
    #region Basin Data
    public sealed class Basin
    {
        public WaterDefinition Definition;
        public Vector2 Centre;
        public float Phase, RimHeight, Fill = 1f;
        public WaterPatch Patch;

                public bool Resident = true;

        public float SmallestRadius =>
            Mathf.Min(Definition.RadiusTiles.X, Definition.RadiusTiles.Y);
        public float Extent =>
            Mathf.Max(Definition.RadiusTiles.X, Definition.RadiusTiles.Y) * 1.1f;
        public float Rotation =>
            Mathf.DegToRad(Definition.RotationDegrees);
        public float WaterDrop => Definition.BasinDepth -
            (Definition.BasinDepth - Definition.WaterSurfaceDrop) * Fill;
        public float WaterHeight => RimHeight - WaterDrop;

        // =========================================================
        // Measure approximate inward distance from the irregular basin rim.
        public float InwardDistance(Vector2 tile)
        {
            Vector2 offset = (tile - Centre).Rotated(-Rotation);
            Vector2 q = new(offset.X / Definition.RadiusTiles.X,
                offset.Y / Definition.RadiusTiles.Y);
            return (SurfaceGeometry.Edge(q.Angle(), Phase) - q.Length())
                * SmallestRadius;
        }

        // =========================================================
        // Form a smooth bank leading down to a flat basin floor.
        public float DepthAt(Vector2 tile)
        {
            float t = Mathf.Clamp(
                InwardDistance(tile) / Definition.ShoreWidthTiles, 0f, 1f);
            return Definition.BasinDepth * t * t * (3f - 2f * t);
        }

        // =========================================================
        // Calculate the contour where basin floor meets the current water level.
        public float ShoreInsetNormalized()
        {
            float target = WaterDrop / Definition.BasinDepth;
            float low = 0f, high = 1f;
            for (int i = 0; i < 16; i++)
            {
                float middle = (low + high) * 0.5f;
                float depth = middle * middle * (3f - 2f * middle);
                if (depth < target) low = middle;
                else high = middle;
            }
            return (low + high) * 0.5f *
                Definition.ShoreWidthTiles / SmallestRadius;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 1024;
    private const int MaximumPendingCells = 256;

    private sealed class CellJob
    {
        public Basin Result;
        public IEnumerator<int> Work;
    }

    private readonly List<Basin> _basins = new();
    private readonly Dictionary<Vector2I, CellJob> _jobs = new();
    private readonly Queue<Vector2I> _pending = new();

    private GenerationCellCache<Basin> _cells;
    private WorldGenerator _generator;
    private ChunkController _chunks;
    private Node2D _ground;
    private Vector2 _spawnTile;
    private float _spacing;
    private bool _enabled;
    private double _queryBudget;

    public IReadOnlyList<Basin> Basins => _basins;
    public float MaximumDepth { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register this scene-owned service without static world state.
    public override void _EnterTree()
    {
        AddToGroup("water_basins");
        SetProcess(false);
    }

    // =========================================================
    // Resolve the basin service from any actor or world system.
    public static WaterBasinWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("water_basins")
            as WaterBasinWorld;
    }

    // =========================================================
    // Snapshot basin rules and enable budgeted background data preparation.
    public void Initialize(
        WorldGenerator generator, ChunkController chunks, Node2D ground)
    {
        _generator = generator;
        _chunks = chunks;
        _ground = ground;
        _cells = new GenerationCellCache<Basin>(CacheLimit, ReleaseBasin);

        WorldConfig config = WorldConfig.Find(generator);
        _enabled = config.GenerateBiomeBasins;
        _spacing = config.BasinCandidateSpacingTiles;
        _queryBudget = config.BasinQueryBudgetMs;

        if (!float.IsFinite(_spacing) || _spacing < 16f)
            throw new InvalidOperationException(
                "BasinCandidateSpacingTiles must be finite and at least 16.");

        if (!double.IsFinite(_queryBudget) || _queryBudget <= 0)
            throw new InvalidOperationException(
                "BasinQueryBudgetMs must be finite and positive.");

        Player player = generator.GetNode<Player>(
            "../../WorldObjects/Player");
        _spawnTile = IsoGrid.WorldToTile(
            ground.ToLocal(CampaignSession.OriginalSpawnFor(player) ?? player.GlobalPosition), chunks.TileSize);

        if (_enabled)
        {
            HashSet<BiomeBasinProfile> validated = new();

            foreach (BiomeDefinition biome in
                generator.Catalog.GetEnabledBiomes())
            {
                BiomeBasinProfile profile =
                    biome.GetFeature<BiomeBasinProfile>("basins");
                if (profile == null || !profile.Enabled) continue;

                if (validated.Add(profile)) profile.Validate(biome.Id);
                MaximumDepth = Mathf.Max(MaximumDepth, profile.BasinDepth);

                foreach (WaterDefinition template in profile.Templates)
                {
                    float largest = Mathf.Max(
                        template.RadiusTiles.X, template.RadiusTiles.Y) *
                        profile.SizeMultiplierRange.Y * 1.1f +
                        template.ClearanceTiles;

                    if (largest > _spacing * 0.4f)
                        throw new InvalidOperationException(
                            $"Biome '{biome.Id}' needs larger basin cells. " +
                            "Increase BasinCandidateSpacingTiles or reduce basin size.");
                }
            }
        }

        SetProcess(_enabled);
    }
    #endregion

    #region Local Planning
    // =========================================================
    // Prepare an area under the caller's chunk-building budget.
    public IEnumerable<int> PrepareArea(Rect2 area)
    {
        if (!_enabled) yield break;

        using IDisposable protection = PinArea(area);
        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);

            while (!_cells.ContainsKey(cell))
            {
                RequestCell(cell);
                StepCell(cell);
                yield return 0;
            }
        }
    }

    // =========================================================
    // Protect metadata required by a chunk or an incremental placement check.
    public IDisposable PinArea(Rect2 area)
    {
        if (!_enabled) return new GenerationLease(null);
        return _cells.Pin(CellAt(area.Position), CellAt(area.End));
    }

    // =========================================================
    // Use floor division for positive and negative generation coordinates.
    private Vector2I CellAt(Vector2 tile)
    {
        return new Vector2I(
            Mathf.FloorToInt(tile.X / _spacing),
            Mathf.FloorToInt(tile.Y / _spacing));
    }

    // =========================================================
    // Queue one shared job without doing basin validation inside the query.
    private void RequestCell(Vector2I cell)
    {
        if (!_enabled || _cells.ContainsKey(cell) ||
            _jobs.ContainsKey(cell) || _jobs.Count >= MaximumPendingCells)
            return;

        CellJob job = new();
        job.Work = BiomeBasinGenerator.PrepareCell(
            _generator, _chunks, _spawnTile, _spacing, cell,
            basin => job.Result = basin).GetEnumerator();

        _jobs.Add(cell, job);
        _pending.Enqueue(cell);
    }

    // =========================================================
    // Resume one small generation step, publishing only completed decisions.
    private void StepCell(Vector2I cell)
    {
        if (!_jobs.TryGetValue(cell, out CellJob job)) return;
        if (job.Work.MoveNext()) return;

        job.Work.Dispose();
        _jobs.Remove(cell);

        if (_cells.TryAdd(cell, job.Result) && job.Result != null)
            _basins.Add(job.Result);
    }

    // =========================================================
    // Advance cold-query work under its own small frame budget.
    public override void _Process(double delta)
    {
        long started = Stopwatch.GetTimestamp();

        while (_pending.Count > 0 &&
            (Stopwatch.GetTimestamp() - started) * 1000.0 /
            Stopwatch.Frequency < _queryBudget)
        {
            Vector2I cell = _pending.Peek();

            if (!_jobs.ContainsKey(cell))
            {
                _pending.Dequeue();
                continue;
            }

            StepCell(cell);
        }
    }

    // =========================================================
    // Dispose suspended generation work when the world closes.
    public override void _ExitTree()
    {
        foreach (CellJob job in _jobs.Values)
            job.Work.Dispose();

        _jobs.Clear();
        _pending.Clear();
    }

    // =========================================================
    // Release only a basin whose metadata is no longer protected.
    private void ReleaseBasin(Basin basin)
    {
        if (basin == null) return;

        basin.Resident = false;
        _basins.Remove(basin);

        if (GodotObject.IsInstanceValid(basin.Patch) &&
            !basin.Patch.IsQueuedForDeletion())
            basin.Patch.QueueFree();
    }

    // =========================================================
    // Check readiness while requesting missing data without generating it inline.
    public bool IsAreaReady(Rect2 area)
    {
        if (!_enabled) return true;

        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);
        bool ready = true;

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            if (_cells.ContainsKey(cell)) continue;

            RequestCell(cell);
            ready = false;
        }

        return ready;
    }

    // =========================================================
    // Return a completed height or an explicitly temporary uncarved height.
    public bool TryApplyHeight(
        Vector2 tile, float originalHeight, out float height)
    {
        height = originalHeight;
        if (!_enabled) return true;

        Vector2I cell = CellAt(tile);

        if (!_cells.TryGetValue(cell, out Basin basin))
        {
            RequestCell(cell);
            return false;
        }

        if (basin != null) height -= basin.DepthAt(tile);
        return true;
    }
    #endregion

    // =========================================================
    // Preserve existing callers without completing cold generation synchronously.
    public float ApplyHeight(Vector2 tile, float originalHeight)
    {
        TryApplyHeight(tile, originalHeight, out float height);
        return height;
    }

    // =========================================================
    // Query completed basin data, requesting unchecked cells for later frames.
    public Basin GetBasinAt(Vector2 tile)
    {
        if (!_enabled) return null;

        Vector2I cell = CellAt(tile);
        if (!_cells.TryGetValue(cell, out Basin basin))
        {
            RequestCell(cell);
            return null;
        }

        return basin != null && basin.InwardDistance(tile) > 0f
            ? basin : null;
    }

    // =========================================================
    // Keep unchecked footprints unavailable instead of assuming they are dry.
    public bool Overlaps(Vector2 centre, float radius)
    {
        if (!_enabled) return false;

        Rect2 area = new(
            centre - Vector2.One * radius,
            Vector2.One * (radius * 2f));

        if (!IsAreaReady(area)) return true;

        Vector2I first = CellAt(area.Position);
        Vector2I last = CellAt(area.End);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            _cells.TryGetValue(new Vector2I(x, y), out Basin basin);
            if (basin == null) continue;

            float separation = basin.Extent + radius;
            if (centre.DistanceSquaredTo(basin.Centre) <
                separation * separation)
                return true;
        }

        return false;
    }

    // =========================================================
// Reserve the permanent basin against an object's logical ground footprint.
// Uses a conservative tile-space radius, independently of current water fill.
public bool OverlapsWorldFootprint(
    Vector2 globalPoint, Vector2 footprint, Vector2 padding)
{
    Vector2 centre = IsoGrid.WorldToTile(
        _ground.ToLocal(globalPoint), _chunks.TileSize);

    Vector2 half = footprint.Abs() * 0.5f + padding.Abs();
    float radius = 0f;

    for (int y = -1; y <= 1; y += 2)
    for (int x = -1; x <= 1; x += 2)
    {
        Vector2 corner = globalPoint +
            new Vector2(half.X * x, half.Y * y);

        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(corner), _chunks.TileSize);

        radius = Mathf.Max(radius, centre.DistanceTo(tile));
    }

    return Overlaps(centre, radius);
}
}
'@
$Changes['WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs'] = @'
// Plans shared surface/cave entrances from absolute seeded coordinates.
// Sparse candidate spacing guarantees separation independently of discovery order.
using Godot;
using System;
using System.Collections.Generic;

public sealed class CaveEntrancePlanner
{
    #region State
    private const int CacheLimit = 1024;

    private readonly Node _world;
    private readonly ChunkController _chunks;
    private readonly WorldConfig _config;
    private readonly Node2D _ground;
    private readonly WaterBasinWorld _basins;
    private readonly CaveSurfaceSampler _sampler;
    private readonly GenerationCellCache<WorldLayerConnection> _cells;
    private CaveWorld _cave;
    private readonly float _floorElevation;
    private readonly float _tunnelLength;

    private readonly int _offset, _stride;
    private readonly float _pitch, _reach, _clearTiles;
    private readonly Vector2 _spawn;

    public CaveGenerationSettings Settings { get; }
    public float HubX => Settings.EntranceTunnelLengthTiles + 8f;
    #endregion

    #region Construction
    // =========================================================
// Reserve a shared chamber lattice large enough for all cave biome profiles.
public CaveEntrancePlanner(
    Node world, ChunkController chunks, CaveGenerationSettings settings,
    float floorElevation)
{
    _world = world;
    _floorElevation = floorElevation;
    _chunks = chunks;
    _config = WorldConfig.Find(world);
    _ground = world.GetNode<Node2D>("GroundChunks");
    _basins = WaterBasinWorld.Find(world);
    _sampler = new CaveSurfaceSampler(world, chunks);
    Player player = world.GetNode<Player>("WorldObjects/Player");
    _spawn = CampaignSession.OriginalSpawnFor(player) ?? player.GlobalPosition;

    Settings = (CaveGenerationSettings)settings.Duplicate();
    Settings.Validate();
    _tunnelLength = WorldLayerConnection.LengthFor(_config, Settings.EntranceTunnelLengthTiles);

    float minimum = _config.MinimumCaveHoleDistanceTiles;
    if (!float.IsFinite(minimum) || minimum < 64f)
        throw new InvalidOperationException(
            "Minimum cave-hole distance must be at least 64 tiles.");

    float maximumRadius = Settings.MaximumChamberRadius();
    float maximumWidth = Settings.MaximumTunnelWidth();

    _offset = Mathf.CeilToInt(
        maximumRadius + maximumWidth * 0.5f + 4f);

    Settings.CellSpacingTiles = Mathf.Max(
        Settings.CellSpacingTiles,
        Mathf.CeilToInt(_tunnelLength) + _offset +
        Mathf.CeilToInt(maximumWidth * 0.5f) + 6);

    Settings.Validate();

    _reach = new Vector2(
        _offset, _tunnelLength + _offset).Length();

    _stride = Mathf.CeilToInt(
        (minimum + _reach * 2f) / Settings.CellSpacingTiles);
    _pitch = _stride * Settings.CellSpacingTiles;

    float maximumClearance = 0f;
    WorldGenerator generator =
        world.GetNode<WorldGenerator>("Systems/WorldGenerator");

    foreach (BiomeDefinition biome in generator.Catalog.GetEnabledBiomes())
    {
        CaveHoleProfile profile =
            biome.GetFeature<CaveHoleProfile>("cave_holes");
        if (profile == null || !profile.Enabled) continue;

        profile.Validate();
        maximumClearance = Mathf.Max(maximumClearance, profile.ClearRadius);
    }

    _clearTiles = maximumClearance * Mathf.Sqrt(
        2f / (chunks.TileSize.X * chunks.TileSize.X) +
        2f / (chunks.TileSize.Y * chunks.TileSize.Y)) + 3f;

    _cells = new GenerationCellCache<WorldLayerConnection>(CacheLimit, hole =>
    {
        if (hole != null) WorldLayerRuntime.Find(_world)?.Connections.Remove(hole);
    });
}
    #endregion

    #region Planning

        // =========================================================
    // Use the same candidate rectangle for preparation and lifetime protection.
    private void CandidateRange(
        Rect2 area, out Vector2I first, out Vector2I last)
    {
        Rect2 nearby = area.Grow(
            _config.CaveDiscoveryRadiusTiles + _reach);

        first = new Vector2I(
            Mathf.FloorToInt((nearby.Position.X - HubX) / _pitch),
            Mathf.FloorToInt(nearby.Position.Y / _pitch));

        last = new Vector2I(
            Mathf.CeilToInt((nearby.End.X - HubX) / _pitch),
            Mathf.CeilToInt(nearby.End.Y / _pitch));
    }

    // =========================================================
    // Protect entrance decisions used by the requested chunk and discovery buffer.
    public IDisposable PinArea(Rect2 area)
    {
        CandidateRange(area, out Vector2I first, out Vector2I last);
        return _cells.Pin(first, last);
    }

        // =========================================================
    // Prepare shared entrances while protecting unfinished placement checks.
    public IEnumerable<int> PrepareArea(Rect2 area, CaveWorld cave)
    {
        _cave = cave;
        using IDisposable protection = PinArea(area);

        CandidateRange(area, out Vector2I first, out Vector2I last);

        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            if (_cells.ContainsKey(cell)) continue;
            yield return 0;

            Vector2I anchor = cell * _stride;
            Vector2 room = new(
                HubX + anchor.X * Settings.CellSpacingTiles,
                anchor.Y * Settings.CellSpacingTiles);

            uint hash = IsoGrid.Hash(
                x, y, _chunks.WorldSeed ^ Settings.SeedOffset ^ 0xCA7E021u);

            float sideX = (hash & 1u) == 0 ? -1f : 1f;
            float sideY = (hash & 2u) == 0 ? -1f : 1f;

            Vector2 mouth = room + new Vector2(
                sideX * _offset,
                sideY * (_tunnelLength + _offset));
            // Align mouths to the collision tile lattice while keeping ramp length continuous.
            mouth = new Vector2(Mathf.Round(mouth.X), Mathf.Round(mouth.Y));
            Vector2 direction = sideY > 0f ? Vector2.Up : Vector2.Down;

            Vector2 point = _ground.ToGlobal(
                IsoGrid.TileToWorld(mouth, _chunks.TileSize));
            WorldLayerConnection hole = null;

            if (point.DistanceSquaredTo(_spawn) >
                _chunks.SpawnClearRadius * _chunks.SpawnClearRadius)
            {
                Rect2 waterArea = new(
                    mouth - Vector2.One * _clearTiles,
                    Vector2.One * (_clearTiles * 2f));
                waterArea = waterArea.Grow(2f);

                using IDisposable waterProtection = _basins.PinArea(waterArea);

                foreach (int step in _basins.PrepareArea(waterArea))
                    yield return step;

                CaveSurfaceSampler.Result result = new();
                foreach (int step in _sampler.Evaluate(
                    point, direction, false, result))
                    yield return step;

                if (result.Accepted &&
                    result.RimHeight > _floorElevation + 32f)
                {
                    hole = new WorldLayerConnection(
                        $"C_{x}_{y}", WorldLayerId.Surface, cave.LayerId, mouth, direction, point,
                        result.RimHeight, _tunnelLength,
                        anchor)
                    {
                        ClearRadius = result.ClearRadius
                    };
                }
            }

            if (!_cells.TryAdd(cell, hole)) continue;

            if (hole != null)
            {
                WorldLayerRuntime.Find(_world).Connections.Register(hole);
                GD.Print($"[Caves] {hole.Id}: {hole.UpperPosition}");
            }
        }
    }

    // =========================================================
    // Supply only nearby registered mouths to floor and elevation sampling.
    public IEnumerable<WorldLayerConnection> Nearby(Vector2 tile)
    {
        int firstX = Mathf.FloorToInt((tile.X - HubX - _reach - 4f) / _pitch);
        int lastX = Mathf.CeilToInt((tile.X - HubX + _reach + 4f) / _pitch);
        int firstY = Mathf.FloorToInt((tile.Y - _reach - 4f) / _pitch);
        int lastY = Mathf.CeilToInt((tile.Y + _reach + 4f) / _pitch);

        for (int y = firstY; y <= lastY; y++)
        for (int x = firstX; x <= lastX; x++)
            if (_cells.TryGetValue(new Vector2I(x, y), out WorldLayerConnection hole) &&
                hole != null)
                yield return hole;
    }
    #endregion


}
'@
$Changes['UI/Menus/Pause/PauseMenu.cs'] = @'
// Pauses gameplay while its own UI remains active; save support binds separately.
using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
    #region Configuration
    [ExportGroup("Layout")]
    [Export] public float PanelWidth { get; set; } = 440f;
    #endregion

    #region State
    private PlayerInput _controls;
    private InputModes _modes;
    private Control _screen;
    private Button _resume, _save;
    private AcceptDialog _message;
    private ConfirmationDialog _confirm;
    private Action _saveAction, _exitAction;
    private bool _open;
    private Input.MouseModeEnum _previousMouse;
    public bool IsOpen => _open;
    #endregion

    #region Setup
    // =========================================================
    // Attach once per player without editing every playable world scene.
    public static PauseMenu Attach(Player player, PlayerInput controls)
    {
        PauseMenu existing = player.GetNodeOrNull<PauseMenu>("PauseMenu");
        if (existing != null) return existing;

        PauseMenu menu = GD.Load<PackedScene>(
            "res://UI/Menus/Pause/PauseMenu.tscn")
            .Instantiate<PauseMenu>();

        menu.Name = "PauseMenu";
        menu._controls = controls;
        menu._modes = InputModes.For(player);
        player.AddChild(menu);
        return menu;
    }

    // =========================================================
    // Only this menu branch ignores the scene-tree pause state.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 200;
        BuildUi();
        _screen.Hide();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Enable saving only when the real persistent saver is connected.
    public void BindSave(Action saveAction)
    {
        _saveAction = saveAction;
        if (_save == null) return;

        _save.Disabled = saveAction == null;
        _save.TooltipText = saveAction == null
            ? "Campaign saving is unavailable in this scene."
            : "Partial save: surface world, player, inventory and crafting only.";
    }
    #endregion

    #region Input and Pause
    // =========================================================
    // Let existing interfaces consume ESC before opening pause.
    public override void _UnhandledInput(InputEvent input)
    {
        if (_controls == null || !PlayerInput.IsPauseRequest(input))
            return;

        if (!_open && (!_modes.GameplayAllowed ||
            GetTree().Paused || GetViewport().GuiIsDragging()))
            return;

        GetViewport().SetInputAsHandled();

        if (_open) ResumeGame();
        else Open();
    }

    // =========================================================
    // Stop player actions, claim input, then pause the scene tree.
    private void Open()
    {
        _open = true;
        _previousMouse = Input.MouseMode;
        _modes.Push(this, PlayerInputMode.Pause);
        _controls.Suspend();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        _screen.Show();
        GetTree().Paused = true;
        _resume.GrabFocus();
    }

    // =========================================================
    // Release ownership and prevent held-input retriggers.
    private void ResumeGame()
    {
        if (!_open) return;

        _message.Hide();
        _confirm.Hide();
        _exitAction = null;
        _screen.Hide();
        _modes.Release(this);
        _controls.Resume();

        Input.MouseMode = _previousMouse;
        _open = false;
        GetTree().Paused = false;
    }

    // =========================================================
    // Never leave the tree paused if the owning player is removed.
    public override void _ExitTree()
    {
        if (!_open) return;

        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);

        GetTree().Paused = false;
        Input.MouseMode = _previousMouse;
    }
    #endregion

    #region Layout
    // =========================================================
    // Build a blocking overlay with the existing shared button styles.
    private void BuildUi()
    {
        _screen = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        AddChild(_screen);
        _screen.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        ColorRect dim = new()
        {
            Color = new Color(0.015f, 0.035f, 0.05f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        _screen.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        CenterContainer centre = new();
        _screen.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0)
        };

        panel.AddThemeStyleboxOverride(
            "panel", UIButtonFactory.Box(
                UIButtonFactory.Ink, UIButtonFactory.Accent));

        centre.AddChild(panel);

        VBoxContainer contents = new();
        contents.AddThemeConstantOverride("separation", 12);
        panel.AddChild(contents);
        contents.AddChild(UIButtonFactory.Label("PAUSED", 30));

        _resume = AddButton(contents, "Resume", ResumeGame);
        _save = AddButton(contents, "Save Game", SaveGame);

        AddButton(contents, "Options", () => ShowMessage(
            "Options", "Options will be added later."));

        AddButton(contents, "Exit to Main Menu",
            () => ConfirmExit(false));

        AddButton(contents, "Exit Game",
            () => ConfirmExit(true));

        AddButton(contents, "About", () => ShowMessage(
            "About", "A science-fiction survival world.\n" +
            "About content is a placeholder."));

        _message = new AcceptDialog { Exclusive = true };
        AddChild(_message);
        UIButtonFactory.Apply(_message.GetOkButton());

        _confirm = new ConfirmationDialog
        {
            Exclusive = true,
            Title = "Leave game?"
        };

        AddChild(_confirm);
        _confirm.GetOkButton().Text = "LEAVE";
        UIButtonFactory.Apply(_confirm.GetOkButton());
        UIButtonFactory.Apply(_confirm.GetCancelButton());

        _confirm.Confirmed += CompleteExit;
        _confirm.Canceled += () => _exitAction = null;
        BindSave(_saveAction);
    }

    // =========================================================
    // Share button construction, focus handling and styling.
    private static Button AddButton(
        VBoxContainer parent, string text, Action action)
    {
        Button button = UIButtonFactory.Create(text, action);
        parent.AddChild(button);
        return button;
    }
    #endregion

    #region Actions
    // =========================================================
    // Invoke only an explicitly connected persistent saver.
    private void SaveGame()
    {
        if (_saveAction == null) return;

        try
        {
            _saveAction();
            ShowMessage("Partial campaign saved", "World and player saved to your selected profile.\n" +
                CampaignSession.SavedPositionFor(this) + "\n" +
                "Harvesting, world items, containers, buildings and entities are not saved yet.");
        }
        catch (Exception error)
        {
            ShowMessage("Save failed", error.Message);
        }
    }

    // =========================================================
    // Ask before leaving unsaved gameplay.
    private void ConfirmExit(bool quit)
    {
        _exitAction = quit
            ? () => GetTree().Quit()
            : () => MenuNavigation.Open(
                this, ProfileStore.Selected == null
                    ? MenuNavigation.BootScene
                    : MenuNavigation.MainScene);

        _confirm.DialogText =
            "Leave this game? Any unsaved progress will be lost.";

        _confirm.PopupCentered(new Vector2I(460, 180));
    }

    // =========================================================
    // Keep gameplay paused if returning to a menu fails.
    private void CompleteExit()
    {
        Action action = _exitAction;
        _exitAction = null;

        try { action?.Invoke(); }
        catch (Exception error)
        {
            ShowMessage("Could not leave", error.Message);
        }
    }

    // =========================================================
    // Show placeholders or errors while gameplay remains paused.
    private void ShowMessage(string title, string text)
    {
        _message.Title = title;
        _message.DialogText = text;
        _message.PopupCentered(new Vector2I(460, 180));
    }
    #endregion
}
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '94042028bf1d034387f24f2c5b7e9e613e7077e484bf4139339172bb088929a6'
$Expected['WORLD/Chunks/ChunkController.cs'] = 'bf84571202b0b02ac212807ce3e3a6e97f025ac6bc2ef8bbb367c38a51a40194'
$Expected['COMBAT/CombatLife.cs'] = '1c814cdece92fd7cb44bca74df421f944960a2362f788f926b75b912fc5c9729'
$Expected['WORLD/Contents/Water/WaterBasinWorld.cs'] = '4ce13b6d90fdf7d8bae66975230d8421a45b70a69c0ba9ba6be9e12efe42575b'
$Expected['WORLD/Generation/Caves/Surface/CaveEntrancePlanner.cs'] = 'b0eab53b9129d15247b0e38577c9f47ed8a7a4d3888f54bb07c312e9df96d6b7'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = '21a5b97654d2aad8b16f4748ddc8eebaa8a90ebdf79fcaf361292686520df166'
# Validate all dependencies and targets before touching any project file.
$Conflicts = New-Object System.Collections.Generic.List[string]
$Pending = New-Object System.Collections.Generic.List[string]
foreach ($Relative in $Expected.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (!(Test-Path $Path)) { $Conflicts.Add("Missing: $Relative"); continue }
    $Hash = Digest ([IO.File]::ReadAllText($Path))
    $AlreadyUpdated = $Changes.Contains($Relative) -and $Hash -eq (Digest $Changes[$Relative])
    if ($Hash -ne $Expected[$Relative] -and !$AlreadyUpdated) { $Conflicts.Add("Changed since reviewed push: $Relative") }
}
foreach ($Relative in $Changes.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        if ((Digest ([IO.File]::ReadAllText($Path))) -eq (Digest $Changes[$Relative])) { continue }
        if (!$Expected.ContainsKey($Relative)) { $Conflicts.Add("Existing new-file destination: $Relative"); continue }
    }
    $Pending.Add($Relative)
}
if ($Conflicts.Count) { throw ($Conflicts -join "`n") }
if (!$Pending.Count) { Write-Host 'These passes are already installed. No files changed.'; return }
Write-Host ('Files to install/update: ' + $Pending.Count)
$Pending | ForEach-Object { Write-Host ('  ' + $_) }
if ($Preview) { Write-Host 'Preview complete. No files changed.'; return }
$Backup = Join-Path $ProjectRoot ('.save-pass-backups/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($Backup) | Out-Null
$Original = @{}
# Backups use .txt so the C# compiler cannot compile duplicate scripts.
foreach ($Relative in $Pending) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        $Original[$Relative] = [IO.File]::ReadAllBytes($Path)
        $Copy = Join-Path $Backup ($Relative + '.before.txt')
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Copy)) | Out-Null
        [IO.File]::WriteAllBytes($Copy, $Original[$Relative])
    }
}
$Written = New-Object System.Collections.Generic.List[string]
try {
    foreach ($Relative in $Pending) {
        $Path = Join-Path $ProjectRoot $Relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
        $Written.Add($Relative)
        [IO.File]::WriteAllText($Path, $Changes[$Relative] + "`n", $Utf8)
    }
}
catch {
    foreach ($Relative in $Written) {
        $Path = Join-Path $ProjectRoot $Relative
        if ($Original.ContainsKey($Relative)) { [IO.File]::WriteAllBytes($Path, $Original[$Relative]) }
        elseif (Test-Path $Path) { Remove-Item -LiteralPath $Path -Force }
    }
    throw
}
Write-Host ('Installed. Original files backed up in: ' + $Backup)
Write-Host 'Reopen Godot, build C#, and use Continue with the same profile.'
Write-Host 'Walk away, Save Game, note the displayed X/Y, quit, then Continue.'
Write-Host 'Existing campaign files are unchanged. Full object persistence remains a later pass.'
