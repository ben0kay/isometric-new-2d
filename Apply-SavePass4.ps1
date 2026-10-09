# Installs save Pass 4: items, containers and structures. Based on push e5d6df4. No Git operations.
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
// Coordinates world/player restoration and the changed-resource save section.
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
        ResourceChanges.Find(this).Capture(_data);
        WorldObjectSaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureResourceDefinitions(_data);
        CampaignRecipe.CaptureObjectDefinitions(_data);
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
$Changes['SYSTEMS/Saving/CampaignRecipe.cs'] = @'
// Records exported world settings and detects incompatible generation resources.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

public static class CampaignRecipe
{
    private static readonly string[] Owners =
        { "CONFIG", "Systems/WorldGenerator", "Systems/ChunkController" };

    // =========================================================
    // Capture exported settings before Ready callbacks derive terrain and cave seeds.
    public static void Capture(Node world, CampaignData data)
    {
        data.Settings.Clear(); data.Resources.Clear();
        foreach (string owner in Owners)
        {
            Node node = world.GetNode(owner);
            Dictionary<string, string> values = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) == 0 ||
                    (usage & PropertyUsageFlags.Storage) == 0) continue;
                string name = property["name"].AsString();
                Variant value = node.Get(name);
                if (value.VariantType == Variant.Type.Object)
                {
                    Resource resource = value.AsGodotObject() as Resource;
                    if (resource == null) { values[name] = "null"; continue; }
                    if (string.IsNullOrEmpty(resource.ResourcePath) || resource.ResourcePath.Contains("::"))
                        throw new InvalidDataException($"Save needs a separate resource file for {owner}/{name}.");
                    values[name] = "@resource:" + resource.ResourcePath;
                    Stamp(resource.ResourcePath, data.Resources);
                }
                else values[name] = GD.VarToStr(value);
            }
            data.Settings[owner] = values;
        }
        // A null generator catalog uses the surface definition inside LayerCatalog.
        Stamp("res://WORLD/Layers/WorldLayers.tres", data.Resources);
    }

    // =========================================================
    // Include hard-coded ground catalogs and scene resource definitions in this save's recipe.
    public static void CaptureResourceDefinitions(CampaignData data)
    {
        Stamp("res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres", data.Resources);
        foreach (ResourceChangeData entry in ResourceChanges.ReadSection(data).Entries)
            Stamp(entry.Definition.Split("::")[0], data.Resources);
    }

    // =========================================================
    // Stamp object scenes, storage/loot definitions and external item references.
    public static void CaptureObjectDefinitions(CampaignData data)
    {
        foreach (string path in WorldObjectSaves.ReadSection(data).Recipes)
            Stamp(path, data.Resources);
    }

    // =========================================================
    // Hash referenced settings and their dependencies without storing engine objects.
    private static void Stamp(string path, Dictionary<string, string> stamps)
    {
        if (stamps.ContainsKey(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidDataException($"Missing world resource: {path}");
        stamps[path] = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)));
        foreach (string dependency in ResourceLoader.GetDependencies(path))
        {
            string resolved = dependency.Split("::").Last();
            if (resolved.StartsWith("res://", StringComparison.Ordinal)) Stamp(resolved, stamps);
        }
    }

    // =========================================================
    // Refuse changed resource recipes rather than silently rebuilding a different map.
    public static void Validate(CampaignData data)
    {
        if (data.Settings.Count != Owners.Length || data.Resources.Count == 0 || data.Resources.Count > 4096)
            throw new InvalidDataException("Missing or invalid generation recipe.");
        foreach (var stamp in data.Resources)
        {
            if (!stamp.Key.StartsWith("res://", StringComparison.Ordinal) ||
                !Godot.FileAccess.FileExists(stamp.Key) ||
                Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(stamp.Key))) != stamp.Value)
                throw new InvalidDataException("World generation resources changed since this save. " +
                    "Start a new campaign or restore the original resources. " + stamp.Key);
        }
    }

    // =========================================================
    // Apply settings to a detached scene so initialization sees the saved recipe.
    public static void Apply(Node world, CampaignData data)
    {
        Validate(data);
        foreach (string owner in Owners)
        {
            if (!data.Settings.TryGetValue(owner, out var values) || values == null || values.Count > 256)
                throw new InvalidDataException("Missing saved world settings.");
            Node node = world.GetNode(owner);
            HashSet<string> exported = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) != 0 &&
                    (usage & PropertyUsageFlags.Storage) != 0)
                    exported.Add(property["name"].AsString());
            }
            foreach (var setting in values)
            {
                if (!exported.Contains(setting.Key) || setting.Value == null)
                    throw new InvalidDataException("Unsupported saved setting.");
                if (setting.Value.StartsWith("@resource:", StringComparison.Ordinal))
                {
                    string path = setting.Value.Substring(10);
                    if (!data.Resources.ContainsKey(path))
                        throw new InvalidDataException("Unvalidated resource reference.");
                    Resource resource = ResourceLoader.Load(path)
                        ?? throw new InvalidDataException("Saved resource is unavailable.");
                    node.Set(setting.Key, resource);
                }
                else
                {
                    Variant value = GD.StrToVar(setting.Value);
                    if (value.VariantType == Variant.Type.Object || value.VariantType == Variant.Type.Callable ||
                        value.VariantType == Variant.Type.Signal)
                        throw new InvalidDataException("Invalid saved world setting type.");
                    node.Set(setting.Key, value);
                }
            }
        }
        world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed = data.Seed;
    }
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
              data.Version == 3 && data.Coverage == "world-player-resources-objects") ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null ||
            (data.Version == 1 ? data.Sections.Count != 0 :
                data.Version == 2 ? data.Sections.Count != 1 || !data.Sections.ContainsKey(ResourceChanges.Section) :
                data.Sections.Count != 2 || !data.Sections.ContainsKey(ResourceChanges.Section) ||
                    !data.Sections.ContainsKey(WorldObjectSaves.Section)))
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        ResourceChanges.ReadSection(data);
        WorldObjectSaves.ReadSection(data);
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
$Changes['ITEMS/World/WorldPickup.cs'] = @'
// Displays a dropped stack and collects only while a living player is nearby.
// Capacity failures leave items in the world; retries run only inside pickup range.
using Godot;
using System.Collections.Generic;
using System;

public partial class WorldPickup : Area2D
{
    #region Configuration
    public string PersistentId { get; set; } = Guid.NewGuid().ToString("N");
    // Null means no expiry. A later expiry mechanic will update remaining gameplay seconds.
    public double? RemainingLifetimeSeconds { get; set; }
    public ItemDefinition Item { get; set; }
    public string PendingLayer { get; set; } = WorldLayerId.Surface;
    public Vector2 PendingGlobalPosition { get; set; }
    public int Count { get; set; }
    public float PickupRadius { get; set; } = 48f;
    public double PickupDelay { get; set; } = 0.3;
    #endregion

    #region State
    private readonly HashSet<Player> _players = new();
    private double _delay, _retry;
    #endregion

    #region Persistence
    // =========================================================
    // Register scene-attached pickups while ResourceWorld separately tracks deferred ones.
    public override void _EnterTree()
    {
        AddToGroup("world_pickups");
    }

    // =========================================================
    // Capture exact remaining quantity and optional lifetime, without resetting either.
    public DropSaveData CaptureSave()
    {
        Vector2 position = IsInsideTree() ? GlobalPosition : PendingGlobalPosition;
        return new DropSaveData
        {
            Id = PersistentId, Item = Item.Id, Count = Count,
            Layer = IsInsideTree() ? WorldLayerMember.For(this) : PendingLayer,
            X = position.X, Y = position.Y, PickupRadius = PickupRadius,
            PickupDelay = IsNodeReady() ? Math.Max(0, _delay) : Math.Max(0, PickupDelay),
            RemainingLifetimeSeconds = RemainingLifetimeSeconds
        };
    }
    #endregion

