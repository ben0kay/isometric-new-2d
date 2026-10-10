powershell -ExecutionPolicy Bypass -File .\Apply-LayerConnections-Pass3.ps1# Installs Layer Connection Restore Pass 3 against fc09adf. No Git operations.
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
if (!(Test-Path (Join-Path $ProjectRoot 'SYSTEMS/Saving/WorldObjectSaves.cs'))) {
    throw 'This installer requires the existing project save foundation.'
}
$Changes = [ordered]@{}
$Expected = @{}
$Changes['SYSTEMS/Saving/CampaignSession.cs'] = @'
// Coordinates world/player restoration and the changed-resource save section.
using Godot;
using System;
using System.Diagnostics;
using System.IO;

public partial class CampaignSession : Node
{
    private CampaignData _data;
    private bool _restoring, _finished, _layerRestored;
    private System.Collections.Generic.IEnumerator<int> _destinationPlan;
    private IDisposable _destinationPin;
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
            if (string.IsNullOrEmpty(data.Player.Layer) ||
                !float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
                !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY))
                throw new InvalidDataException("Saved player coordinates or layer are invalid.");
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
                WorldConfig.Find(world).GetLayerCatalog().Get(data.Player.Layer);
                // Reconstruct the seeded surface entrance before preparing underground terrain.
                Vector2 start = data.Player.Layer == WorldLayerId.Surface
                    ? new Vector2(data.Player.X, data.Player.Y)
                    : new Vector2(data.Player.SurfaceEntranceX, data.Player.SurfaceEntranceY);
                player.Position = world.GetNode<Node2D>("WorldObjects").ToLocal(start);
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
            EntityDeaths deaths = new() { Name = "EntityDeaths" };
            deaths.Initialize(data, world);
            world.AddChild(deaths);
            EntitySaves entities = new() { Name = "EntitySaves" };
            entities.Initialize(data, world);
            world.AddChild(entities);
            ResourceChanges resources = new() { Name = "ResourceChanges" };
            resources.Initialize(data);
            world.AddChild(resources);
            WorldObjectSaves objects = new() { Name = "WorldObjectSaves" };
            objects.Initialize(data, world);
            world.AddChild(objects);
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
    // Keep automatic crossings frozen until campaign restoration has completed.
    public static bool IsLoadingFor(Node context) => FindCampaign(context) is CampaignSession campaign && !campaign._finished;

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
        try { WorldObjectSaves.Find(this).RestoreBuildings(_world); }
        catch (Exception error)
        {
            _world.GetNode("WorldObjects").ProcessMode = ProcessModeEnum.Disabled;
            FailLoad(error);
        }
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
                if (!PrepareSavedDestination(position))
                {
                    if (_startup.Elapsed.TotalSeconds > 120)
                        throw new IOException("Saved layer or destination could not be prepared.");
                    return;
                }
                RestorePlayer();
                _player.GlobalPosition = position;
                _player.Velocity = Vector2.Zero;
                _clock.RestoreSave(_data.WorldSeconds);
            }
            else _clock.RestoreSave(0);
            WorldObjectSaves.Find(this).RestoreDrops(this);
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
    // Reconstruct local seeded routes at any saved depth, retaining the surface return entrance.
    private bool PrepareSavedDestination(Vector2 position)
    {
        string layer = _data.Player.Layer;
        if (layer == WorldLayerId.Surface)
        {
            _player.GlobalPosition = position;
            if (!_chunks.PrepareDestination(position)) return false;
            if (!_chunks.IsNavigationPointAvailable(position, 12f))
                throw new InvalidDataException("Saved position is no longer on available surface terrain.");
            return true;
        }
        WorldLayerController controller = WorldLayerController.Find(this)
            ?? throw new InvalidDataException("This campaign has no underground layer runtime.");
        if (!_layerRestored)
        {
            Vector2 entrance = new(_data.Player.SurfaceEntranceX, _data.Player.SurfaceEntranceY);
            _player.GlobalPosition = entrance;
            if (!_chunks.PrepareDestination(entrance)) return false;
            WorldLayerConnection route = null;
            foreach (WorldLayerConnection connection in controller.Worlds.Connections.All)
                if (connection.Id == _data.Player.SurfaceEntranceId && connection.UpperLayer == WorldLayerId.Surface)
                    route = connection;
            if (route == null || route.LowerLayer != controller.Worlds.SurfaceEntranceLayerId)
                throw new InvalidDataException("The saved surface entrance could not be reconstructed.");
            // Plan the saved area's endpoints before terrain or arrival-ramp sampling.
            CaveWorld destination = controller.Worlds.GetUnderground(layer);
            InfiniteWorldGeneration generation = InfiniteWorldGeneration.Find(this);
            if (_destinationPlan == null)
            {
                Vector2 tile = destination.WorldToTile(position);
                Rect2 area = new(tile - Vector2.One * 16f, Vector2.One * 32f);
                _destinationPin = generation.PinArea(area, layer);
                _destinationPlan = generation.PrepareArea(area, layer).GetEnumerator();
            }
            Stopwatch budget = Stopwatch.StartNew();
            while (_destinationPlan.MoveNext())
                if (budget.Elapsed.TotalMilliseconds >= 2) return false;
            _destinationPlan.Dispose(); _destinationPlan = null;
            controller.RestoreCampaignLayer(layer, position, route);
            _layerRestored = true;
        }
        _player.GlobalPosition = position;
        CaveWorld cave = controller.Worlds.GetUnderground(layer);
        if (!cave.Streaming.AreaReady(position)) return false;
        if (!controller.Worlds.IsAvailable(layer, position, 14f))
            throw new InvalidDataException("Saved position is no longer on available underground terrain.");
        _destinationPin?.Dispose(); _destinationPin = null;
        return true;
    }

    // =========================================================
    // Release temporary destination metadata on cancellation or load failure.
    public override void _ExitTree()
    {
        _destinationPlan?.Dispose(); _destinationPlan = null;
        _destinationPin?.Dispose(); _destinationPin = null;
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
    // Capture supported sections while paused and grounded in the exact active layer.
    private void Save()
    {
        if (!_finished || !GetTree().Paused || ProfileStore.Selected?.Id != _data.ProfileId)
            throw new InvalidOperationException("No paused campaign belongs to the selected profile.");
        string layer = WorldLayerMember.For(_player);
        WorldLayerController controller = WorldLayerController.Find(this);
        WorldLayerConnection entrance = controller?.LastSurfaceConnection;
        if (layer != WorldLayerId.Surface && (entrance == null || entrance.UpperLayer != WorldLayerId.Surface ||
            entrance.LowerLayer != controller.Worlds.SurfaceEntranceLayerId ||
            controller.Worlds.IsAvailable(layer, _player.GlobalPosition, 14f) != true))
            throw new InvalidOperationException("Save on ready underground ground with a known natural surface return entrance.");
        if (layer != WorldLayerId.Surface)
        {
            CaveWorld cave = controller.Worlds.GetUnderground(layer);
            Vector2 tile = cave.WorldToTile(_player.GlobalPosition);
            foreach (WorldLayerConnection connection in controller.Worlds.Connections.ForLayer(layer))
                if (connection.Id.StartsWith("TEST_", StringComparison.Ordinal) && connection.SampleArea.HasPoint(tile))
                    throw new InvalidOperationException("Move away from the temporary debug corridor before saving; use natural corridors for persistence tests.");
        }
        if (_player.IsAirborne || !_player.GetNode<Health>("Systems/Health").IsAlive)
            throw new InvalidOperationException("Save while alive and standing on the ground.");
        PlayerSaveData saved = new()
        {
            X = _player.GlobalPosition.X, Y = _player.GlobalPosition.Y,
            Layer = layer,
            SurfaceEntranceId = layer == WorldLayerId.Surface ? "" : entrance.Id,
            SurfaceEntranceX = layer == WorldLayerId.Surface ? 0 : entrance.UpperPosition.X,
            SurfaceEntranceY = layer == WorldLayerId.Surface ? 0 : entrance.UpperPosition.Y,
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
        ResourceChanges.Find(this).Capture(_data);
        WorldObjectSaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureResourceDefinitions(_data);
        EntityDeaths.Find(this).Capture(_data);
        EntitySaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureObjectDefinitions(_data);
        CampaignRecipe.CaptureEntityDefinitions(_data);
        _data.Version = 6;
        _data.Coverage = "world-player-resources-objects-entities-layer";
        CampaignStore.Write(_data);
        GD.Print($"[CampaignSave] Profile {_data.ProfileId}: " +
            $"({saved.X}, {saved.Y}), original spawn ({_data.SpawnX}, {_data.SpawnY}).");
    }

    // =========================================================
    // Keep failed restoration frozen and show an exit route without saving over it.
    private void FailLoad(Exception error)
    {
        _destinationPlan?.Dispose(); _destinationPlan = null;
        _destinationPin?.Dispose(); _destinationPin = null;
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
$Changes['WORLD/Layers/WorldLayerController.cs'] = @'
// Coordinates bidirectional layer connections without owning terrain generation.
// Surface pausing, landing checks and connection metadata have focused helpers.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldLayerController : Node
{
    #region Configuration
    [ExportGroup("Connection Loading")]
    [Export(PropertyHint.Range, "16,256,8")]
    public float ExitPreloadDistanceTiles { get; set; } = 96f;
    #endregion

    #region State
    public string Current => Worlds?.ActiveLayer ?? WorldLayerId.Surface;
    public int Epoch { get; private set; }
    public WorldLayerRuntime Worlds { get; private set; }
    public WorldLayerDefinition CurrentDefinition => _config.GetLayerCatalog().Get(Current);
    public WorldLayerConnection LastSurfaceConnection { get; private set; }
    public event Action<string, string, WorldLayerConnection> LayerChanged;

    private Player _player;
    private Health _health;
    private Camera2D _camera;
    private Vector2 _cameraPosition;
    private WorldConfig _config;
    private ChunkController _surfaceChunks;
    private WorldLayerSurface _surface;
    private WorldLayerLanding _landing;
    private InfiniteWorldGeneration _generation;
    private Label _status;
    private WorldLayerConnection _pending, _arrival;
    private bool _ready, _arrivalDeparted;
    private Vector2 _destination;
    private double _landingTimer, _hudTimer;
    private string _previewLayer;
    private float _previewOpacity;
    private readonly Dictionary<WorldLayerConnection, IDisposable> _routeLeases = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Register one controller without running before its dependencies exist.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_controller");
        SetProcess(false);
    }

    // =========================================================
    // Bind shared player/services and create small runtime helpers.
    public void Configure(Node world, Player player, WorldLayerRuntime worlds)
    {
        Worlds = worlds; _player = player;
        _health = player.GetNode<Health>("Systems/Health");
        _config = WorldConfig.Find(world);
        _surfaceChunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _generation = InfiniteWorldGeneration.Find(world);
        _camera = player.GetNode<Camera2D>("Camera2D");
        _cameraPosition = _camera.Position;
        _surface = new WorldLayerSurface { Name = "SurfacePresentation" };
        AddChild(_surface);
        _surface.Configure(world, player);
        _landing = new WorldLayerLanding(worlds, world);
        CanvasLayer hud = new() { Name = "LayerHUD", Layer = 40 };
        AddChild(hud);
        _status = new Label { Position = new Vector2(16, 200) };
        _status.AddThemeColorOverride("font_color", new Color("#8be4cf"));
        hud.AddChild(_status);
        WorldLayerPursuit.Ensure(this, world);
        SetProcess(true);
    }

    // =========================================================
    // Advance preparation before checking the small physical crossing boundary.
    public override void _Process(double delta)
    {
        if (!GodotObject.IsInstanceValid(_player) || CampaignSession.IsLoadingFor(this)) return;
        if (Current != WorldLayerId.Surface) _surfaceChunks.RetireUnusedChunks();
        _surface.Drain();
        _landingTimer -= delta; _hudTimer -= delta;
        if (!_health.IsAlive && Current != WorldLayerId.Surface) ReturnToSurface();

        WorldLayerConnection approach = FindApproach();
        if (approach != _pending)
        {
            CancelPending();
            _pending = approach;
            _landingTimer = 0;
        }
        if (_pending != null && !_ready && _landing.Prepare(_pending, Current))
        {
            _surface.Drain();
            if (_landingTimer <= 0)
            {
                _landingTimer = 0.25;
                _ready = _landing.TryReady(_pending, Current, out _destination);
            }
        }
        if (_pending != null && _ready && _health.IsAlive &&
            InputModes.For(_player).GameplayAllowed && AtCrossing(_pending, _player.GlobalPosition))
            Transfer(_pending, _destination);
        UpdatePresentation(delta);
    }

    // =========================================================
    // Release protected metadata and restore the shared camera on teardown.
    public override void _ExitTree()
    {
        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
        _routeLeases.Clear();
        if (GodotObject.IsInstanceValid(_camera)) _camera.Position = _cameraPosition;
    }
    #endregion

    #region Connection Selection
    // =========================================================
    // Prefer a connection underfoot, otherwise preload the nearest known endpoint.
    private WorldLayerConnection FindApproach()
    {
        CaveWorld primary = Worlds.SurfaceUnderground;
        Vector2 tile = primary.WorldToTile(_player.GlobalPosition);
        WorldLayerConnection best = null;
        float distance = ExitPreloadDistanceTiles * ExitPreloadDistanceTiles;
        foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
        {
            Vector2 local = connection.Coordinates(tile);
            if (connection == _arrival && Current == connection.LowerLayer && !_arrivalDeparted)
            {
                if (local.X > 1f || local.X < -0.1f || Mathf.Abs(local.Y) > 2f)
                    _arrivalDeparted = true;
                else continue;
            }
            // Ascending connections can be approached along their full ramp.
            bool onRamp = Current == connection.LowerLayer &&
                local.X >= -1.5f && local.X <= connection.TunnelLength &&
                Mathf.Abs(local.Y) < 1.4f;
            bool atMouth = local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) < 1.4f;
            if (onRamp || atMouth) return connection;
            float candidate = tile.DistanceSquaredTo(connection.MouthTile);
            if (candidate >= distance) continue;
            distance = candidate; best = connection;
        }
        return best;
    }

    // =========================================================
    // Never transfer from a distant preload: cross only at the actual mouth seam.
    private bool AtCrossing(WorldLayerConnection connection, Vector2 position)
    {
        Vector2 local = connection.Coordinates(Worlds.SurfaceUnderground.WorldToTile(position));
        return Mathf.Abs(local.Y) < 1.4f && (Current == connection.UpperLayer
            ? local.X >= 0.05f && local.X <= 0.6f
            : local.X >= -1.5f && local.X <= -0.45f);
    }

    // =========================================================
    // Release only the old destination's temporary preparation request.
    private void CancelPending()
    {
        if (_pending != null)
        {
            string target = _pending.Other(Current);
            if (target != WorldLayerId.Surface) Worlds.CancelPreload(target);
        }
        _pending = null; _ready = false;
    }
    #endregion

    #region Transfer
    // =========================================================
    // Restore ownership/presentation directly without simulating an entrance crossing.
    public void RestoreCampaignLayer(string layer, Vector2 position, WorldLayerConnection surfaceEntrance)
    {
        if (layer == WorldLayerId.Surface || surfaceEntrance == null ||
            surfaceEntrance.UpperLayer != WorldLayerId.Surface ||
            surfaceEntrance.LowerLayer != Worlds.SurfaceEntranceLayerId)
            throw new InvalidOperationException("Underground restoration requires a surface return entrance.");
        Worlds.GetUnderground(layer);
        CancelPending(); HidePreview();
        LastSurfaceConnection = surfaceEntrance;
        HoldRoute(surfaceEntrance);
        _surface.Pause();
        Worlds.ActivateLayer(layer);
        _player.GlobalPosition = position;
        _player.Velocity = Vector2.Zero;
        CaveWorld cave = Worlds.GetUnderground(layer);
        _arrival = cave.TransitionAt(cave.WorldToTile(position));
        _arrivalDeparted = false;
        _surface.SetOpacity(0f);
        Epoch++;
        StopMining();
        _camera.ResetSmoothing();
    }

    // =========================================================
    // Change ownership, collision and movement together once landing is validated.
    private void Transfer(WorldLayerConnection connection, Vector2 position)
    {
        string from = Current, destination = connection.Other(from);
        if (from == WorldLayerId.Surface)
        {
            LastSurfaceConnection = connection;
            _surface.Pause();
        }
        HoldRoute(connection);
        CancelPending();
        HidePreview();
        Worlds.ActivateLayer(destination);
        _player.GlobalPosition = position;
        _player.Velocity = Vector2.Zero;
        _arrival = connection;
        _arrivalDeparted = destination != connection.LowerLayer;
        Epoch++;
        connection.Marker?.QueueRedraw();
        if (destination == WorldLayerId.Surface)
        {
            _surface.Resume();
            foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
            _routeLeases.Clear();
        }
        _camera.ResetSmoothing();
        StopMining();
        WorldLayerPursuit.Find(this)?.PlayerCrossed(connection, destination, _player);
        GD.Print($"[Layers] {from} -> {destination} through {connection.Id}");
        LayerChanged?.Invoke(from, destination, connection);
    }

    // =========================================================
    // Protect the original surface entrance during a multi-depth journey.
    private void HoldRoute(WorldLayerConnection connection)
    {
        if (connection.UpperLayer != WorldLayerId.Surface || _routeLeases.ContainsKey(connection)) return;
        _routeLeases.Add(connection, _generation.PinArea(new Rect2(
            connection.MouthTile - Vector2.One * 4f, Vector2.One * 8f)));
    }

    // =========================================================
    // Preserve death/respawn behaviour without treating every exit as surface.
    public void ReturnToSurface()
    {
        if (Current == WorldLayerId.Surface) return;
        CancelPending(); HidePreview();
        Worlds.ActivateLayer(WorldLayerId.Surface);
        if (LastSurfaceConnection != null)
            _player.GlobalPosition = LastSurfaceConnection.OutsidePosition(Worlds.SurfaceUnderground.TileSize);
        _player.Velocity = Vector2.Zero;
        _surface.Resume();
        foreach (IDisposable lease in _routeLeases.Values) lease?.Dispose();
        _routeLeases.Clear();
        _arrival = null;
        Epoch++;
        _camera.Position = _cameraPosition;
        _camera.ResetSmoothing();
        StopMining();
        // Respawn is a reset, not a traversable shortcut for pursuing actors.
        WorldLayerPursuit.Find(this)?.PlayerCrossed(null, Current, _player);
    }

    // =========================================================
    // Stop active mining across a change of collision world.
    private void StopMining()
    {
        foreach (Node node in _player.GetNode("Systems").GetChildren())
            if (node is MiningEmitter mining) mining.Stop();
    }
    #endregion

    #region Presentation
    // =========================================================
    // Fade only the departure layer while traversing a lower-owned ramp.
    private void UpdatePresentation(double delta)
    {
        string upper = null;
        float target = 0f;
        WorldLayerConnection ramp = null;
        if (Current != WorldLayerId.Surface)
        {
            CaveWorld cave = Worlds.GetUnderground(Current);
            Vector2 tile = cave.WorldToTile(_player.GlobalPosition);
            ramp = cave.TransitionAt(tile);
            if (ramp != null)
            {
                float descent = Mathf.Clamp(ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f);
                upper = ramp.UpperLayer;
                target = Mathf.Clamp(_config.ObstructingSpriteOpacityPercent / 100f, 0f, 1f) * (1f - descent);
            }
            float reference = ramp?.UpperLayer == WorldLayerId.Surface
                ? ramp.RimHeight * (1f - Mathf.Clamp(
                    ramp.Coordinates(tile).X / ramp.TunnelLength, 0f, 1f)) : 0f;
            _camera.Position = _cameraPosition + Vector2.Down *
                (reference - cave.Elevation.SampleWorldHeight(_player.GlobalPosition));
        }
        else _camera.Position = _cameraPosition;

        if (_previewLayer != upper)
        {
            HidePreview(); _previewLayer = upper;
        }
        _previewOpacity = Mathf.MoveToward(_previewOpacity, target,
            (float)delta / Mathf.Max(0.05f, _config.ObstructionFadeSeconds));
        _surface.SetOpacity(Current == WorldLayerId.Surface ? 1f :
            upper == WorldLayerId.Surface ? _previewOpacity : 0f);
        if (upper != null && upper != WorldLayerId.Surface)
            Worlds.GetUnderground(upper).SetPreview(_previewOpacity);

        if (_hudTimer > 0) return;
        _hudTimer = 0.2;
        _surface.Prune();
        foreach (WorldLayerConnection connection in Worlds.Connections.All)
            if (GodotObject.IsInstanceValid(connection.Marker))
                connection.Marker.Visible = Current == connection.UpperLayer ||
                    (Current == connection.LowerLayer && connection == ramp);
        _status.Text = $"{CurrentDefinition.DisplayName}\n" +
            (_pending == null ? "Explore the connected world" :
                $"{_pending.Id}: {(_ready ? "destination ready" : _landing.Status)}") +
            (Current == WorldLayerId.Surface ? "" : " | M map is surface-only");
    }

    // =========================================================
    // Remove departure previews without reactivating their simulation.
    private void HidePreview()
    {
        if (_previewLayer != null && _previewLayer != WorldLayerId.Surface)
            Worlds.GetUnderground(_previewLayer).SetPreview(0f);
        _previewLayer = null; _previewOpacity = 0f;
    }
    #endregion

    #region Movement
    // =========================================================
    // Keep the shared player's footprint inside ready terrain, allowing axis sliding.
    public Vector2 ConstrainVelocity(Vector2 position, Vector2 velocity, double delta)
    {
        if (delta <= 0) return velocity;
        Vector2 motion = velocity * (float)delta;
        if (CanTravel(position, motion)) return velocity;
        bool x = CanTravel(position, new Vector2(motion.X, 0f));
        bool y = CanTravel(position, new Vector2(0f, motion.Y));
        if (x && (!y || Mathf.Abs(motion.X) >= Mathf.Abs(motion.Y))) return new Vector2(velocity.X, 0f);
        return y ? new Vector2(0f, velocity.Y) : Vector2.Zero;
    }

    // =========================================================
    // Hold any connection seam until that exact destination has a safe landing.
    private bool CanTravel(Vector2 position, Vector2 motion)
    {
        int steps = Mathf.Max(1, Mathf.CeilToInt(motion.Length() / 12f));
        if (steps > 64) return false;
        for (int i = 1; i <= steps; i++)
        {
            Vector2 point = position + motion * ((float)i / steps);
            if (Current != WorldLayerId.Surface && !Worlds.IsAvailable(Current, point, 14f)) return false;
            foreach (WorldLayerConnection connection in Worlds.Connections.ForLayer(Current))
                if (AtCrossing(connection, point) && (connection != _pending || !_ready)) return false;
        }
        return true;
    }
    #endregion

    #region Shared Queries
    // =========================================================
    // Resolve the gameplay world's optional layer controller.
    public static WorldLayerController Find(Node context)
    {
        return context == null || !context.IsInsideTree() ? null :
            context.GetTree().GetFirstNodeInGroup("world_layer_controller") as WorldLayerController;
    }

    // =========================================================
    // Route artwork and aiming to the actor's exact elevation provider.
    public static float HeightFor(Node owner, Vector2 point)
    {
        string layer = WorldLayerMember.For(owner);
        if (layer != WorldLayerId.Surface)
            return (WorldLayerRuntime.Find(owner) ?? throw new InvalidOperationException(
                $"No runtime for '{layer}'.")).GetUnderground(layer).Elevation.SampleWorldHeight(point);
        TerrainElevation elevation = owner.GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
        return elevation?.SampleWorldHeight(point) ?? 0f;
    }

    // =========================================================
    // Keep player-created drops in the active world's object root.
    public static Node2D DropRoot(Node context, Node2D surfaceRoot)
    {
        WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
        return runtime == null ? surfaceRoot : runtime.ObjectsFor(runtime.ActiveLayer);
    }
    #endregion
}
'@
$Changes['NOTES/OngoingWork/LayerGenerationConnections.md'] = @'
# Layer Generation and Connections

## Goal

Support any number of defined depths, including future Hell layers. Query seeded world information without loading physical chunks. Reconstruct natural connections from the seed and save gameplay changes separately.

## Rules

- Every layer has a stable ID, biome/generation settings and depth. Future connection rules name their destination layer explicitly; never infer it from names or assume Deep Caverns is the final layer.
- The upper layer owns a downward connection. Either endpoint can discover the same seeded record without visiting the other endpoint first.
- Use a spaced candidate grid with deterministic probability checks. Connection frequency scales candidate probability, not chunk counts or guaranteed entrances. Terrain suitability and spacing still apply.
- The lower endpoint joins the corridor to its own chamber network. Each endpoint uses its own biome and terrain rules.
- Queries sample logical global ground coordinates. They reuse generation services and do not activate layers, build chunks or spawn objects.
- Avoid recursive generation dependencies. Establish base biome data, then independently planned features, then physical content. Surface flowers may consult a planned underground lair before that lair is spawned.
- Keep queries local and caches bounded. Do not scan every depth for every tile or retain physical terrain for unvisited areas.
- Same seed, original spawn, generation settings and generator version reproduce the same base world. Seed alone is not a compatibility guarantee after generation rules change.

## Global Frequency Tuning

`CONFIG/GlobalConfig.cs` exposes `LayerConnectionFrequency` on CONFIG in the Inspector, keyed by the **upper/source layer ID**.

- `surface = 1`: existing surface candidate probability.
- `underground_1 = 0.5`: half the future downward candidate probability.
- `underground_2 = 0.25`: quarter the future downward candidate probability.
- `0` disables new natural downward connections from that layer; `2` doubles probability, capped at 100%. Allowed multiplier range is 0–8.
- A defined layer without an entry defaults to 1. Add an explicit setting for Hell or another new layer when adding its definition. Unknown IDs and invalid multipliers are rejected.
- This is generation tuning: restart after changing it and use a new campaign when assessing a changed layout. Campaign settings restore the tuning captured for that campaign.
- Pass 1 wires the surface setting into the existing entrance sampler. Underground settings are available to queries but do not create deeper connections until Pass 2. Debug connections are unaffected.

## Implementation Stages

1. **Queries and tuning — implemented by this installer.** Shared exact-layer biome query facade reusing current samplers; central frequency dictionary; surface probability integration; preserve current surface behaviour at multiplier 1.
2. **Permanent connection planner — implemented by Pass 2.** Explicit layer-pair rules, seeded stable connection IDs, discovery from either side, bounded planning/caching, valid corridor endpoints and shared spacing rules. Replace reliance on the temporary deep-cavern test without making that debug script permanent save data.
3. **Restore integration — implemented by Pass 3; completes save Pass 6.2.** Reconstruct nearby routes before terrain/player activation, support loading at any defined depth, preserve necessary route discovery/pins and verify travel back through multiple layers. Save only non-reconstructible identities or gameplay modifications.
4. **Return to save Pass 6.3 and Pass 7.** Liquid/basin changes, then combined persistence and menu verification.

## Save Boundaries

Seeded biomes, base chambers and unchanged natural corridors are reconstructed. Player layer/position, harvested vegetation, destroyed resources, entities, items, buildings, boss defeat and changed/player-created connections are persistent gameplay state. Feature/lair planning and flower hooks are future work; Pass 1 supplies biome queries only.

## Checks

Biome queries must match the existing samplers at the same position, work for inactive layers, leave chunk counts/active layer unchanged and reject unknown IDs. Frequency 1 preserves surface decisions; 0 rejects normal surface probability checks; 0.5/2 scale and cap candidate probability. Existing explicit debug probability bypasses remain intact. Test loading unexplored routes from below when the permanent planner is implemented.

Pass 1 automated verification passed: biome equality against existing samplers at positive/negative coordinates for all three currently defined layers; unchanged underground chunk counts/active layer; multiplier scaling/capping, normal surface 0/1 decisions and explicit debug bypass; invalid IDs/values; typed dictionary save-recipe roundtrip and migration of recipes missing the setting. Local visuals and changed-layout exploration still need gameplay checks.


## Pass 2 — Permanent Cave Corridors

- Added explicit DownwardLayerId and candidate chance/spacing/length to layer definitions. Catalog validation requires a defined deeper target, so the graph cannot cycle by depth. No maximum depth or terminal layer name is assumed. Current Upper Caverns targets Deep Caverns; add Hell through its own definition and an incoming target rule later.
- Candidates use stable layer IDs, seed and absolute cells. Either endpoint prepares the same records before chunk geometry. Frequency multiplies the upper layer's base chance; configured spacing is a minimum, increased conservatively to separate complete corridors/room connectors. Results do not depend on exploration order.
- The source chamber joins a cardinal corridor mouth and the lower end joins a chamber in the destination's own network. Source elevation comes from base biome sampling rather than registered ramps. Adjacent permanent depth-pair overlaps resolve from seeded raw candidates and stable ID ordering.
- Existing chunk work budgets/yields and GenerationMetadataLease pinning are reused. Each pair caches at most 512 unused/total-target decisions, permitting protected loaded working sets to exceed that target. Both successful and empty decisions are cached; retired unpinned records unregister markers and endpoints and regenerate later.
- Layer-pair lookup is indexed by layer ID, and corridor footprints have spatial buckets. Floor/elevation sampling checks nearby endpoints rather than scanning every cached corridor on every terrain tile. Eviction removes the associated spatial entries.
- The existing surface entrance planner is retained. DeepCavernsTest is unchanged and remains optional; disable its Enabled property when testing natural deep corridors so the debug route is not confused with the new planner.
- This intentionally changes the generation recipe/resource. Test with a new campaign. Existing saves remain untouched but compatibility checks can refuse the changed generation resources. Restore integration/save Pass 6.2 remains next: saving in deeper layers is still blocked until direct-load routes are handled. Liquids remain 6.3.
- Local test: new campaign, enter Upper Caverns, explore for natural descents, descend to Deep Caverns and return through the same corridor. Test frequency 0 on underground_1 in a separate new campaign, then a higher multiplier. Repeat after chunk retirement. Default candidates are deliberately sparse; a valid chance is not an entrance guarantee in every chunk.
- Automated verification passed: full production compilation including current audio sources; identical fresh-process lower-first versus upper-first corridor records; repeated preparation without duplicates; real landing preparation and controller descent/return; lower connector floor continuity; zero-frequency suppression; cache eviction preserving a pinned route; bounded unused records, local spatial lookup and removal of evicted spatial entries; installer preview/application/idempotence/exact payloads and zero-write conflict rejection. The headless fixture uses a fixed seed/high frequency, substitutes artwork and disables the debug route and automatic crossing process; local walking through seams/visual presentation still needs gameplay verification.


## Pass 3 — Saved Depth Restoration (Save 6.2)

- Manual saving supports ready ground in any defined underground layer while retaining the natural surface return entrance. Deep Caverns is not treated as the last depth. Existing schema 6 stores the exact layer/position and original surface entrance identity; no corridor geometry or exploration history is serialized.
- Continue reconstructs the original surface entrance, then incrementally prepares seeded connection metadata around the saved position before activation/arrival-ramp sampling. A temporary lease protects destination metadata until real chunk streaming has prepared the area; cancellation/failure releases it. Work yields after a 2 ms planning budget. Other route segments regenerate locally when approached from either endpoint, without scanning the journey or loading every intervening depth.
- Save/restore keeps the original spawn, campaign generation settings, surface return pin and existing item/entity/object save sections. Temporary TEST_ corridor footprints are rejected for saving because debug geometry is not deterministic campaign state. Disable DeepCavernsTest for natural-route persistence tests.
- No schema or generation-resource changes in this pass. Saves from the Pass 2 recipe can continue; recipes predating that generation change still follow existing compatibility checks.
- This completes the three planned connection stages. Future feature/lair queries and changed/player-created corridor persistence remain future features. Next save work is 6.3 liquids/basins, then Pass 7 combined verification/menu finishing.
- Automated checks passed: full production compilation; real paused Deep Caverns save; fresh-process Continue restoring the exact deep position/layer, original surface anchor and nearby natural route; real landing/controller return through Upper Caverns to surface; installer preview/application/idempotence and zero-write conflict protection. Headless tests use fixed seed/high frequency and artwork substitutes; automatic walking/visual seams remain a local check.
- Local test: save on natural Deep Caverns ground, close completely, Continue the same profile, verify exact position/items and climb back to Upper Caverns then surface. Repeat far from the original entrance, near a natural ramp, and after chunk retirement; also verify surface and Upper Caverns saves.
'@
$Changes['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = @'
| Pass | Scope | Check before moving on |
|---|---|---|
| **1. Profile-owned save foundation** | Campaign identity, versioned save format with named sections, safe temporary-file replacement and backup recovery. Connect the existing pause Save button and basic New/Continue flow. | Profiles A and B save/load separate campaign metadata; failures preserve the previous save. Clearly mark this as partial persistence. |
| **2. World and player restoration** | Generation configuration, world time/eclipse phase, original spawn, player stats/reserves, inventory/equipment/hotbar and crafting. Establish controlled startup and restoration. | Quit and reload on the surface with the same player state and world layout. |
| **3. Generated resource changes** | Shared generated identities; rocks, trees, plants, ores, ground deposits and consumed/cleared grass. Integrate with chunk regeneration. | Harvest, leave until the chunk unloads, return, then quit/reload: changes remain. |
| **4. Items, containers and structures** | Loose drops, ordinary storage, loot containers, wrecks, buildings and health. Add records where only live nodes exist today. | No lost or duplicated items; empty containers stay empty; buildings survive. |
| **5. Entities and groups** | Population records plus live actors, persistent deaths, transferred actors and relevant group state. Coordinate death rewards with Pass 4. | Damaged, dead and transferred entities restore correctly without duplicate loot or wildlife. |
| **6. Underground restoration and liquids** | Complete exact-depth loading, required connections, basin changes and saves on entrance ramps. | Save/load in Surface, Upper Caverns and Deep Caverns, then traverse back successfully. |
| **7. Complete-save verification and menu finish** | Load Campaign selection, overwrite handling, recovery messages and combined regression checks. | One save restores every supported section across profiles and unloaded chunks. |

## Current Save Progress

- Passes 1–3: implemented; surface position fix confirmed locally.
- Pass 4: installed and verified in the reviewed source. Physical drops, storage/loot, wrecks, structures and health persist. Drop lifetime is stored; the expiry countdown remains future work.
- Pass 5.1: installed; stable origin IDs, deaths and coordinated reward identities.
- Pass 5.2: installed; adds surviving authored/population state, retired records and exact living layer/transfer ownership with lazy restoration near available terrain.
- Pass 5.3: installed; adds logical group membership, formation/roaming state and persistent dissolution. Streaming retirement preserves membership; restored actors cannot duplicate their origin slot.
- Versions 1–5 upgrade to campaign version 6 on Save. Living changes made before this pass cannot be reconstructed. Local gameplay checks remain required after the supplied automated checks.
- Pass 6.1: this installer adds exact player restoration in natural surface-connected caves, including the seeded return entrance. Deeper/custom routes remain gated.
- Next — Pass 6.2: connection/entrance changes and deeper routes; Pass 6.3: liquid/basin changes.
- Then — Pass 7: full combined verification, load-selection/overwrite handling and remaining menu/recovery work.
- Persistence remains staged. Automatic saving/options and actual dropped-item expiry are separate future features. Combat targets/paths/reservations are rebuilt rather than persisted as engine references.

## Save Pass 6.1 — Exact Player Layer Restoration

- Implemented against bfb85d6. Preserve all newer UI, notifications, vegetation and unrelated work.
- Manual Save supports a living, grounded player on available surface or natural surface-connected cave terrain. Underground saves require the surface entrance used for the journey; deeper/custom routes and debug-only placement without that route are gated until Pass 6.2.
- Campaign version 6 stores the exact player layer/global position plus the stable seeded surface return entrance ID/position. Versions 1–5 remain readable and upgrade on the next successful Save. Earlier game versions cannot read version 6.
- Continue freezes player actions and automatic crossings, rebuilds the seeded return entrance, pins its metadata, activates only the saved cave layer and waits for its nearby terrain before restoring inventory/vitals/time and enabling gameplay. Invalid/missing layers, entrances or terrain fail visibly while preserving the existing save; there is no silent surface fallback.
- Respawn remains anchored to the original landing site; returning through the remembered entrance and ordinary death handling retain the existing controller flow. Camera and surface presentation are reset for the restored layer.
- Automated checks passed: full C# compilation including the latest notification sources; real transfer into a generated cave and version 6 capture; fresh-process exact underground position/layer/health/return-route restoration; underground resave, return to surface and surface resave; version 5 surface cold load/version 6 upgrade; invalid route data preserving the primary save; installer preview/application/idempotence/exact payloads and zero-write conflict rejection. Headless artwork is substituted; local visuals and walking through entrance seams still need gameplay verification.
- Local test: enter a natural cave, walk into its room, save, close/reopen, select the same profile and Continue. Verify exact position/layer, health/inventory and return to surface. Repeat on surface and a separate profile. Custom/debug deeper connections still require Pass 6.2; this stage does not serialize their geometry or mutations. Liquid changes remain Pass 6.3.
- Next: Pass 6.2 connection/entrance changes, Pass 6.3 liquid/basin changes, then Pass 7 combined checks/menu completion. Autosaves and drop-expiry countdown remain separate future work.

## Connection Generation Planning Before Save Pass 6.2

- See LayerGenerationConnections.md for the scalable layer/connection plan. Query/tuning Pass 1 adds exact-layer biome queries without chunk loading and global per-source-layer frequency multipliers. Surface tuning is connected to the existing seeded sampler; deeper permanent connection generation remains Pass 2.
- Save Pass 6.1 was supplied separately and confirmed locally: underground position and inventory restored. Do not replace those installed changes with older pushed versions.
- Save Pass 6.2 is deferred until permanent seeded connections are implemented. DeepCavernsTest remains temporary and unmodified; planned feature/lair queries are not implemented yet. Save Pass 6.3 and Pass 7 remain outstanding.

## Permanent Layer Connection Planner — Pass 2

- Permanent seeded Upper Caverns to Deep Caverns corridors are implemented by this installer. Explicit layer target rules support additional depths such as Hell without a terminal-depth assumption. Both endpoints discover the same corridor; chunk preparation and metadata leases retain its geometry while needed.
- Test with a new campaign: the layer definition/generation recipe changes. Existing save files are preserved, and compatibility checks can refuse older recipes. Disable DeepCavernsTest for natural-corridor verification; the debug script itself remains unchanged.
- Next: connection restore integration to complete save Pass 6.2. Deeper player saving remains gated until then. Liquid persistence is Pass 6.3, followed by combined Pass 7 checks/menu work.


## Save Pass 6.2 / Layer Connections Pass 3 — Implemented

- Manual saving now supports any defined underground depth on ready natural terrain. Original surface entrance ID/position remains the return/respawn anchor, independently of saved depth. Schema 6 and existing sections are retained; seeded corridor geometry is regenerated, not written into JSON.
- Continue prepares local incoming/outgoing connection metadata incrementally and pins it before activating the saved layer. The temporary pin transfers responsibility to streamed chunks once the destination is ready. Failures preserve the existing save and release temporary planning resources.
- Temporary TEST_ corridor footprints are not saveable. Test natural connections with DeepCavernsTest disabled. Current resource recipe compatibility checks stay intact.
- The planned layer-connection work is complete. Remaining persistence: 6.3 changed liquids/basins; Pass 7 combined persistence/menu regression. Autosave and actual dropped-item expiry remain future work.
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '004952890fb3f909f5c704a99d0775d868ab932ae67a020beac6fcfd7e2ac177'
$Expected['WORLD/Layers/WorldLayerController.cs'] = '76052010e2ff6f17cef9e843c291965283d86a2527616a4edac65accfed6ce15'
$Expected['NOTES/OngoingWork/LayerGenerationConnections.md'] = '5dfc56b2150b9a75d84bcea921f4fd6f4ac2b36c016fb4d3cd3dc8f1ec8eca60'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = '9faf6561ca17e04c41e5e8c2facaa963656bdc82452c2deb22af290afa89862a'
$Expected['WORLD/Streaming/InfiniteWorldGeneration.cs'] = 'f6fef2b2e8bfff16b0ce989df891383dcb3cc500ba3d39b3d80050ad5568b671'
$Expected['WORLD/Layers/Connections/LayerConnectionPlanner.cs'] = '21ab76c5ae24d4bed4c3eac82743e8f7341e1f73d03f005af7212cb3577d8f4a'
$Expected['WORLD/Generation/Caves/CaveWorld.cs'] = '2a3c567acb9228d9238edd99b50946e8963711e8ea18d40d0144ccc880f51b43'
$Expected['SYSTEMS/Saving/WorldObjectSaves.cs'] = '1db1da422ac930f1a07e9d001ebaf7cc2b51e97715b507fe68f97a5bab16401f'
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
if (!$Pending.Count) { Write-Host 'Layer Connection Restore Pass 3 is already installed. No files changed.'; return }
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
Write-Host 'Reopen Godot and build C#. Saved-depth restoration is installed.'
Write-Host 'Test natural Upper Caverns / Deep Caverns corridors with DeepCavernsTest disabled.'
Write-Host 'Test saving in Deep Caverns, restarting, continuing and returning through natural corridors. Existing save files were not modified.'
