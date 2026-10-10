# Installs Pass 6.1 against bfb85d6. No Git operations.
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
    throw 'Install the preceding persistence passes first.'
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
    // Prepare only the saved layer, retaining the seeded surface return entrance.
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
            if (route == null || route.LowerLayer != layer)
                throw new InvalidDataException("The saved surface entrance could not be reconstructed.");
            controller.RestoreCampaignLayer(layer, position, route);
            _layerRestored = true;
        }
        _player.GlobalPosition = position;
        CaveWorld cave = controller.Worlds.GetUnderground(layer);
        if (!cave.Streaming.AreaReady(position)) return false;
        if (!controller.Worlds.IsAvailable(layer, position, 14f))
            throw new InvalidDataException("Saved position is no longer on available underground terrain.");
        return true;
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
        if (layer != WorldLayerId.Surface && (entrance == null || entrance.LowerLayer != layer ||
            controller.Worlds.IsAvailable(layer, _player.GlobalPosition, 14f) != true))
            throw new InvalidOperationException("Save on ready ground in a surface-connected cave. Deeper/custom routes require Pass 6.2.");
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
$Changes['SYSTEMS/Saving/CampaignData.cs'] = @'
// Versioned campaign data; runtime nodes and shared resources never enter JSON.
using System;
using System.Collections.Generic;

public sealed class CampaignData
{
    public int Version { get; set; } = 1;
    public string Coverage { get; set; } = "world-player-only";
    public string ProfileId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public uint Seed { get; set; }
    public float SpawnX { get; set; }
    public float SpawnY { get; set; }
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = new();
    public Dictionary<string, string> Resources { get; set; } = new();
    public PlayerSaveData Player { get; set; } = new();
    public double WorldSeconds { get; set; }
    public Dictionary<string, System.Text.Json.JsonElement> Sections { get; set; } = new();
}

public sealed class PlayerSaveData
{
    public string Layer { get; set; } = "surface";
    public string SurfaceEntranceId { get; set; } = "";
    public float SurfaceEntranceX { get; set; }
    public float SurfaceEntranceY { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public int Health { get; set; }
    public float[] Stats { get; set; } = Array.Empty<float>();
    public Dictionary<string, List<ModifierSaveData>> Modifiers { get; set; } = new();
    public float[] Reserves { get; set; } = Array.Empty<float>();
    public string Backpack { get; set; } = "";
    public Dictionary<string, string> ItemResources { get; set; } = new();
    public string[] Tools { get; set; } = Array.Empty<string>();
    public int SelectedTool { get; set; }
    public List<StackSaveData> Bag { get; set; } = new();
    public List<HotbarSaveData> Hotbar { get; set; } = new();
    public int SelectedHotbar { get; set; }
    public List<CraftSaveData> Crafting { get; set; } = new();
    public double[] Survival { get; set; } = Array.Empty<double>();
    public bool SprintExhausted { get; set; }
}

public sealed class ModifierSaveData
{
    public int Stat { get; set; }
    public int Operation { get; set; }
    public float Amount { get; set; }
}
public sealed class StackSaveData
{
    public string Item { get; set; } = "";
    public int Count { get; set; }
}
public sealed class HotbarSaveData
{
    public int Area { get; set; } = -1;
    public int Index { get; set; }
    public string Item { get; set; } = "";
}
public sealed class CraftSaveData
{
    public string Recipe { get; set; } = "";
    public int Remaining { get; set; }
    public double Elapsed { get; set; }
}
'@
$Changes['SYSTEMS/Saving/CampaignStore.cs'] = @'
// Keeps each profile's current campaign separate and replaces saves atomically.
using Godot;
using System;
using System.IO;
using System.Text.Json;

public static class CampaignStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string RecoveryMessage { get; private set; } = "";