    #region Lifecycle
// =========================================================
// Build a player trigger and display an icon or readable fallback label.
public override void _Ready()
{
    CollisionLayer = 0;
    CollisionMask = 2;
    Monitoring = true;
    Monitorable = false;
    _delay = PickupDelay;

    AddChild(new CollisionShape2D
    {
        Shape = new CircleShape2D { Radius = PickupRadius }
    });

    if (Item.Icon != null)
    {
        Vector2 size = Item.Icon.GetSize();
        TerrainVisual.Attach(this, new Rect2(Vector2.Zero, size),
            new Vector2(-12, -24),
            new Vector2(24f / Mathf.Max(1f, size.X),
                24f / Mathf.Max(1f, size.Y)),
            false, null, Item.Icon);
    }
    else
    {
        Label label = new()
        {
            Text = Item.ShortName,
            Position = new Vector2(-30, -28),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeColorOverride("font_color", Item.Tint);
        label.AddThemeFontSizeOverride("font_size", 12);
        AddChild(label);
    }

    BodyEntered += OnBodyEntered;
    BodyExited += OnBodyExited;
    SetPhysicsProcess(false);
}

    // =========================================================
    // Begin collection checks only when a player enters the trigger.
    private void OnBodyEntered(Node2D body)
    {
        if (body is not Player player) return;
        _players.Add(player);
        _retry = 0;
        SetPhysicsProcess(true);
    }

    // =========================================================
    // Stop processing empty triggers.
    private void OnBodyExited(Node2D body)
    {
        if (body is Player player) _players.Remove(player);
        if (_players.Count == 0) SetPhysicsProcess(false);
    }
    #endregion

    #region Collection
// =========================================================
// Collect nearby items only when player and pickup occupy the same layer.
public override void _PhysicsProcess(double delta)
{
    _delay -= delta;
    if (_delay > 0) return;
    _retry -= delta;
    if (_retry > 0) return;
    _retry = 0.5;

    foreach (Player player in _players)
    {
        if (!GodotObject.IsInstanceValid(player) ||
            player.IsQueuedForDeletion() ||
            !WorldLayerMember.Same(this, player)) continue;

        if (!player.GetNode<Health>("Systems/Health").IsAlive) continue;

        PlayerInventory inventory =
            player.GetNode<PlayerInventory>("Systems/Inventory");

        int budget = 16;
        while (Count > 0 && budget-- > 0)
        {
            if (!inventory.TryCollect(Item, 1)) break;
            Count--;
        }

        if (Count > 0) continue;
        SetPhysicsProcess(false);
        QueueFree();
        return;
    }
}
    #endregion
}
'@
$Changes['ITEMS/World/ResourceWorld.cs'] = @'
// Owns the world's item catalog and spawns independent dropped stacks.
// Drops remain separate from mined objects so deleting a resource cannot delete its yield.
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ResourceWorld : Node
{
	#region Configuration
	[Export] public ItemCatalog Catalog { get; set; }
	[Export] public float PickupRadius { get; set; } = 48f;
	[Export] public double PickupDelay { get; set; } = 0.3;
	#endregion

	#region State
	private Node2D _objects;
    private readonly List<WorldPickup> _pending = new();
	#endregion

	#region Lifecycle
	// =========================================================
	// Initialize before world objects begin their own Ready callbacks.
	public override void _EnterTree()
	{
		if (Catalog == null)
			throw new InvalidOperationException("ResourceWorld requires an ItemCatalog.");

		Catalog.Initialize();
		_objects = GetNode<Node2D>("../../WorldObjects");
		AddToGroup("resource_world");
		SetProcess(false);
		SetPhysicsProcess(false);
	}
	#endregion

	#region Drops
	// =========================================================
	// Resolve the service belonging to the current scene tree.
	public static ResourceWorld Find(Node node)
	{
		return node.GetTree().GetFirstNodeInGroup("resource_world") as ResourceWorld;
	}

	// =========================================================
	// Resolve an item and defer scene attachment outside physics queries.
	public bool Spawn(string itemId, int count, Vector2 globalPosition,
        Node owner = null)
	{
		ItemDefinition item = Catalog.Get(itemId);
        Node2D objects = RootFor(owner);
		if (item == null || count <= 0 ||
			!GodotObject.IsInstanceValid(objects))
		{
			GD.PushError($"[Resources] Cannot drop '{itemId}' ×{count}.");
			return false;
		}

		WorldPickup pickup = new()
		{
			Name = "ItemDrop",
			Item = item,
			Count = count,
			PickupRadius = Mathf.Max(8f, PickupRadius),
			PickupDelay = Math.Max(0, PickupDelay),
			Position = objects.ToLocal(globalPosition)
		};
		QueuePickup(objects, pickup);
		return true;
	}

// =========================================================
// Drop inventory items into the player's currently active world layer.
public bool SpawnItem(ItemDefinition item, int count, Vector2 globalPosition)
{
	Node2D objects = WorldLayerController.DropRoot(this, _objects);
	if (item == null || count <= 0 ||
		!GodotObject.IsInstanceValid(objects)) return false;

	WorldPickup pickup = new()
	{
		Name = "ItemDrop",
		Item = item,
		Count = count,
		PickupRadius = Mathf.Max(8f, PickupRadius),
		PickupDelay = Math.Max(0, PickupDelay),
		Position = objects.ToLocal(globalPosition)
	};
	QueuePickup(objects, pickup);
	return true;
}

// =========================================================
// Validate every reward before spawning any part of a harvested object's yield.
public bool SpawnHarvest(string primaryId, int primaryCount,
	string[] bonusIds, Vector2 globalPosition, Node owner = null)
{
    Node2D objects = RootFor(owner);
	if (primaryCount <= 0 || !GodotObject.IsInstanceValid(objects))
		return false;

	var rewards = new System.Collections.Generic.List<(ItemDefinition Item, int Count)>();
	ItemDefinition primary = Catalog.Get(primaryId);
	if (primary == null)
	{
		GD.PushError($"[Resources] Unknown harvest item: '{primaryId}'.");
		return false;
	}
	rewards.Add((primary, primaryCount));

	foreach (string id in bonusIds ?? Array.Empty<string>())
	{
		if (string.IsNullOrWhiteSpace(id)) continue;
		ItemDefinition item = Catalog.Get(id);
		if (item == null)
		{
			GD.PushError($"[Resources] Unknown bonus harvest item: '{id}'.");
			return false;
		}
		rewards.Add((item, 1));
	}

	var pickups = new System.Collections.Generic.List<WorldPickup>();
	for (int i = 0; i < rewards.Count; i++)
	{
		Vector2 offset = rewards.Count == 1 ? Vector2.Zero :
			Vector2.FromAngle(Mathf.Tau * i / rewards.Count) * 10f;
		pickups.Add(new WorldPickup
		{
			Name = "ItemDrop",
			Item = rewards[i].Item,
			Count = rewards[i].Count,
			PickupRadius = Mathf.Max(8f, PickupRadius),
			PickupDelay = Math.Max(0, PickupDelay),
			Position = objects.ToLocal(globalPosition + offset)
		});
	}

	foreach (WorldPickup pickup in pickups)
		QueuePickup(objects, pickup);
	return true;
}
	#endregion

	// =========================================================
// Deliver an actor's drops into that actor's layer, independent of the player.
public bool SpawnItemFor(
    Node owner, ItemDefinition item, int count, Vector2 globalPosition)
{
    Node2D objects = RootFor(owner);
    if (item == null || count <= 0 || !GodotObject.IsInstanceValid(objects))
        return false;

    WorldPickup pickup = new()
    {
        Name = "ItemDrop",
        Item = item,
        Count = count,
        PickupRadius = Mathf.Max(8f, PickupRadius),
        PickupDelay = Math.Max(0, PickupDelay),
        Position = objects.ToLocal(globalPosition)
    };
    QueuePickup(objects, pickup);
    return true;
}

    #region Persistence
    // =========================================================
    // Include accepted rewards immediately, even before deferred scene attachment.
    private void QueuePickup(Node2D root, WorldPickup pickup)
    {
        pickup.PendingLayer = WorldLayerMember.For(root);
        pickup.PendingGlobalPosition = root.ToGlobal(pickup.Position);
        _pending.Add(pickup);
        Callable.From(() =>
        {
            if (!GodotObject.IsInstanceValid(pickup)) return;
            _pending.Remove(pickup);
            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(root) || !root.IsInsideTree() || root.IsQueuedForDeletion())
            {
                pickup.Free();
                return;
            }
            root.AddChild(pickup);
        }).CallDeferred();
    }

    // =========================================================
    // Release unattached rewards if their world closes before its deferred calls run.
    public override void _ExitTree()
    {
        foreach (WorldPickup pickup in _pending)
            if (GodotObject.IsInstanceValid(pickup) && !pickup.IsInsideTree()) pickup.Free();
        _pending.Clear();
    }

    // =========================================================
    // Enumerate both attached pickups and accepted pending rewards without duplicates.
    public IEnumerable<WorldPickup> SaveDrops()
    {
        Node world = WorldConfig.Find(this).GetParent();
        foreach (Node node in GetTree().GetNodesInGroup("world_pickups"))
            if (node is WorldPickup pickup && world.IsAncestorOf(pickup)) yield return pickup;
        foreach (WorldPickup pickup in _pending) yield return pickup;
    }

    // =========================================================
    // Recreate an exact saved stack in its original layer without issuing a new reward.
    public void RestoreDrop(DropSaveData data, ItemDefinition item)
    {
        Node2D root = data.Layer == WorldLayerId.Surface ? _objects
            : WorldLayerRuntime.Find(this).ObjectsFor(data.Layer);
        root.AddChild(new WorldPickup
        {
            Name = "ItemDrop", PersistentId = data.Id, Item = item, Count = data.Count,
            Position = root.ToLocal(new Vector2(data.X, data.Y)),
            PickupRadius = data.PickupRadius, PickupDelay = data.PickupDelay,
            RemainingLifetimeSeconds = data.RemainingLifetimeSeconds
        });
    }
    #endregion

    // =========================================================
	// Resolve source ownership independently from the player's current depth.
	private Node2D RootFor(Node owner)
	{
		if (owner == null) return WorldLayerController.DropRoot(this, _objects);
		string layer = WorldLayerMember.For(owner);
		return layer == WorldLayerId.Surface ? _objects
			: (WorldLayerRuntime.Find(this) ??
				throw new InvalidOperationException("Missing layer runtime."))
				.ObjectsFor(layer);
	}
}
'@
$Changes['SYSTEMS/Storage/WorldStorage.cs'] = @'
// Owns container contents and performs complete-stack inventory transfers.
// Loot containers extend this component without duplicating storage or transfer rules.
using Godot;
using System;

public partial class WorldStorage : Node
{
    #region Configuration
    [ExportGroup("Identity")]
    [Export] public string PersistentId { get; set; } = "";

    [ExportGroup("Storage")]
    [Export] public StorageDefinition Definition { get; set; }

    [ExportGroup("Owner")]
    [Export] public NodePath HostPath { get; set; } = new("../..");
    #endregion

    #region State
    public event Action Changed;
    public bool Initialized { get; private set; }
    public Node2D Host { get; private set; }
    public int SlotCount => _contents.SlotCount;
    public float WeightKg { get; private set; }
    public float VolumeLitres { get; private set; }
    public virtual bool CanDeposit => true;

    protected InventoryStorage _contents = new(0);
    #endregion

    #region Lifecycle
    // =========================================================
    // Register regular storage and loot with the same interaction interface.
    public override void _EnterTree()
    {
        AddToGroup("world_storage");
    }

    // =========================================================
    // Allocate contents and resolve the physical container.
    public override void _Ready()
    {
        if (Definition == null)
            throw new InvalidOperationException("WorldStorage requires a Definition.");

        Definition.Validate();
        Host = GetNode<Node2D>(HostPath);
        _contents = new InventoryStorage(Definition.SlotCount);
        Initialized = true;
        try
        {
            if (this is not LootContainer) WorldObjectSaves.Ensure(this).RestoreStorage(this);
        }
        catch (Exception error)
        {
            WorldObjectSaves.Ensure(this).ReportLoadFailure(error);
            throw;
        }
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Persistence
    // =========================================================
    // Copy physical slots without exposing live mutable storage.
    public InventoryStorage ExportContents()
    {
        return _contents.Clone();
    }

    // =========================================================
    // Restore a complete compatible container before publishing its contents.
    public void ImportContents(InventoryStorage contents)
    {
        contents.GetTotals(out float weight, out float volume);
        if (contents.SlotCount != Definition.SlotCount || weight > Definition.MaximumWeightKg ||
            volume > Definition.CapacityLitres)
            throw new System.IO.InvalidDataException("Saved container capacity changed.");
        _contents = contents.Clone();
        PublishContents();
    }
    #endregion

    #region Queries
    // =========================================================
    // Read a stack without exposing mutable storage.
    public InventoryStack GetStack(int index)
    {
        return _contents.Get(index);
    }

// =========================================================
// Require matching world layers as well as normal proximity and health.
public bool CanInteract(Player player)
{
    return Initialized && GodotObject.IsInstanceValid(Host) &&
        Host.IsInsideTree() && !Host.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(player) && player.IsInsideTree() &&
        !player.IsQueuedForDeletion() &&
        WorldLayerMember.Same(Host, player) &&
        player.GetNode<Health>("Systems/Health").IsAlive &&
        Host.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) <=
            Definition.InteractionRange * Definition.InteractionRange;
}
    #endregion

    #region Transfers
    // =========================================================
    // Validate capacity before removing a complete backpack stack.
    public bool TryDeposit(PlayerInventory player, int index, out string reason)
    {
        reason = "";
        if (!Initialized || player == null) return false;
        if (!CanDeposit)
        {
            reason = "This is salvage, not a storage container.";
            return false;
        }

        InventoryStack stack = player.GetStack(
            new InventoryAddress(InventoryArea.Bag, index));
        if (stack.IsEmpty) return false;

        InventoryStorage staged = _contents.Clone();
        if (!staged.TryAdd(stack.Item, stack.Count))
        {
            reason = "Container has no free slots or stack space.";
            return false;
        }

        staged.GetTotals(out float weight, out float volume);
        if (weight > Definition.MaximumWeightKg)
        {
            reason = "Container weight limit reached.";
            return false;
        }
        if (volume > Definition.CapacityLitres)
        {
            reason = "Container volume limit reached.";
            return false;
        }
        if (!player.TryRemoveBagStack(index, stack))
        {
            reason = "Backpack contents changed; try again.";
            return false;
        }

        _contents = staged;
        PublishContents();
        return true;
    }

    // =========================================================
    // Accept the stack into the backpack before removing container contents.
    public bool TryWithdraw(PlayerInventory player, int index, out string reason)
    {
        reason = "";
        if (!Initialized || player == null || !_contents.HasSlot(index))
            return false;

        InventoryStack stack = _contents.Get(index);
        if (stack.IsEmpty) return false;

        if (!player.TryCollect(stack.Item, stack.Count))
        {
            reason = "Backpack cannot accept this stack. Check slots, weight and volume.";
            return false;
        }

        _contents.Set(index, default);
        PublishContents();
        return true;
    }

    // =========================================================
    // Update physical totals and notify the shared inventory window.
    protected virtual void PublishContents()
    {
        _contents.GetTotals(out float weight, out float volume);
        WeightKg = weight;
        VolumeLitres = volume;
        WorldObjectSaves.Find(this)?.StoreStorage(this);
        Changed?.Invoke();
    }
    #endregion
}
'@
$Changes['SYSTEMS/Loot/LootContainer.cs'] = @'
// Adds seeded, take-only loot to shared world storage and inventory controls.
// LootWorld retains contents independently from streamed container instances.
using Godot;
using System;

public partial class LootContainer : WorldStorage
{
    #region Configuration
    [ExportGroup("Loot")]
    [Export] public LootTable Table { get; set; }

    public override bool CanDeposit => false;
    private LootWorld _world;
    private string _key;
    #endregion

    #region Lifecycle
    // =========================================================
    // Restore session contents or generate this loot container once.
    public override void _Ready()
    {
        try
        {
        base._Ready();
        if (Table == null)
            throw new InvalidOperationException("LootContainer requires a LootTable.");

        _world = LootWorld.GetOrCreate(this);
        _key = WorldObjectSaves.StorageKey(this);
        WorldObjectSaves.Ensure(this).RememberRecipe(Table);
        WorldObjectSaves.Find(this).RememberRecipe(Definition);

        _contents = _world.GetContents(_key, Table, Definition);
        ImportContents(_contents);
        }
        catch (Exception error)
        {
            WorldObjectSaves.Ensure(this).ReportLoadFailure(error);
            throw;
        }
    }

    // =========================================================
    // Retain committed contents before refreshing the shared inventory window.
    protected override void PublishContents()
    {
        if (GodotObject.IsInstanceValid(_world) && _key != null)
            _world.StoreContents(_key, _contents);
        base.PublishContents();
    }
    #endregion
}
'@
$Changes['SYSTEMS/Loot/LootWorld.cs'] = @'
// Retains session loot contents and restores wrecks created by robot deaths.
// Armoured loot crates are spawned by ChunkController, not this service.
using Godot;
using System;
using System.Collections.Generic;