    // =========================================================
    // Resolve only validated stable IDs beneath Godot's user directory.
    public static string PathFor(string profile)
    {
        if (!Guid.TryParseExact(profile, "N", out _))
            throw new InvalidDataException("Invalid profile identity.");
        return ProjectSettings.GlobalizePath($"user://Profiles/{profile}/campaign.json");
    }

    // =========================================================
    // Keep a damaged save visible to Continue so it can report its actual error.
    public static bool Exists(string profile)
    {
        string path = PathFor(profile);
        return File.Exists(path) || File.Exists(path + ".bak");
    }

    // =========================================================
    // Validate ownership and the supported format before exposing a save.
    private static CampaignData Read(string path, string profile)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Campaign file exceeds this pass's size limit.");
        CampaignData data = JsonSerializer.Deserialize<CampaignData>(File.ReadAllText(path));
        if (data == null ||
            !(data.Version == 1 && data.Coverage == "world-player-only" ||
              data.Version == 2 && data.Coverage == "world-player-resources" ||
              data.Version == 3 && data.Coverage == "world-player-resources-objects" ||
              data.Version == 4 && data.Coverage == "world-player-resources-objects-deaths" ||
              data.Version == 5 && data.Coverage == "world-player-resources-objects-entities" ||
              data.Version == 6 && data.Coverage == "world-player-resources-objects-entities-layer") ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null ||
            (data.Version == 1 ? data.Sections.Count != 0 :
                data.Version == 2 ? data.Sections.Count != 1 || !data.Sections.ContainsKey(ResourceChanges.Section) :
                data.Sections.Count != (data.Version == 3 ? 2 : data.Version == 4 ? 3 : 4) ||
                    !data.Sections.ContainsKey(ResourceChanges.Section) ||
                    !data.Sections.ContainsKey(WorldObjectSaves.Section) ||
                    (data.Version >= 4 && !data.Sections.ContainsKey(EntityDeaths.Section)) ||
                    (data.Version >= 5 && !data.Sections.ContainsKey(EntitySaves.Section))))
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        WorldLayerId.Validate(data.Player.Layer);
        if (!float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
            !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY) ||
            (data.Player.Layer != WorldLayerId.Surface && (data.Version < 6 ||
                string.IsNullOrWhiteSpace(data.Player.SurfaceEntranceId) ||
                data.Player.SurfaceEntranceId.Length > 512 ||
                !float.IsFinite(data.Player.SurfaceEntranceX) || !float.IsFinite(data.Player.SurfaceEntranceY))))
            throw new InvalidDataException("Invalid saved player layer, coordinates or return entrance.");
        ResourceChanges.ReadSection(data);
        WorldObjectSaves.ReadSection(data);
        EntityDeaths.ReadSection(data);
        EntitySaves.ReadSection(data);
        return data;
    }

    // =========================================================
    // Recover a readable previous save without overwriting damaged files.
    public static CampaignData Load(string profile)
    {
        RecoveryMessage = "";
        string path = PathFor(profile);
        try { return Read(path, profile); }
        catch (Exception original)
        {
            try
            {
                CampaignData data = Read(path + ".bak", profile);
                RecoveryMessage = "Loaded the previous campaign backup.";
                return data;
            }
            catch
            {
                throw new IOException("Could not read the campaign or its backup. " +
                    "Existing files were preserved. " + original.Message, original);
            }
        }
    }

    // =========================================================
    // Flush all bytes before replacing the current save; preserve a valid backup.
    public static void Write(CampaignData data)
    {
        string path = PathFor(data.ProfileId), temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        using (FileStream stream = new(temporary, FileMode.Create,
            System.IO.FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        Read(temporary, data.ProfileId);
        if (File.Exists(path))
        {
            bool valid = false;
            try { Read(path, data.ProfileId); valid = true; } catch { }
            File.Replace(temporary, path, valid ? path + ".bak" : null);
        }
        else File.Move(temporary, path);
        RecoveryMessage = "";
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
            surfaceEntrance.UpperLayer != WorldLayerId.Surface)
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
            : "Partial save: world/player, resources, items, containers and buildings.";
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
            ShowMessage("Partial campaign saved", "World, player, resources, items, containers and buildings saved to your profile.\n" +
                CampaignSession.SavedPositionFor(this) + "\n" +
                "Player position and layer, surviving entities, deaths and groups are included.");
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
$Changes['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = @'
# Planned Work â€” Menus, Persistence and Local Lighting

## Purpose

Two upcoming work areas:

1. Main menu, in-game pause menu and persistent saves.
2. Local light sources that illuminate the world.

These are planned features. This document does not mark them as implemented.

Keep both systems modular and reuse the existing world layers, input modes,
inventory systems and shared visual settings where appropriate.

---

## 1 â€” Main Menu, Pause Menu and Persistent Saves

### Goal

Provide a proper entry point to the game and allow the player to save,
quit and continue without losing world changes.

### Main Menu

Initial options:

- Continue â€” available when a valid save exists.
- New Campaign â€” create a new world and player.
- Load Campaign â€” choose an existing save.
- Change Profile - can switch between profiles
- Sandbox - my testing
- Options. - placeholder
- About - just about this game - placeholder
- Exit.

Keep menu presentation separate from world creation and save/load logic.
Do not embed these systems in world_infinite or the player script.

### In-Game Pause Menu

Press ESC during gameplay to open the pause menu.

Initial options:

- Resume.
- Save Game.
- Options.
- Return to Main Menu.
- Exit Game.

Use the shared input-mode system to prevent movement, shooting, hotbar
scrolling and world interactions while the menu is open.

For this single-player first pass, opening the pause menu pauses world
simulation. Menu controls must continue processing.

ESC should first close the active interface where appropriate, such as
the inventory or debug map, rather than opening several menus together.

### Persistent Saves

Saving must preserve the playable world, not only the player's position
and the world seed.

Save data should include:

- Save format version.
- World identity and generation seed.
- Generation settings needed to reproduce the world.
- Player position and exact layer ID.
- Player vitals and progression.
- Inventory, equipment and hotbar assignments.
- Changes to generated world objects.
- Harvested or depleted resources.
- Container contents and claimed loot.
- Placed objects and structures.
- Relevant changes to liquids and basins.
- Persistent entities where required.
- Persistent connections between layers where required.

Generated terrain can be recreated from its seed and settings.
Store changes to that generated world rather than saving every unchanged
tile or loaded node.

Unloading a chunk must not discard its persistent changes.
Returning to a chunk or reloading a save must not restore harvested
resources or regenerate already-claimed loot.

Persistent object identities must include the world layer so objects at
the same coordinates on different depths remain separate.

### Reliability

- Store saves in Godot's user data location.
- Write to a temporary file before replacing the previous save.
- Keep a recoverable previous save.
- Detect unsupported or damaged saves and show a useful message.
- Include a save format version for future migrations.
- Confirm before overwriting or abandoning unsaved progress.

Do not treat debug test placement as permanent world content by default.

### Suggested Passes

1. Menu scenes and ESC/input-mode integration.
2. Save format, world identity and player persistence.
3. Persistent chunk changes, containers and world objects.
4. Remaining systems, recovery handling and end-to-end verification.

Do not label persistence complete until all currently relevant gameplay
changes survive quitting and loading.

### Completion Checks

- New Game starts a fresh world.
- Continue restores the correct save and layer.
- ESC pauses and resumes cleanly.
- Gameplay input does not leak through menus.
- Inventory and hotbar assignments survive loading.
- Harvested resources remain harvested.
- Claimed loot stays claimed.
- Container contents and placed objects survive loading.
- Chunk unloading does not lose changes.
- Surface and underground progress remain independent.
- A failed save does not destroy the previous valid save.

---

## 2 â€” Local Light Sources

### Goal

Allow local sources to illuminate nearby terrain and objects.

Examples:

- Lamps and placed lights.
- Glowing enemies or wildlife.
- Bioluminescent plants.
- Powered equipment.
- Temporary effects and projectiles.

An emissive-looking sprite or glow alone is not enough: the source should
also affect nearby world surfaces.

### Design Direction

Use one reusable light definition and runtime system.

A source should be able to define:

- Colour.
- Intensity.
- Radius.
- Enabled state.
- Optional flicker or pulse.
- Whether it moves.
- Whether it requires shadows.

Keep species-specific light settings with their species and object-specific
settings with their objects.

Shared lighting behaviour belongs with the existing visual lighting
systems. Global quality limits and multipliers can live in CONFIG.

### Compatibility

Review the existing terrain and sprite shaders before choosing the
implementation.

Local lighting must work with:

- Procedural ground surfaces.
- Imported and baked sprites.
- Terrain elevation and projected artwork.
- Existing sun lighting and shadows.
- Surface and underground layers.

Lights must respect exact layer identities. A surface light must not
illuminate a cave directly below it merely because their coordinates
overlap.

Decide explicitly how local lighting interacts with fading layers during
entrance transitions.

### Performance

Start with a small number of lights and measure the cost in a dense biome.

- Exclude distant lights from active lighting work.
- Stop unloaded lights from updating.
- Avoid scanning every world object for every light each frame.
- Avoid separate processing helpers on every vegetation instance.
- Update static light data only when it changes.
- Limit simultaneous visible lights through shared quality settings.
- Make local shadow casting optional and reserve it for selected sources.

Do not assume a light is cheap because its visual effect looks simple.

### Suggested Passes

1. Review shader compatibility and implement one test light.
2. Verify terrain, sprites, elevation and layer isolation.
3. Add reusable definitions and attach lights to objects and entities.
4. Add quality controls and measure performance with multiple sources.
5. Consider flicker, pulses and selected local shadows afterward.

### Completion Checks

- A test lamp visibly illuminates nearby ground and sprites.
- Light fades smoothly with distance.
- Moving sources remain aligned with their artwork.
- Lights affect only the correct world layer.
- Lights disappear correctly when their objects unload or are removed.
- Existing sunlight and shadows remain correct.
- Multiple lights have a measured, acceptable performance cost.
- Disabling local lighting restores the baseline appearance.

---

## Working Rules

- Review the latest GitHub push before preparing each pass.
- Keep scripts focused and folders clearly organised.
- Reuse existing shared systems where appropriate.
- Provide complete replacement files or functions.
- Remove temporary test nodes and obsolete implementations after testing.
- Update this document with actual implementation and verification status.

## Save Passes 1â€“2 â€” Applied, Verification Pending

- One current campaign slot per stable profile ID; manual pause-menu saving only.
- Versioned JSON, temporary-file replacement, previous-save backup and recovery.
- Fresh campaign seeds, exported world settings and resource recipe compatibility checks.
- Surface player position, original spawn, stats/modifiers, vitals, inventory, equipment, hotbar, crafting and world clock.
- Resource definition edits are detected and incompatible loads are refused; resource migrations remain future work.
- Save while alive, grounded and on the surface. Underground restore is a later pass.
- Not yet saved: harvested resources, grass changes, loose drops, containers, loot, buildings, entities, basin changes or connections.
- Debug herd and deep-cave test systems are unchanged and outside this pass.
- Build and local gameplay verification must be completed before marking tested.

Checks: two separate profiles; pause/save/quit/Continue; same surface position, original respawn location, seed/layout, reserves, inventory, selected hotbar, crafting progress and eclipse time; second save backup; rejected incompatible recipe; no save creation from Sandbox/F6.


## Save Pass 3 — Changed Resources

- One world-owned ResourceChanges helper; no frame processing or references to retired nodes.
- Stable generated IDs use world layer, resource family, chunk coordinate and original scatter candidate index. Scene resources use world-relative node paths. Ground deposits use chunk/slot IDs.
- Only changed resources are recorded: unfinished harvesting/mining work, remaining ore, depleted rocks/trees/plants/ore, removed grass, extracted ground-deposit units and unfinished shovel work.
- Grazing and successful building grass clearing share Grass.Clear. Normal chunk retirement never records grass as consumed.
- Removed solids retain lightweight generation-only footprint/spacing reservations. They have no collision, navigation blockers, artwork or shadows.
- Resource artwork callbacks check that their hosts still exist after awaiting a bake, so harvesting/unloading during initialization cannot access a freed node.
- Spawners draw original variation values and retain plant/grass spacing before skipping removed candidates.
- Change records load before world initialization, remain after chunks unload and are included by the existing pause Save action.
- Version 1 world/player saves still load with empty resource history. The next successful save writes version 2 with the named resource-changes section. Old game code cannot load version 2.
- Changes made before installing this pass cannot be reconstructed from older saves.
- Persistence remains partial: loose drops, containers, structures and entities are subsequent passes. A harvested source is now saved as removed, but its uncollected loose reward is not yet saved; collect rewards before quitting until Pass 4.
- Saving underground is still blocked until the layer restoration pass. Generated identities are layer-scoped for future extension.
- Local gameplay check: partially mine one rock/ore; completely harvest another rock/tree/plant; clear/graze grass; extract ground material; leave until chunks unload and return; save/quit/Continue with the same profile. Check another profile remains separate.
- Automated verification passed: production C# compilation, version 1 save loading, mutation/save, chunk retirement/regeneration, separate-process restore, profile separation, invalid resource-section rejection and installer conflict protection/idempotence. Runtime tests use headless artwork substitutes and controlled ore/ground-deposit fixtures; local visual gameplay verification remains required.


## Save Pass 4 — Items, Containers and Structures

- Existing version 1/2 saves still load. The next successful save writes version 3, retaining both resource-changes and world-objects sections. Earlier game code cannot read version 3.
- Loose stacks save stable GUIDs, exact quantities, item references, world positions, owning layers, pickup radius and remaining pickup delay. Accepted drops still awaiting deferred AddChild are included in the snapshot.
- Optional RemainingLifetimeSeconds is saved/restored. Null means no expiry. This pass does not decrement lifetime or implement an expiry timer. A future gameplay-time expiry mechanic can update this field; offline time must not consume it. Zero-lifetime records are excluded from saves/restoration.
- Shared item encoding resolves catalog IDs and external resource references, including starter tools absent from the master catalog. Exact storage slots are restored without collection/merging.
- Ordinary containers retain their contents by explicit layer-scoped PersistentId or scene-relative path. Placed containers use the building GUID. Loot caches retain full, partial and completely empty contents, including containers whose visible nodes retired.
- Death-wreck records include stable death ID, layer and position; LootWorld rebuilds them through its existing availability checks. Duplicate death identities do not generate another wreck.
- Placed objects save stable GUID, source item/scene identities, grid anchor, footprint and health. They restore before natural resource generation/navigation, without charging inventory or clearing grass again. Destroyed placed objects are absent from subsequent snapshots.
- Scene world-object health is restored separately; player/entity/placed-object health stays with its corresponding owner. Destroyed scene world objects are removed before Ready. Current landing pod/crate have no health component; this supports scene objects that actually own Health.
- Startup safely initializes saved loot caches before the first loot roll, including containers whose Ready precedes LootWorld.Ready. Object/item recipe assets are included in compatibility stamps.
- This remains staged persistence. Entity health/deaths/population/groups are Pass 5; saving the player underground and exact-depth restoration remain Pass 6. A killed entity may return until Pass 5 despite its saved drops/wreck. Debug herd and deep-cave test systems remain unchanged.
- Automated verification: full production C# compilation; separate-process headless save/reload with controlled scene fixtures covering pending/partial drops, lifetime and layers, exact storage, empty/retired loot, structures and scene health; backup recovery and profile isolation; version 1/2 load and version 3 upgrade; installer preview, application, idempotent rerun and conflict rejection. Headless artwork uses a test substitute; local visual gameplay checks remain.
- Local checks: leave a loose drop; partially pick up a larger stack; deposit and withdraw storage; empty a loot crate; partially loot a wreck and unload its visible node; place, damage and destroy separate buildings; save/quit/Continue and verify quantities, empty containers, surviving buildings, health and original world layer. Check another profile remains separate.


## Save Pass 5, Stage 1 — Stable Entity Identity and Deaths

- Requires Save Pass 4. The next successful save writes campaign version 4 with resource-changes, world-objects and entity-deaths sections. Version 1/2/3 campaigns still load, starting with no entity-death history. Earlier game code cannot read version 4.
- One world-owned EntityDeaths helper retains lightweight tombstones; no per-frame scanning, retired actor references or per-entity persistence helper nodes.
- Population identity uses the owning population's world-relative path, origin layer, world seed, original chunk and slot. It deliberately does not depend on actor position, selected candidate, species or engine instance ID.
- Authored entities already present in the detached WorldObjects scene receive world-relative scene identities. Dead authored actors are removed before EnterTree/Ready, so reload cannot issue their death rewards again.
- Population discovery and preparation reject killed identities. Normal streaming retirement and QueueFree do not create death records. Tombstones outlive chunk/layer unloading.
- An entity retains its origin identity during layer transfers. Its death record separately stores the actual death layer. A surface population actor killed underground therefore cannot reappear at its original surface spawn.
- Actor death and death-delivery callbacks share the same history. A separate identity reward claim prevents duplicate callbacks/actors from issuing another reward. Persisted robot wreck IDs use the stable origin identity; actual wreck/drop position and layer remain owned by Pass 4.
- Saved entity scene/definition resources are included in compatibility stamps. Invalid or duplicate death records are rejected before replacing the current save. Existing backup recovery and profile isolation remain in use.
- Debug TallowbackTest and deep-cavity test spawning remain unchanged and are not assigned persistent identities by this pass. Do not use the temporary debug herd to verify persistent wildlife. Future runtime spawners need stable origin identities and must consult EntityDeaths before creating actors.
- Stage 1 does not restore surviving entity position, health, home, AI timers, transferred living actors or group state. Stage 2 handles surviving population/actor state; Stage 3 handles population/group restoration. A death that happened before this stage cannot be reconstructed from an older save's wreck alone.
- Automated verification passed: full production C# compilation; real health/death delivery with controlled headless actor fixtures; origin/death-layer identity separation; population rediscovery after clearing records; retirement exclusion; duplicate reward suppression; invalid death-section rejection preserving the primary; separate-process restore, backup recovery and profile isolation; version 3 load/version 4 upgrade and the surviving-state stage boundary; installer preview/apply/idempotence and zero-write conflict rejection. Artwork is substituted for headless testing; local visual gameplay remains to be checked.
- Local check: kill a normal streamed robot; save, quit, Continue and revisit its origin chunk. It stays absent and its existing reward does not duplicate. Leave/revisit the chunk before saving too. Confirm a second profile has its own death history. Test transferred deaths when convenient; player saves remain surface-only until Pass 6.

## Save Pass 5, Stages 2–3 — Living Entities, Population and Groups

- Implemented against reviewed push aa113d5. Preserve the newer notification, inventory and vegetation work; this installer changes only entity/group persistence, save coordination, pause-save wording and the two ongoing-work save notes.
- Campaign version 5 adds the named entities-groups section. Versions 1–4 remain readable and upgrade on the next successful Save; prior unsaved living-entity changes cannot be reconstructed. Earlier game code cannot read version 5.
- Chosen population records preserve stable origin identity, prefab/species, actual position, solo home, health, exact current layer, random seed/state, waiting/feeding cooldowns and group membership. Both live and retired records are included, independently of loaded origin chunks.
- Remembered actors are indexed by actual saved position and exact layer. Existing population intervals, physics-check limits and one-create-per-update budgets remain in use. Loaded survivors may restore on screen; new seeded spawns retain their original visibility/distance checks.
- Inactive/distant cave survivors remain lightweight records and never force all cave chunks to load. They can restore when that exact layer is active and nearby terrain/collision checks permit. Origin identity remains unchanged across transfers, so the original surface slot cannot create a duplicate.
- Authored scene entities retain their scene branches/custom children. Saved authored actors are detached before Ready and reattach when their terrain is available, with definition, ownership and membership configured before behaviour binding. Dead authored actors remain governed by Stage 1.
- Group persistence includes stable identity, layer, centre, formation/minimum-member settings, threat-sharing policy, logical member IDs and optional roaming home/configuration/random state/remaining timer. Offline time does not advance roaming or actor cooldowns.
- Streaming retirement reserves a living member's logical slot instead of dissolving a group. Returning actors replace the reserved slot. Real deaths, departures and cross-layer departures can dissolve undersized groups; dissolution tombstones prevent authored groups from returning on Continue.
- Combat targets, navigation paths, in-flight attacks and grass reservations are transient and are reacquired. Unfinished grazing meals restart; already consumed grass remains covered by Pass 3. Do not describe this as serialization of every temporary AI action.
- Debug TallowbackTest and deep-cavity test placement remain outside persistent entity identity, as previously requested. Generic groups persist when authored or linked to persistent members; the temporary debug herd is not the entity save verification target.
- Automated checks passed: full production C# compilation including the latest notification/inventory sources; actual seeded population preparation and retirement; real health/death callbacks; authored and retired cold-process restoration; home/health/wait and logical group state; remaining roaming timer; dissolution; repeated scans without duplicate actors; dormant cave records followed by actual cave activation/restoration; backup recovery/profile isolation; version 4 load/version 5 upgrade retaining previous deaths; invalid entity-section rejection preserving the primary; installer preview/application/idempotence and zero-write conflict rejection. Headless fixtures substitute artwork and the visibility predicate, using the established biome assets; local visuals and newly edited biome content remain to be checked.
- Local checks: damage/move a streamed robot, retire/revisit it, Save/quit/Continue and verify health/home/location; repeat with a transferred survivor when convenient. Verify group membership after retirement and permanent dissolution after deaths. Check a second profile. Player saves remain surface-only until Pass 6.
- Next: Pass 6 exact underground player restoration/connections/liquid changes; Pass 7 combined regression checks and remaining menu work. Drop expiry and automatic saving/options remain future features.

## Save Pass 6.1 — Exact Player Layer Restoration

- Implemented against bfb85d6. Preserve all newer UI, notifications, vegetation and unrelated work.
- Manual Save supports a living, grounded player on available surface or natural surface-connected cave terrain. Underground saves require the surface entrance used for the journey; deeper/custom routes and debug-only placement without that route are gated until Pass 6.2.
- Campaign version 6 stores the exact player layer/global position plus the stable seeded surface return entrance ID/position. Versions 1–5 remain readable and upgrade on the next successful Save. Earlier game versions cannot read version 6.
- Continue freezes player actions and automatic crossings, rebuilds the seeded return entrance, pins its metadata, activates only the saved cave layer and waits for its nearby terrain before restoring inventory/vitals/time and enabling gameplay. Invalid/missing layers, entrances or terrain fail visibly while preserving the existing save; there is no silent surface fallback.
- Respawn remains anchored to the original landing site; returning through the remembered entrance and ordinary death handling retain the existing controller flow. Camera and surface presentation are reset for the restored layer.
- Automated checks passed: full C# compilation including the latest notification sources; real transfer into a generated cave and version 6 capture; fresh-process exact underground position/layer/health/return-route restoration; underground resave, return to surface and surface resave; version 5 surface cold load/version 6 upgrade; invalid route data preserving the primary save; installer preview/application/idempotence/exact payloads and zero-write conflict rejection. Headless artwork is substituted; local visuals and walking through entrance seams still need gameplay verification.
- Local test: enter a natural cave, walk into its room, save, close/reopen, select the same profile and Continue. Verify exact position/layer, health/inventory and return to surface. Repeat on surface and a separate profile. Custom/debug deeper connections still require Pass 6.2; this stage does not serialize their geometry or mutations. Liquid changes remain Pass 6.3.
- Next: Pass 6.2 connection/entrance changes, Pass 6.3 liquid/basin changes, then Pass 7 combined checks/menu completion. Autosaves and drop-expiry countdown remain separate future work.
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
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = 'c808b94c488f20743eeff6314e2bf5df256b7a19047577bb3192f0b3dbfbc349'
$Expected['SYSTEMS/Saving/CampaignData.cs'] = '17f518f8c8f8fd5bbf02e154dbab0c82fc5c353cadd041431ac138387f58e112'
$Expected['SYSTEMS/Saving/CampaignStore.cs'] = '8592c3dc79de7338179ea13cb49ab6da0bc3ab87474fe897385580cdaa929432'
$Expected['WORLD/Layers/WorldLayerController.cs'] = '2c366b087b2621be169bc102c181885b61cff3b5f3a68505859196ba6618ee51'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = '4e26d7c4a9eb8251b81b9c2884fbd30b16b80655ba0976ab7a3688bb59a42f32'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = '6821223c4138d9ce6449c93284ce1401b4d981c896392e14fa3b7d1aeabe3466'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = '43f9d0734ad7c2c8693806b8c64ecc484f30cc3b4fcb599072390e14ee6b4380'
$Expected['WORLD/Layers/WorldLayerRuntime.cs'] = '8914263212540bcdc3d992c239e533d253b54012a7a3c5d33a7b60fe9ba6f901'
$Expected['WORLD/Layers/WorldLayerSurface.cs'] = 'b3dff45aeb73e5d62d315ee7d2c3ca9f8212840d2ad78d7ec72210835628b2c7'
$Expected['WORLD/Streaming/InfiniteWorldGeneration.cs'] = '9b83fec26ad94c8580af53c7507b7cda0779889d0742f028c1026816f7ffe581'
$Expected['WORLD/Chunks/ChunkController.cs'] = 'f4c246730c8d3255350e0eac177ff8620b288831a9dc7d6c946b14b2907a3968'
$Expected['WORLD/Generation/Caves/Chunks/CaveChunkController.cs'] = '32cae8a423965ce4725a134899a0d496f7ea1a70b0e02e8019b6d66feec1b29b'
$Expected['WORLD/Layers/Connections/WorldLayerConnections.cs'] = '58af6e7a3495f068f6588c66817ef002418827c3578e9bc4cf6e696d1cb90149'
$Expected['WORLD/Layers/Connections/WorldLayerConnection.cs'] = '226ce60dd897b0614ea86dbbbf31170aa1f6e007ec2033d26dc7084f456ad166'
$Expected['SYSTEMS/Saving/EntitySaves.cs'] = '64801c6c2330e1afebf9aca00bbc8bb53c4234bdf2716c86114fc5d51fe0ab72'
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
if (!$Pending.Count) { Write-Host 'Pass 6.1 is already installed. No files changed.'; return }
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
Write-Host 'Enter a natural cave, walk inside, save, quit and Continue with the same profile; test returning to surface.'
Write-Host 'Existing saves upgrade to version 6 on Save. Connection mutations and liquids remain Pass 6.2/6.3.'