public partial class LootWorld : Node
{
#region State
private sealed class DeathWreck
{
    public string Id;
    public Vector2 Position;
    public string Layer;
    public Node2D Actor;
}

private readonly Dictionary<string, InventoryStorage> _contents = new();
private readonly Dictionary<string, DeathWreck> _wrecks = new();
private ChunkController _chunks;
private Node2D _objects;
private PackedScene _wreckScene;
private double _timer;
private bool _initialized;
#endregion

    #region Lifecycle
    // =========================================================
    // Register this world's loot service before containers initialize.
    public override void _EnterTree()
    {
        AddToGroup("loot_world");
    }

    // =========================================================
    // Resolve world services and the shared robot wreck scene.
    public override void _Ready()
    {
        try { InitializeServices(); }
        catch (Exception error)
        {
            WorldObjectSaves.Ensure(this).ReportLoadFailure(error);
            throw;
        }
    }

    // =========================================================
    // Allow scene containers to initialize loot before the Systems branch finishes Ready.
    private void InitializeServices()
    {
        if (_initialized) return;
        _chunks = GetNode<ChunkController>("../ChunkController");
        _objects = GetNode<Node2D>("../../WorldObjects");
        _wreckScene = GD.Load<PackedScene>(
            "res://WORLDABLES/Objects/Loot/RobotWrecks/Basic/BasicRobotWreck.tscn");

        if (_wreckScene == null)
            throw new InvalidOperationException("BasicRobotWreck.tscn is missing.");

        WorldObjectSaves.Ensure(this).RememberRecipe(_wreckScene);
        RestoreObjects();
        _initialized = true;
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Find the loot service belonging to the current world.
    public static LootWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("loot_world") as LootWorld;
    }

    // =========================================================
    // Create the service automatically beneath the existing world Systems node.
    public static LootWorld GetOrCreate(Node context)
    {
        LootWorld existing = Find(context);
        if (existing != null) return existing;

        Node generator = context.GetTree().GetFirstNodeInGroup("world_generator");
        if (generator == null)
            throw new InvalidOperationException("Loot requires WorldGenerator.");

        LootWorld world = new() { Name = "LootWorld" };
        generator.GetParent().AddChild(world);
        return world;
    }
    #endregion

    #region Persistence
    // =========================================================
    // Restore cached empty/full loot and wreck locations before any container can reroll.
    private void RestoreObjects()
    {
        WorldObjectSaves owner = WorldObjectSaves.Find(this);
        foreach (var pair in owner.Saved.Loot) _contents.Add(pair.Key, owner.Decode(pair.Value));
        foreach (WreckSaveData saved in owner.Saved.Wrecks)
            _wrecks.Add(saved.Id, new DeathWreck
            {
                Id = saved.Id, Layer = saved.Layer, Position = new Vector2(saved.X, saved.Y)
            });
    }

    // =========================================================
    // Include retained contents and unloaded wrecks rather than only visible container nodes.
    public void CaptureObjects(WorldObjectSaves owner, WorldObjectsData saved)
    {
        saved.Loot.Clear(); saved.Wrecks.Clear();
        foreach (var pair in _contents) saved.Loot.Add(pair.Key, owner.Encode(pair.Value));
        foreach (DeathWreck wreck in _wrecks.Values)
            saved.Wrecks.Add(new WreckSaveData
            {
                Id = wreck.Id, Layer = wreck.Layer, X = wreck.Position.X, Y = wreck.Position.Y
            });
    }
    #endregion

    #region Session Contents
    // =========================================================
    // Generate each container once, including retaining completely empty contents.
    public InventoryStorage GetContents(
        string id, LootTable table, StorageDefinition definition)
    {
        InitializeServices();
        WorldObjectSaves owner = WorldObjectSaves.Ensure(this);
        owner.RememberRecipe(table); owner.RememberRecipe(definition);
        if (_contents.TryGetValue(id, out InventoryStorage existing))
            return existing.Clone();

        ResourceWorld resources = ResourceWorld.Find(this);
        if (resources == null)
            throw new InvalidOperationException("Loot requires ResourceWorld.");

        InventoryStorage generated = table.Generate(
            resources.Catalog, definition, SeedFor(id, _chunks.WorldSeed));

        _contents.Add(id, generated.Clone());
        return generated;
    }

    // =========================================================
    // Retain committed contents independently from their visible container.
    public void StoreContents(string id, InventoryStorage contents)
    {
        _contents[id] = contents.Clone();
    }

    // =========================================================
    // Produce stable loot seeds from world seed and container identity.
    private static ulong SeedFor(string id, uint worldSeed)
    {
        unchecked
        {
            ulong value = 14695981039346656037UL ^ worldSeed;
            foreach (char character in id)
            {
                value ^= character;
                value *= 1099511628211UL;
            }
            return value;
        }
    }
    #endregion

    #region Robot Deaths
// =========================================================
// Retain the death's location and layer independently from its visible wreck.
public void RecordRobotDeath(
    string id, Vector2 position, string layer = WorldLayerId.Surface)
{
    if (_wrecks.ContainsKey(id))
    {
        GD.PushWarning($"[Wreck] Duplicate death identity: {id}");
        return;
    }

    DeathWreck wreck = new()
    {
        Id = id,
        Position = position,
        Layer = layer
    };
    _wrecks.Add(id, wreck);

    Callable.From(() =>
    {
        if (!IsInsideTree() || IsQueuedForDeletion()) return;
        try
        {
            RestoreWreck(wreck);
        }
        catch (Exception error)
        {
            GD.PushError($"[Wreck] Creation failed: {error}");
        }
    }).CallDeferred();
}

// =========================================================
// Create the wreck beneath the correct layer's object root.
private void RestoreWreck(DeathWreck record)
{
    if (GodotObject.IsInstanceValid(record.Actor) || !WreckAvailable(record))
        return;

    Node2D root = record.Layer == WorldLayerId.Surface ? _objects
        : WorldLayerRuntime.Find(this).ObjectsFor(record.Layer);

    Node2D wreck = _wreckScene.Instantiate<Node2D>();
    LootContainer loot = wreck.GetNode<LootContainer>("Systems/Loot");
    loot.PersistentId = record.Id;
    wreck.Position = root.ToLocal(record.Position);

    record.Actor = wreck;
    root.AddChild(wreck);
}

// =========================================================
// Retain wreck contents while restoring at most one active-layer wreck per update.
public override void _Process(double delta)
{
    if (!_chunks.WorldReady) return;
    _timer -= delta;
    if (_timer > 0.0) return;
    _timer = 0.25;

    foreach (DeathWreck record in _wrecks.Values)
    {
        if (!GodotObject.IsInstanceValid(record.Actor))
            record.Actor = null;

        if (!WreckAvailable(record) && record.Actor != null)
        {
            record.Actor.QueueFree();
            record.Actor = null;
        }
    }

    foreach (DeathWreck record in _wrecks.Values)
    {
        if (record.Actor != null || !WreckAvailable(record)) continue;
        RestoreWreck(record);
        break;
    }
}

    // =========================================================
// Restore artwork only on its active layer and ready terrain.
private bool WreckAvailable(DeathWreck record)
{
    WorldLayerRuntime runtime = WorldLayerRuntime.Find(this);
    string current = runtime?.ActiveLayer ?? WorldLayerId.Surface;
    if (record.Layer != current) return false;
    return runtime != null
        ? runtime.IsAvailable(record.Layer, record.Position)
        : _chunks.IsNavigationPointAvailable(record.Position);
}
    #endregion
}
'@
$Changes['SYSTEMS/Placement/PlacedObject.cs'] = @'
// Creates a placed object's solid footprint, terrain-adjusted artwork, and health.
// Uses the existing Obstacle family so navigation reacts to placement and removal.
using Godot;

public partial class PlacedObject : Obstacle
{
    #region State
    public Health ObjectHealth { get; private set; }
    public string PersistentId { get; private set; }
    public ItemDefinition SourceItem { get; private set; }
    public PlaceableDefinition Definition => _definition;

    private PlacementWorld _world;
    private PlaceableDefinition _definition;
    private Vector2I _anchor;
    #endregion

    #region Setup
    // =========================================================
    // Configure footprint dimensions before navigation discovers this object.
    public void Configure(
        PlacementWorld world, PlaceableDefinition definition, Vector2I anchor,
        ItemDefinition sourceItem = null, string persistentId = null)
    {
        PersistentId = persistentId ?? System.Guid.NewGuid().ToString("N");
        SourceItem = sourceItem;
        _world = world;
        _definition = definition;
        _anchor = anchor;
        Height = definition.CoverHeight;

        Vector2[] corners = world.Grid.Corners(anchor, definition.Cells);
        Vector2 low = corners[0], high = corners[0];

        foreach (Vector2 corner in corners)
        {
            low = new Vector2(
                Mathf.Min(low.X, corner.X), Mathf.Min(low.Y, corner.Y));
            high = new Vector2(
                Mathf.Max(high.X, corner.X), Mathf.Max(high.Y, corner.Y));
        }

        Footprint = high - low;
    }

// =========================================================
// Build placement collision, lit artwork, obstruction fading and health.
public override void _Ready()
{
    if (_world == null || _definition == null)
    {
        GD.PushError("PlacedObject must be configured by PlacementWorld.");
        QueueFree();
        return;
    }

    CollisionLayer = 1;
    CollisionMask = 0;

    Vector2 centre = _world.Grid.Centre(_anchor, _definition.Cells);
    Vector2[] points = _world.Grid.Corners(_anchor, _definition.Cells);
    for (int i = 0; i < points.Length; i++)
        points[i] = (points[i] - centre) * 0.98f;

    AddChild(new CollisionShape2D
    {
        Name = "Footprint",
        Shape = new ConvexPolygonShape2D { Points = points }
    });

    CreateShadowAsync(points);

    Node2D artwork = _definition.ArtworkScene.Instantiate<Node2D>();
    artwork.Name = "Visual";
    artwork.Position = Vector2.Up * _world.HeightAt(GlobalPosition);
    artwork.Scale *= _definition.ArtworkScale;
    AddChild(artwork);

    Rect2? drawingBounds = null;
    Vector2[] outline = _definition.ObstructionOutline;

    if (outline != null && outline.Length > 0)
    {
        Rect2 bounds = new(outline[0], Vector2.Zero);
        foreach (Vector2 point in outline)
            bounds = bounds.Expand(point);

        if (bounds.Size.X > 0f && bounds.Size.Y > 0f)
            drawingBounds = bounds;
    }

    WorldLightingMaterials.Attach(this, artwork, null, drawingBounds);

    PlayerObstructionFade.Attach(
        this, artwork, _definition.ObstructionOutline);

    ObjectHealth = GetNode<Health>("Systems/Health");
    ObjectHealth.Died += OnDestroyed;

    SetProcess(false);
    SetPhysicsProcess(false);
}

    // =========================================================
    // Capture the source item's recipe, occupied cells and current building health.
    public StructureSaveData CaptureSave()
    {
        if (SourceItem == null)
            throw new System.IO.InvalidDataException("Placed object lacks its source item identity.");
        return new StructureSaveData
        {
            Id = PersistentId, Item = SourceItem.Id, X = _anchor.X, Y = _anchor.Y,
            Width = _definition.Cells.X, Depth = _definition.Cells.Y,
            WorldScene = _definition.WorldScene.ResourcePath,
            ArtworkScene = _definition.ArtworkScene.ResourcePath,
            Health = ObjectHealth.Current
        };
    }

    // =========================================================
    // Remove the object once health reaches zero.
    private void OnDestroyed()
    {
        QueueFree();
    }

    // =========================================================
    // Free occupied cells and disconnect health signals.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(ObjectHealth))
            ObjectHealth.Died -= OnDestroyed;

        if (GodotObject.IsInstanceValid(_world) && _definition != null)
            _world.Unregister(this, _anchor, _definition.Cells);
    }

        // =========================================================
    // Wait for atmosphere initialization, then create one cached ground shadow.
    private async void CreateShadowAsync(Vector2[] footprint)
    {
        try
        {
            WorldAtmosphere atmosphere =
                GetTree().GetFirstNodeInGroup("world_atmosphere")
                as WorldAtmosphere;

            if (!GodotObject.IsInstanceValid(atmosphere)) return;

            Node systems = atmosphere.GetParent();

            if (!systems.IsNodeReady())
                await ToSignal(systems, Node.SignalName.Ready);

            if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(atmosphere))
                return;

            atmosphere.CreateObstacleShadow(this, footprint);
        }
        catch (System.Exception error)
        {
            GD.PushError(
                $"Placed object '{Name}' shadow failed: {error}");
        }
    }
    #endregion
}
'@
$Changes['SYSTEMS/Placement/PlacementWorld.cs'] = @'
// Validates surface placement, tracks occupied cells, and spawns world objects.
// Created once per world; placement previews share its terrain and physics rules.
using Godot;
using System.Collections.Generic;

public partial class PlacementWorld : Node
{
    #region State
    public BuildingGrid Grid { get; private set; }

    private ChunkController _chunks;
    private TerrainElevation _elevation;
    private Node2D _objects;
    private SurfaceWorld _surfaces;

    private readonly Dictionary<Vector2I, PlacedObject> _occupied = new();
    private readonly ConvexPolygonShape2D _shape = new();
    private readonly PhysicsShapeQueryParameters2D _query = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Reuse one placement service under the existing world Systems node.
    public static PlacementWorld Ensure(Node context)
    {
        Node generator = context.GetTree().GetFirstNodeInGroup("world_generator");
        if (generator == null) return null;

        Node systems = generator.GetParent();
        PlacementWorld existing =
            systems.GetNodeOrNull<PlacementWorld>("PlacementWorld");
        if (existing != null) return existing;

        Node world = systems.GetParent();
        ChunkController chunks = systems.GetNode<ChunkController>("ChunkController");

        PlacementWorld service = new()
        {
            Name = "PlacementWorld",
            _chunks = chunks,
            _elevation = systems.GetNode<TerrainElevation>("TerrainElevation"),
            _objects = world.GetNode<Node2D>("WorldObjects"),
            Grid = new BuildingGrid(
                world.GetNode<Node2D>("GroundChunks"), chunks.TileSize)
        };

        systems.AddChild(service);
        return service;
    }

// =========================================================
// Query solids, actors, gaps, and plant footprints; grass does not block placement.
public override void _Ready()
{
    _query.Shape = _shape;
    _query.CollisionMask = 1u | 2u | 4u | 8u |
        VegetationPlacement.PlantLayer;
    _query.CollideWithBodies = true;
    _query.CollideWithAreas = true;
    SetProcess(false);
    SetPhysicsProcess(false);
}

    // =========================================================
    // Release native query resources when the world closes.
    public override void _ExitTree()
    {
        _query.Dispose();
        _shape.Dispose();
        _occupied.Clear();
    }
    #endregion

    #region Targeting
    // =========================================================
    // Return surface elevation for grid lines and artwork.
    public float HeightAt(Vector2 world)
    {
        return _elevation.SampleWorldHeight(world);
    }

    // =========================================================
    // Unproject the visible cursor onto terrain before snapping.
    public bool TryCursorCell(Vector2 cursor, out Vector2I cell)
    {
        Vector2 ground = cursor;
        bool converged = false;

        for (int i = 0; i < 24; i++)
        {
            Vector2 next = cursor + Vector2.Down * HeightAt(ground);
            if (next.DistanceSquaredTo(ground) < 0.01f)
            {
                ground = next;
                converged = true;
                break;
            }
            ground = next;
        }

        cell = Grid.CellAt(ground);
        return converged;
    }
    #endregion

    #region Validation
    // =========================================================
    // Check the entire footprint using shared terrain and collision rules.
    public bool CanPlace(
        Player player, PlaceableDefinition definition, Vector2I anchor,
        float reach, out string reason)
    {
        reason = "";
        if (WorldLayerMember.For(player) != WorldLayerId.Surface)
        {
            reason = "Surface placement only";
            return false;
        }

        Vector2 centre = Grid.Centre(anchor, definition.Cells);
        Vector2[] corners = Grid.Corners(anchor, definition.Cells);
        float maximumDistance = Mathf.Max(0f, reach);
        float distanceSquared = maximumDistance * maximumDistance;

        // Include the farthest footprint corner, not just its centre.
        foreach (Vector2 corner in corners)
            if (player.GlobalPosition.DistanceSquaredTo(corner) > distanceSquared)
            {
                reason = "Too far away";
                return false;
            }

        for (int y = 0; y < definition.Cells.Y; y++)
        for (int x = 0; x < definition.Cells.X; x++)
        {
            Vector2I cell = anchor + new Vector2I(x, y);
            if (_occupied.TryGetValue(cell, out PlacedObject owner) &&
                GodotObject.IsInstanceValid(owner) &&
                !owner.IsQueuedForDeletion())
            {
                reason = "Occupied";
                return false;
            }
        }

        _surfaces ??= SurfaceWorld.Find(this);
        float lowest = float.PositiveInfinity;
        float highest = float.NegativeInfinity;

        // Half-cell sampling includes all centres, corners, and edge midpoints.
        for (int y = 0; y <= definition.Cells.Y * 2; y++)
        for (int x = 0; x <= definition.Cells.X * 2; x++)
        {
            Vector2 tile = new(
                anchor.X - 0.5f + x * 0.5f,
                anchor.Y - 0.5f + y * 0.5f);
            Vector2 point = Grid.ToWorld(tile);

            if (!_chunks.IsNavigationPointAvailable(point, 2f))
            {
                reason = "Ground unavailable or unsafe";
                return false;
            }

            if (_surfaces != null &&
                _surfaces.Sample(point).SubmersionPixels > 0.1f)
            {
                reason = "Liquid or unsuitable surface";
                return false;
            }

            float height = HeightAt(point);
            lowest = Mathf.Min(lowest, height);
            highest = Mathf.Max(highest, height);

            if (highest - lowest > definition.MaximumHeightDifference)
            {
                reason = "Ground too uneven";
                return false;
            }
        }

        Vector2[] local = new Vector2[corners.Length];
        for (int i = 0; i < local.Length; i++)
            local[i] = (corners[i] - centre) * 0.98f;

        _shape.Points = local;
        _query.Transform = new Transform2D(0f, centre);

        if (player.GetWorld2D().DirectSpaceState.IntersectShape(_query, 1).Count > 0)
        {
            reason = "Object or actor in the way";
            return false;
        }

        return true;
    }
    #endregion

    #region Placement
// =========================================================
// Revalidate, consume one item, place the object, and clear its overlapping grass.
public bool TryPlace(
    Player player, ItemDefinition item, InventoryAddress address,
    Vector2I anchor, float reach)
{
    PlaceableDefinition definition = item?.Placeable;
    if (definition == null ||
        !CanPlace(player, definition, anchor, reach, out _))
        return false;

    Node instance = definition.WorldScene.Instantiate();
    if (instance is not PlacedObject placed ||
        placed.GetNodeOrNull<Health>("Systems/Health") == null)
    {
        instance.Free();
        GD.PushError(
            "Placeable world scene requires PlacedObject and Systems/Health.");
        return false;
    }

    placed.Configure(this, definition, anchor, item);
    placed.Position = _objects.ToLocal(Grid.Centre(anchor, definition.Cells));

    PlayerInventory inventory =
        player.GetNode<PlayerInventory>("Systems/Inventory");

    if (!inventory.TryTakeOne(address, item))
    {
        placed.Free();
        return false;
    }

    _objects.AddChild(placed);
    Register(placed, anchor, definition.Cells);
    ClearGrass(anchor, definition.Cells);
    return true;
}

    // =========================================================
    // Restore the saved building without charging inventory or clearing grass a second time.
    public void RestoreSaved(StructureSaveData saved, ItemDefinition item)
    {
        PlaceableDefinition definition = item.Placeable
            ?? throw new System.IO.InvalidDataException("Saved building item is no longer placeable.");
        definition.Validate();
        if (definition.WorldScene.ResourcePath != saved.WorldScene ||
            definition.ArtworkScene.ResourcePath != saved.ArtworkScene ||
            definition.Cells != new Vector2I(saved.Width, saved.Depth))
            throw new System.IO.InvalidDataException("Saved building recipe changed.");
        Vector2I anchor = new(saved.X, saved.Y);
        for (int y = 0; y < saved.Depth; y++)
        for (int x = 0; x < saved.Width; x++)
            if (_occupied.ContainsKey(anchor + new Vector2I(x, y)))
                throw new System.IO.InvalidDataException("Saved building footprints overlap.");
        Node instance = definition.WorldScene.Instantiate();
        if (instance is not PlacedObject placed || placed.GetNodeOrNull<Health>("Systems/Health") == null)
        {
            instance.Free();
            throw new System.IO.InvalidDataException("Saved building scene is invalid.");
        }
        placed.Configure(this, definition, anchor, item, saved.Id);
        placed.Position = _objects.ToLocal(Grid.Centre(anchor, definition.Cells));
        _objects.AddChild(placed);
        Register(placed, anchor, definition.Cells);
        placed.ObjectHealth.RestoreState(saved.Health);
    }

    // =========================================================
    // Reserve every occupied cell without splitting larger footprints.
    private void Register(PlacedObject owner, Vector2I anchor, Vector2I cells)
    {
        for (int y = 0; y < cells.Y; y++)
        for (int x = 0; x < cells.X; x++)
            _occupied[anchor + new Vector2I(x, y)] = owner;
    }

    // =========================================================
    // Release only cells still owned by the departing object.
    public void Unregister(PlacedObject owner, Vector2I anchor, Vector2I cells)
    {
        for (int y = 0; y < cells.Y; y++)
        for (int x = 0; x < cells.X; x++)
        {
            Vector2I cell = anchor + new Vector2I(x, y);
            if (_occupied.TryGetValue(cell, out PlacedObject current) &&
                current == owner)
                _occupied.Remove(cell);
        }
    }

    // =========================================================
// Remove overlapping grass only after the building transaction succeeds.
private void ClearGrass(Vector2I anchor, Vector2I cells)
{
    Vector2 centre = Grid.Centre(anchor, cells);
    Vector2[] corners = Grid.Corners(anchor, cells);

    for (int i = 0; i < corners.Length; i++)
        corners[i] -= centre;

    _shape.Points = corners;
    _query.Transform = new Transform2D(0f, centre);

    uint previousMask = _query.CollisionMask;
    _query.CollisionMask = VegetationPlacement.GrassLayer;

    try
    {
        var hits = _objects.GetWorld2D().DirectSpaceState
            .IntersectShape(_query, 4096);

        foreach (var hit in hits)
        {
            if (hit["collider"].AsGodotObject()
                is not VegetationPlacement vegetation ||
                !vegetation.IsGrass ||
                !GodotObject.IsInstanceValid(vegetation.Host) ||
                vegetation.Host.IsQueuedForDeletion())
                continue;

            if (vegetation.Host is Grass grass) grass.Clear();
        }
    }
    finally
    {
        _query.CollisionMask = previousMask;
    }
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
                "Entity state and underground player restoration are later passes.");
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
$Changes['SYSTEMS/Saving/WorldObjectSaves.cs'] = @'
// Snapshots physical world items, containers, wrecks and placed structures.
// Item resources are resolved by catalog ID or an external resource reference.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class WorldObjectsData
{
    public int Version { get; set; } = 1;
    public Dictionary<string, string> ItemResources { get; set; } = new();
    public List<string> Recipes { get; set; } = new();
    public List<DropSaveData> Drops { get; set; } = new();
    public Dictionary<string, List<StackSaveData>> Storage { get; set; } = new();
    public Dictionary<string, List<StackSaveData>> Loot { get; set; } = new();
    public List<WreckSaveData> Wrecks { get; set; } = new();
    public List<StructureSaveData> Structures { get; set; } = new();
    public List<SceneHealthSaveData> SceneHealth { get; set; } = new();
}
public sealed class DropSaveData
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public string Item { get; set; } = "";
    public int Count { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float PickupRadius { get; set; }
    public double PickupDelay { get; set; }
    public double? RemainingLifetimeSeconds { get; set; }
}
public sealed class WreckSaveData
{
    public string Id { get; set; } = "";
    public string Layer { get; set; } = WorldLayerId.Surface;
    public float X { get; set; }
    public float Y { get; set; }
}
public sealed class StructureSaveData
{
    public string Id { get; set; } = "";
    public string Item { get; set; } = "";
    public string WorldScene { get; set; } = "";
    public string ArtworkScene { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Depth { get; set; }
    public int Health { get; set; }
}
public sealed class SceneHealthSaveData
{
    public string Host { get; set; } = "";
    public string HealthPath { get; set; } = "";
    public int Current { get; set; }
}

public partial class WorldObjectSaves : Node
{
    #region State
    public const string Section = "world-objects";
    private WorldObjectsData _saved = new();
    private readonly Dictionary<string, InventoryStorage> _storage = new();
    private readonly Dictionary<string, SceneHealthSaveData> _health = new();
    private readonly HashSet<string> _recipes = new();
    private bool _buildingsRestored, _dropsRestored;
    public WorldObjectsData Saved => _saved;
    public Exception StartupError { get; private set; }
    #endregion

    #region Ownership
    // =========================================================
    // Resolve only the helper belonging to this world.
    public static WorldObjectSaves Find(Node context)
    {
        return WorldConfig.TryFind(context)?.GetParent().GetNodeOrNull<WorldObjectSaves>("WorldObjectSaves");
    }

    // =========================================================
    // Reuse a non-processing helper in Sandbox and direct scene tests too.
    public static WorldObjectSaves Ensure(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        WorldObjectSaves saves = Find(context);
        if (saves != null) return saves;
        saves = new WorldObjectSaves { Name = "WorldObjectSaves" };
        world.AddChild(saves);
        return saves;
    }

    // =========================================================
    // Persistence itself adds no per-frame work.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Surface errors from child Ready callbacks before Campaign can enable play or saving.
    public void ReportLoadFailure(Exception error)
    {
        StartupError ??= error;
    }
    #endregion

    #region Format Validation
    // =========================================================
    // Validate the named section before a save can replace the current file.
    public static WorldObjectsData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version <= 2) return new WorldObjectsData();
            throw new InvalidDataException("Missing world-object save section.");
        }
        WorldObjectsData saved = section.Deserialize<WorldObjectsData>();
        if (saved == null || saved.Version != 1 || saved.ItemResources == null ||
            saved.Recipes == null || saved.Drops == null || saved.Storage == null ||
            saved.Loot == null || saved.Wrecks == null || saved.Structures == null || saved.SceneHealth == null ||
            saved.ItemResources.Count > 4096 || saved.Recipes.Count > 4096 ||
            saved.Drops.Count + saved.Storage.Count + saved.Loot.Count + saved.Wrecks.Count + saved.Structures.Count + saved.SceneHealth.Count > 100000)
            throw new InvalidDataException("Invalid world-object save section.");
        foreach (var item in saved.ItemResources)
            if (string.IsNullOrWhiteSpace(item.Key) || !ResourcePath(item.Value, ".tres"))
                throw new InvalidDataException("Invalid saved item reference.");
        foreach (string recipe in saved.Recipes)
            if (!ResourcePath(recipe)) throw new InvalidDataException("Invalid saved object recipe.");
        HashSet<string> ids = new();
        foreach (DropSaveData drop in saved.Drops)
        {
            if (drop == null || !Guid.TryParseExact(drop.Id, "N", out _) || !ids.Add(drop.Id) ||
                string.IsNullOrWhiteSpace(drop.Item) || drop.Count <= 0 ||
                !float.IsFinite(drop.X) || !float.IsFinite(drop.Y) ||
                !float.IsFinite(drop.PickupRadius) || drop.PickupRadius <= 0 ||
                !double.IsFinite(drop.PickupDelay) || drop.PickupDelay < 0 ||
                (drop.RemainingLifetimeSeconds is double lifetime && (!double.IsFinite(lifetime) || lifetime < 0)))
                throw new InvalidDataException("Invalid saved drop.");
            WorldLayerId.Validate(drop.Layer);
        }
        ids.Clear();
        foreach (WreckSaveData wreck in saved.Wrecks)
        {
            if (wreck == null || string.IsNullOrWhiteSpace(wreck.Id) || !ids.Add(wreck.Id) ||
                !float.IsFinite(wreck.X) || !float.IsFinite(wreck.Y))
                throw new InvalidDataException("Invalid saved wreck.");
            WorldLayerId.Validate(wreck.Layer);
        }
        ids.Clear();
        foreach (StructureSaveData structure in saved.Structures)
            if (structure == null || !Guid.TryParseExact(structure.Id, "N", out _) || !ids.Add(structure.Id) ||
                string.IsNullOrWhiteSpace(structure.Item) || !ResourcePath(structure.WorldScene, ".tscn") ||
                !ResourcePath(structure.ArtworkScene, ".tscn") || structure.Health < 1 ||
                structure.Width < 1 || structure.Width > 8 || structure.Depth < 1 || structure.Depth > 8)
                throw new InvalidDataException("Invalid saved structure.");
        ids.Clear();
        foreach (SceneHealthSaveData health in saved.SceneHealth)
            if (health == null || !LocalPath(health.Host) || !health.Host.StartsWith("WorldObjects/", StringComparison.Ordinal) ||
                !LocalPath(health.HealthPath) || !ids.Add(health.Host + "/" + health.HealthPath) || health.Current < 0)
                throw new InvalidDataException("Invalid scene-object health.");
        foreach (var contents in saved.Storage.Concat(saved.Loot))
        {
            if (string.IsNullOrWhiteSpace(contents.Key) || contents.Key.Length > 2048 ||
                contents.Value == null || contents.Value.Count < 1 || contents.Value.Count > 96)
                throw new InvalidDataException("Invalid saved container layout.");
            foreach (StackSaveData stack in contents.Value)
                if (stack == null || stack.Item == null || stack.Count < 0 ||
                    (stack.Item.Length == 0) != (stack.Count == 0))
                    throw new InvalidDataException("Invalid saved container stack.");
        }
        return saved;
    }

    // =========================================================
    // Keep references inside the project and separate from embedded resource IDs.
    private static bool ResourcePath(string path, string extension = null)
    {
        return !string.IsNullOrEmpty(path) && path.StartsWith("res://", StringComparison.Ordinal) &&
            !path.Contains("::") && !path.Contains("..") && (extension == null || path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    // =========================================================
    // Allow only ordinary world-relative node paths.
    private static bool LocalPath(string path)
    {
        return !string.IsNullOrEmpty(path) && !path.StartsWith('/') && !path.Contains(':') &&
            !path.Split('/').Any(part => part is "" or "." or "..");
    }
    #endregion

    #region Startup
    // =========================================================
    // Initialize before containers enter Ready and remove destroyed scene originals.
    public void Initialize(CampaignData data, Node world)
    {
        _saved = ReadSection(data);
        foreach (string recipe in _saved.Recipes) _recipes.Add(recipe);
        foreach (SceneHealthSaveData health in _saved.SceneHealth)
        {
            _health.Add(health.Host + "/" + health.HealthPath, health);
            if (health.Current != 0) continue;
            Node host = world.GetNodeOrNull(health.Host);
            if (host != null) { host.GetParent().RemoveChild(host); host.Free(); }
        }
    }

    // =========================================================
    // Restore structures before natural objects and navigation populate their footprints.
    public void RestoreBuildings(Node world)
    {
        if (StartupError != null) throw StartupError;
        if (_buildingsRestored) return;
        if (_saved.Wrecks.Count > 0 || _saved.Loot.Count > 0) LootWorld.GetOrCreate(world);
        if (StartupError != null) throw StartupError;
        foreach (StructureSaveData structure in _saved.Structures)
            PlacementWorld.Ensure(world).RestoreSaved(structure, Resolve(structure.Item));
        TrackSceneHealth(world.GetNode("WorldObjects"));
        _buildingsRestored = true;
    }

    // =========================================================
    // Restore drop ownership once, after player state is ready and while actions are frozen.
    public void RestoreDrops(Node context)
    {
        if (_dropsRestored) return;
        ResourceWorld resources = ResourceWorld.Find(context);
        foreach (DropSaveData drop in _saved.Drops)
        {
            if (drop.RemainingLifetimeSeconds == 0) continue;
            resources.RestoreDrop(drop, Resolve(drop.Item));
        }
        _dropsRestored = true;
    }
    #endregion

    #region Item Codec
    // =========================================================
    // Remember external items absent from the master catalog, such as starter tools.
    private void Remember(ItemDefinition item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Id))
            throw new InvalidDataException("World items need a stable item identity.");
        if (string.IsNullOrEmpty(item.ResourcePath)) return;
        if (!ResourcePath(item.ResourcePath, ".tres"))
            throw new InvalidDataException("Saved items need external resources or catalog identities.");
        _saved.ItemResources[item.Id] = item.ResourcePath;
        _recipes.Add(item.ResourcePath);
    }

    // =========================================================
    // Resolve the catalog first and check external resource identities before using them.
    public ItemDefinition Resolve(string id)
    {
        ItemDefinition item = ResourceWorld.Find(this).Catalog.Get(id);
        if (item != null) return item;
        if (!_saved.ItemResources.TryGetValue(id, out string path))
            throw new InvalidDataException("Saved world item is unavailable: " + id);
        item = GD.Load<ItemDefinition>(path);
        if (item?.Id != id) throw new InvalidDataException("Saved world-item identity changed: " + id);
        return item;
    }

    // =========================================================
    // Copy exact physical slots, including empty slots and external item references.
    public List<StackSaveData> Encode(InventoryStorage contents)
    {
        List<StackSaveData> slots = new();
        for (int i = 0; i < contents.SlotCount; i++)
        {
            InventoryStack stack = contents.Get(i);
            if (!stack.IsEmpty) Remember(stack.Item);
            slots.Add(new StackSaveData { Item = stack.Item?.Id ?? "", Count = stack.Count });
        }
        return slots;
    }

    // =========================================================
    // Stage a container without collection, rerolling loot or merging its saved slots.
    public InventoryStorage Decode(List<StackSaveData> slots)
    {
        InventoryStorage contents = new(slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            StackSaveData stack = slots[i];
            if (stack.Count == 0) continue;
            ItemDefinition item = Resolve(stack.Item);
            if (stack.Count > Math.Max(1, item.MaxStack))
                throw new InvalidDataException("Saved container stack exceeds its item limit.");
            contents.Set(i, new InventoryStack(item, stack.Count));
        }
        return contents;
    }

    // =========================================================
    // Include embedded definitions through their containing scene or resource file.
    public void RememberRecipe(Resource resource)
    {
        string path = resource?.ResourcePath?.Split("::")[0];
        if (!string.IsNullOrEmpty(path)) _recipes.Add(path);
    }
    #endregion

    #region Containers
    // =========================================================
    // Separate explicit IDs, placed-object GUIDs and scene paths inside each layer.
    public static string StorageKey(WorldStorage storage)
    {
        string layer = WorldLayerMember.For(storage.Host);
        if (!string.IsNullOrWhiteSpace(storage.PersistentId)) return layer + "/id/" + storage.PersistentId;
        for (Node node = storage.Host; node != null; node = node.GetParent())
            if (node is PlacedObject placed)
                return layer + "/placed/" + placed.PersistentId + "/" + placed.GetPathTo(storage);
        return layer + "/scene/" + WorldConfig.Find(storage).GetParent().GetPathTo(storage);
    }

    // =========================================================
    // Restore ordinary storage before its first interaction; empty contents remain empty.
    public void RestoreStorage(WorldStorage storage)
    {
        string key = StorageKey(storage);
        RememberRecipe(storage.Definition);
        if (!_storage.TryGetValue(key, out InventoryStorage contents) && _saved.Storage.TryGetValue(key, out List<StackSaveData> slots))
            contents = Decode(slots);
        if (contents != null) storage.ImportContents(contents);
        else StoreStorage(storage);
    }

    // =========================================================
    // Retain committed storage contents independently of its visible host.
    public void StoreStorage(WorldStorage storage)
    {
        if (storage is LootContainer) return;
        _storage[StorageKey(storage)] = storage.ExportContents();
    }
    #endregion

    #region Scene Health
    // =========================================================
    // Track scene world-object health while leaving actors and placed structures to their owners.
    private void TrackSceneHealth(Node node)
    {
        if (node is Player or Entity or PlacedObject) return;
        if (node is Health health)
        {
            Node2D host = null;
            for (Node ancestor = health.GetParent(); ancestor != null; ancestor = ancestor.GetParent())
                if (ancestor is Node2D actor) { host = actor; break; }
            if (host != null)
            {
                Node world = GetParent();
                if (!string.IsNullOrEmpty(host.SceneFilePath)) _recipes.Add(host.SceneFilePath);
                string hostPath = world.GetPathTo(host).ToString(), healthPath = host.GetPathTo(health).ToString();
                string key = hostPath + "/" + healthPath;
                if (_health.TryGetValue(key, out SceneHealthSaveData saved) && saved.Current > 0)
                    health.RestoreState(saved.Current);
                SceneHealthSaveData state = new() { Host = hostPath, HealthPath = healthPath, Current = health.Current };
                _health[key] = state;
                health.Changed += (current, maximum) => state.Current = current;
            }
        }
        foreach (Node child in node.GetChildren()) TrackSceneHealth(child);
    }
    #endregion

    #region Capture
    // =========================================================
    // Snapshot live and deferred drops, cached container contents, wrecks and surviving structures.
    public void Capture(CampaignData data)
    {
        Node world = GetParent();
        _saved.Drops.Clear(); _saved.Structures.Clear();
        HashSet<string> dropIds = new();
        foreach (WorldPickup drop in ResourceWorld.Find(this).SaveDrops())
        {
            if (!GodotObject.IsInstanceValid(drop) || drop.IsQueuedForDeletion() || drop.Count <= 0 || drop.RemainingLifetimeSeconds == 0) continue;
            if (!dropIds.Add(drop.PersistentId)) throw new InvalidDataException("Duplicate dropped-item identity.");
            Remember(drop.Item);
            _saved.Drops.Add(drop.CaptureSave());
        }
        HashSet<string> storageIds = new();
        foreach (Node node in GetTree().GetNodesInGroup("world_storage"))
        {
            if (node is not WorldStorage storage || !world.IsAncestorOf(storage) || storage.IsQueuedForDeletion() ||
                !GodotObject.IsInstanceValid(storage.Host) || storage.Host.IsQueuedForDeletion()) continue;
            if (!storageIds.Add(StorageKey(storage))) throw new InvalidDataException("Duplicate world-container identity.");
            RememberRecipe(storage.Definition);
            StoreStorage(storage);
        }
        foreach (var storage in _storage) _saved.Storage[storage.Key] = Encode(storage.Value);
        LootWorld.Find(this)?.CaptureObjects(this, _saved);
        foreach (Node node in GetTree().GetNodesInGroup("world_obstacles"))
        {
            if (node is not PlacedObject placed || !world.IsAncestorOf(placed) || placed.IsQueuedForDeletion() || !placed.ObjectHealth.IsAlive) continue;
            Remember(placed.SourceItem);
            RememberRecipe(placed.Definition.WorldScene);
            RememberRecipe(placed.Definition.ArtworkScene);
            _saved.Structures.Add(placed.CaptureSave());
        }
        _saved.SceneHealth = _health.Values.ToList();
        _saved.Recipes = _recipes.OrderBy(path => path, StringComparer.Ordinal).ToList();
        data.Sections[Section] = JsonSerializer.SerializeToElement(_saved);
        data.Version = 3; data.Coverage = "world-player-resources-objects";
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
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '8c7182db333ddcba7afc46a190b3e13306b3f32323de5ec67a03dc86934f12ee'
$Expected['SYSTEMS/Saving/CampaignRecipe.cs'] = '9b68ebc8a8439e34e6cad71f449e9cc6248f040b2972468d1ff0b64ba76a423d'
$Expected['SYSTEMS/Saving/CampaignStore.cs'] = 'c44c144b1617a399e60a0ec87591c2011b82f63560bc045363dae9774746a541'
$Expected['ITEMS/World/WorldPickup.cs'] = '87d0e9d2887b83421f6fb4a52c8bc7da40eca7c597266933b07d65f12ccca3d7'
$Expected['ITEMS/World/ResourceWorld.cs'] = 'd0b01b0062cdb679ab81b9cf4d66c25ff08a2cd955a128e553aca8c4e9a3907b'
$Expected['SYSTEMS/Storage/WorldStorage.cs'] = 'd8b74190ebcc80828088fcab266cfb7debe0ba1552cdb9c83fc500455f293c30'
$Expected['SYSTEMS/Loot/LootContainer.cs'] = '244875007a64951dbbe6a49bd96afb27a3171ec2a931a9862066a18dbc06567c'
$Expected['SYSTEMS/Loot/LootWorld.cs'] = '8d6ea656c0f1d8e7b99685aaa47bbf1d8eb2882bd1cfd83ea1b1df8026083317'
$Expected['SYSTEMS/Placement/PlacedObject.cs'] = '1b68e745077ce8d966fd931fda81676bb6d7a210a9d3ce178635b5bfa29cfb30'
$Expected['SYSTEMS/Placement/PlacementWorld.cs'] = '572c7f7ed271ab1445df349b44e0bd0265bba65956eddbc990221ffe348560f2'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = 'bc88482ec67414dc6efe97c89ce29a8700291241c7cb2f909729280a420b37fd'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = '92f6e791ecd1a9a14b70e7e5b71378a6497daa99505e810ae7dce58e7cd1014f'
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
if (!$Pending.Count) { Write-Host 'Pass 4 is already installed. No files changed.'; return }
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
Write-Host 'Test drops, container contents, loot and damaged buildings; save, quit, then Continue.'
Write-Host 'Existing saves upgrade on next Save. Lifetime is stored only; expiry is not implemented.'
