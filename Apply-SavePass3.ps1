# Installs save Pass 3: generated resource changes. Based on push 879bc6a. No Git operations.
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
        ResourceChanges.Find(this).Capture(_data);
        CampaignRecipe.CaptureResourceDefinitions(_data);
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
              data.Version == 2 && data.Coverage == "world-player-resources") ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null ||
            (data.Version == 1 ? data.Sections.Count != 0 :
                data.Sections.Count != 1 || !data.Sections.ContainsKey(ResourceChanges.Section)))
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        ResourceChanges.ReadSection(data);
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
$Changes['SYSTEMS/Harvesting/ResourceHarvest.cs'] = @'
// Provides finite extraction for rocks/trees and one-shot gathering for plants.
// State belongs to the instance; shared definitions contain only configuration.
using Godot;

public partial class ResourceHarvest : Area2D
{
    #region Configuration
    public WorldObjectDefinition Definition { get; set; }
    public string DefaultItemId { get; set; }
    public bool GatherByHand { get; set; }
    #endregion

    #region State
    private Node2D _host;
    private ResourceWorld _resources;
    private ResourceChanges _changes;
    private float _work;
    private bool _depleted;
    public int RequiredStrength => Definition.RequiredMiningStrength;
    #endregion

    #region Attachment
    // =========================================================
    // Attach one reusable helper without adding per-frame work to resource objects.
    public static void Attach(Node2D host, WorldObjectDefinition definition,
        string defaultItemId, bool gatherByHand = false)
    {
        host.AddChild(new ResourceHarvest
        {
            Name = "Harvest",
            Definition = definition,
            DefaultItemId = defaultItemId,
            GatherByHand = gatherByHand
        });
    }

    // =========================================================
    // Configure plant query areas; mining continues using the host's solid collision.
    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _resources = ResourceWorld.Find(this);
        _changes = ResourceChanges.Ensure(_host);
        string kind = _host is Tree ? "tree" : _host is Rock ? "rock" : "plant";
        _changes.BindScene(_host, Definition, kind);
        ResourceChangeData saved = _changes.Get(_host);
        if (saved != null)
        {
            _work = Mathf.Min(Mathf.Max(0.1f, Definition.HarvestWork), saved.Work);
            _depleted = saved.Depleted;
            if (_depleted) { _host.QueueFree(); return; }
        }
        CollisionLayer = GatherByHand ? 32u : 0u;
        CollisionMask = 0;
        Monitoring = false;
        Monitorable = GatherByHand;

        if (GatherByHand)
            AddChild(new CollisionShape2D
            {
                Shape = new CircleShape2D { Radius = 8f }
            });

        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Harvesting
    // =========================================================
    // Accumulate work only when the tool meets this resource's strength requirement.
    public bool Mine(int strength, float power)
    {
        if (GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            strength < RequiredStrength || power <= 0f || !float.IsFinite(power)) return false;

        float required = Mathf.Max(0.1f, Definition.HarvestWork);
        _work = Mathf.Min(required, _work + power);
        _changes.Record(_host, _work);
        return _work < required || Finish();
    }

    // =========================================================
    // Require empty hands and close proximity; plants yield one fiber in this pass.
    public bool Gather(Player player, float range)
    {
        if (!GatherByHand || _depleted || _host.IsQueuedForDeletion() ||
            !WorldLayerMember.Same(player, _host) ||
            player.GetNode<PlayerEquipment>("Systems/Equipment").CurrentTool != null ||
            player.GlobalPosition.DistanceSquaredTo(_host.GlobalPosition) >
                range * range) return false;
        return Finish();
    }

// =========================================================
// Spawn primary and bonus rewards together before removing the source.
private bool Finish()
{
    if (_depleted || !GodotObject.IsInstanceValid(_resources)) return false;

    string id = string.IsNullOrWhiteSpace(Definition.HarvestItemId)
        ? DefaultItemId : Definition.HarvestItemId;
    int units = GatherByHand ? 1 : Mathf.Max(1, Definition.HarvestUnits);

    if (!_resources.SpawnHarvest(id, units,
        Definition.BonusHarvestItems, _host.GlobalPosition, _host)) return false;

    _depleted = true;
    _changes.Record(_host, depleted: true);
    _host.QueueFree();
    return true;
}
    #endregion
}
'@
$Changes['WORLD/Contents/Shared/Obstacle.cs'] = @'
// Shares footprint collision and cached shadows for solid world objects.
// Trees and rocks override configuration/artwork while navigation sees one base type.
using Godot;
using System.Threading.Tasks;

public partial class Obstacle : StaticBody2D
{
	#region Configuration
	public enum ObstacleKind { Rock, Crate }

	[ExportGroup("Obstacle")]
	[Export] public ObstacleKind Kind { get; set; } = ObstacleKind.Rock;
	[Export] public Vector2 Footprint { get; set; } = new(96, 48);
	[Export] public float Height { get; set; } = 72f;
	[Export] public int RockVariant { get; set; } = -1;
	[Export] public VisualDefinition VisualOverride { get; set; }
	#endregion

	#region Lifecycle
	// =========================================================
	// Register solids so placement queries skip plants, grass and other walkable nodes.
	public override void _EnterTree()
	{
		AddToGroup("world_obstacles");
	}

// =========================================================
// Build collision/artwork and wait for all world systems before creating shadows.
public override async void _Ready()
{
	try
	{
		ConfigureInstance();
        if (IsQueuedForDeletion()) return;
		Footprint = new Vector2(
			Mathf.Max(1f, Footprint.X), Mathf.Max(1f, Footprint.Y));
		CollisionLayer = 1;
		CollisionMask = 0;

		AddChild(new CollisionShape2D
		{
			Name = "Footprint",
			Shape = new RectangleShape2D { Size = Footprint }
		});

		await AttachArtworkAsync();
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

		WorldAtmosphere atmosphere = GetTree().GetFirstNodeInGroup(
			"world_atmosphere") as WorldAtmosphere;

		if (GodotObject.IsInstanceValid(atmosphere))
		{
			Node systems = atmosphere.GetParent();
			if (!systems.IsNodeReady())
				await ToSignal(systems, Node.SignalName.Ready);

			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion() ||
				!GodotObject.IsInstanceValid(atmosphere)) return;

			atmosphere.CreateObstacleShadow(this);
		}

		SetProcess(false);
	}
	catch (System.Exception error)
	{
		GD.PushError($"Obstacle '{Name}' initialization failed: {error}");
	}
}

	// =========================================================
	// Let a derived family configure its footprint before collision is created.
	protected virtual void ConfigureInstance()
	{
	}
	#endregion

	#region Artwork
	// =========================================================
	// Preserve the existing crate and generic obstacle visual behaviour.
	protected virtual async Task AttachArtworkAsync()
	{
		await PlaceholderAtlas.EnsureReady(this);
		if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

		int variant = RockVariant;
		if (variant < 0)
		{
			variant = (int)(IsoGrid.Hash(
				Mathf.RoundToInt(GlobalPosition.X),
				Mathf.RoundToInt(GlobalPosition.Y), 64127u)
				% (uint)RockDrawing.VariantCount);
		}

		Rect2 region = Kind == ObstacleKind.Rock
			? PlaceholderAtlas.GetRockRegion(variant)
			: PlaceholderAtlas.CrateRegion;

		TerrainVisual.Attach(
			this, region, new Vector2(-80, -120),
			new Vector2(Footprint.X / 96f, Height / 72f),
			false, VisualOverride);
	}
	#endregion
}
'@
$Changes['WORLD/Contents/Vegetation/Plants/Plant.cs'] = @'
// Represents any walkable plant species using its settings and chosen instance variation.
// Imported visuals and baked fallback artwork share the same instance scale.
using Godot;

public partial class Plant : Node2D
{
    #region Configuration
    [ExportGroup("Plant")]
    [Export] public PlantDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Lifecycle
// =========================================================
// Attach replaceable plant artwork and its automatically shared shadows.
public override async void _Ready()
{
    try
    {
        if (Definition == null)
            throw new System.InvalidOperationException(
                "Plant requires a PlantDefinition.");

        VegetationPlacement.Attach(
            this, Definition.PlacementFootprint *
                Mathf.Max(0.1f, SizeMultiplier), false);

        ResourceHarvest.Attach(this, Definition, "plant_fiber", true);
        if (IsQueuedForDeletion()) return;

        await VegetationAtlas.EnsureReady(this);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

        TerrainVisual visual = TerrainVisual.Attach(
            this,
            VegetationAtlas.GetRegion(
                Definition.BakedKind == PlantArtwork.Shrub, Variant),
            VegetationAtlas.Origin, Vector2.One, false,
            Definition.Visual, VegetationAtlas.Texture,
            VegetationAtlas.WindMaterial, Variant);

        float size = Mathf.Max(0.1f, SizeMultiplier);
        visual.Scale = new Vector2(Mirror ? -size : size, size);
        SetProcess(false);
    }
    catch (System.Exception error)
    {
        GD.PushError($"Plant '{Name}' artwork failed: {error}");
    }
}
    #endregion
}
'@
$Changes['WORLD/Contents/Ores/OreDeposit.cs'] = @'
// Represents a mineable ore deposit with shared replacement artwork.
// Selects stable PNG variations and only bakes artwork when needed.
using Godot;
using System;
using System.Threading.Tasks;

public partial class OreDeposit : Obstacle, IMiningTarget
{
	#region Configuration
	[ExportGroup("Deposit")]
	[Export] public OreDefinition Definition { get; set; }
	[Export] public float InstanceSize { get; set; } = 1f;

	[ExportGroup("Artwork Variation")]
	// -1 selects a stable variation from the deposit's world position.
    // 0 and above select a particular image from the sorted PNG folder.
    [Export(PropertyHint.Range, "-1,255,1,or_greater")]
    public int ArtworkVariant { get; set; } = -1;
    #endregion

    #region State
    public int RemainingUnits { get; private set; }
    public int RequiredStrength => Definition.RequiredMiningStrength;

    private float _work;
    private ItemDefinition _yieldItem;
    private ResourceChanges _changes;
    #endregion

    #region Setup
    // =========================================================
	// Resolve the mining reward and configure the deposit's physical dimensions.
	protected override void ConfigureInstance()
	{
		if (Definition == null)
			throw new InvalidOperationException(
				"OreDeposit requires an OreDefinition.");

		_yieldItem = Definition.YieldItem;

		if (_yieldItem == null &&
			!string.IsNullOrWhiteSpace(Definition.YieldItemId))
		{
			ItemCatalog catalog = GD.Load<ItemCatalog>(
				"res://ITEMS/ItemCatalog.tres");

			if (catalog == null)
				throw new InvalidOperationException(
					"OreDeposit requires the master ItemCatalog.");

			_yieldItem = catalog.Get(Definition.YieldItemId);

			if (_yieldItem == null)
			{
				catalog.Initialize();
				_yieldItem = catalog.Get(Definition.YieldItemId);
			}
		}

		if (_yieldItem == null)
			throw new InvalidOperationException(
				$"OreDeposit has an unknown yield: '{Definition.YieldItemId}'.");

		InstanceSize = Mathf.Max(0.1f, InstanceSize);
		Footprint = Definition.Footprint * InstanceSize;
		Height = Definition.Height * InstanceSize;
		VisualOverride = Definition.Visual;
        RemainingUnits = Mathf.Max(1, Definition.TotalUnits);
        _changes = ResourceChanges.Ensure(this);
        _changes.BindScene(this, Definition, "ore");
        ResourceChangeData saved = _changes.Get(this);
        if (saved != null)
        {
            RemainingUnits = Math.Min(RemainingUnits, saved.Units);
            _work = Mathf.Min(Mathf.Max(0.1f, Definition.WorkPerBatch), saved.Work);
            if (saved.Depleted || RemainingUnits == 0) QueueFree();
        }
	}
	#endregion

	#region Mining
	// =========================================================
	// Consume an ore batch only after its reward has been accepted.
	public bool Mine(
		float power, Func<ItemDefinition, int, bool> collect)
	{
		if (IsQueuedForDeletion() || RemainingUnits <= 0 ||
			power <= 0f || !float.IsFinite(power) || collect == null)
			return false;

		float required = Mathf.Max(0.1f, Definition.WorkPerBatch);
        _work = Mathf.Min(required, _work + power);
        _changes.Record(this, _work, RemainingUnits);

		if (_work < required) return true;

		int units = Math.Min(
			RemainingUnits, Mathf.Max(1, Definition.UnitsPerBatch));

		if (!collect(_yieldItem, units)) return false;

		_work = 0f;
        RemainingUnits -= units;
        _changes.Record(this, _work, RemainingUnits, RemainingUnits == 0);

		if (RemainingUnits == 0)
		{
			CollisionLayer = 0;
			QueueFree();
		}

		return true;
	}
	#endregion

	#region Artwork
	// =========================================================
	// Attach imported artwork immediately, or request the cached fallback bake.
	protected override async Task AttachArtworkAsync()
	{
		int variant = SelectArtworkVariant();

		TerrainVisual visual = TerrainVisual.AttachCustom(
			this, false, VisualOverride, variant);

		if (visual == null)
		{
			ImageTexture texture = await OreArtwork.Get(this, Definition);
			if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

			visual = TerrainVisual.Attach(
				this,
				new Rect2(
					Vector2.Zero,
					new Vector2(Definition.BakeSize.X, Definition.BakeSize.Y)),
				-Definition.BakeAnchor,
				Vector2.One,
				false,
				VisualOverride,
				texture,
				imageVariant: variant);
		}

		visual.Scale = Vector2.One * InstanceSize;
	}

	// =========================================================
	// Choose a repeatable image without storing or changing shared resource data.
	private int SelectArtworkVariant()
	{
		if (ArtworkVariant >= 0) return ArtworkVariant;

		int count = VisualOverride?.ImageVariantCount ?? 0;
		if (count <= 1) return 0;

		uint hash = IsoGrid.Hash(
			Mathf.RoundToInt(GlobalPosition.X),
			Mathf.RoundToInt(GlobalPosition.Y),
			0x0AE641u);

		return (int)(hash % (uint)count);
	}
	#endregion
}
'@
$Changes['WORLD/Contents/Rocks/RockSpawner.cs'] = @'
// Generates solid rock instances from weighted species references in each biome.
// Shares footprint placement checks with vegetation and records chunk ownership.
using Godot;
using System.Collections.Generic;

public partial class RockSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region Generation
// =========================================================
// Spawn biome-selected rocks using the same placement recipes as vegetation.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius, List<Obstacle> owned)
{
    ResourceChanges changes = ResourceChanges.Ensure(objects);
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x9041u);

    float clearSquared = spawnClearRadius * spawnClearRadius;

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Rocks,
        coordinate, chunkSize, seed ^ 0xB041u))
    {
        yield return ChunkBuildStage.Rocks;
        if (candidate.Biome == null) continue;

        RockDefinition definition = BiomeSpecies.Select<RockDefinition>(
            candidate.Biome.Rocks, rng);
        if (definition == null) continue;

        float minWidth = Mathf.Max(16f, definition.WidthRange.X);
        float minHeight = Mathf.Max(8f, definition.HeightRange.X);

        float width = rng.RandfRange(
            minWidth, Mathf.Max(minWidth, definition.WidthRange.Y));
        float height = rng.RandfRange(
            minHeight, Mathf.Max(minHeight, definition.HeightRange.Y));
        float size = definition.RollSize(rng);

        Vector2 footprint = new Vector2(
            width, width * Mathf.Max(0.1f, definition.FootprintDepthRatio)) * size;

        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, footprint.Length() * 0.5f + 8f) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, footprint,
                obstacles, new Vector2(12, 8)))
            continue;

        Rock rock = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            BaseWidth = width,
            BaseHeight = height,
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng),
            RockVariant = rng.RandiRange(0, RockDrawing.VariantCount - 1)
        };

        if (!changes.BindGenerated(rock, definition, "rock", coordinate, candidate.Index))
        {
            rock.Free();
            continue;
        }
        objects.AddChild(rock);
        owned.Add(rock);
        obstacles.Add(rock);
    }
}
    #endregion

    #region Test Prop Placement
// =========================================================
// Reject basin reservations and solid obstacles for test prop placement.
public static bool CanPlace(
    Node2D objects, Vector2 point, Vector2 footprint)
{
    return !WorldPlacement.IsBlocked(
        objects, point, footprint,
        WorldPlacement.CollectObstacles(objects),
        new Vector2(12, 8));
}
    #endregion
}
'@
$Changes['WORLD/Contents/Vegetation/VegetationSpawner.cs'] = @'
// Populates streamed chunks with weighted tree and plant species from their biomes.
// A plain Node owns bookkeeping; all instances remain under WorldObjects for Y-sorting.
using Godot;
using System.Collections.Generic;

public partial class VegetationSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region State
    private readonly Dictionary<Vector2I, List<Plant>> _plants = new();
    private readonly Dictionary<Vector2I, List<Tree>> _trees = new();
    #endregion

    #region Generation
  // =========================================================
// Spawn biome-selected trees and plants through shared placement recipes.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint,
    float spawnClearRadius)
{
    if (_plants.ContainsKey(coordinate)) yield break;

    ResourceChanges changes = ResourceChanges.Ensure(objects);
    List<Plant> plants = new();
    List<Tree> trees = new();
    List<Vector2> placed = new();
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);

    _plants.Add(coordinate, plants);
    _trees.Add(coordinate, trees);

    float clearSquared = spawnClearRadius * spawnClearRadius;

    using RandomNumberGenerator treeRng = new();
    treeRng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0xA471u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Trees,
        coordinate, chunkSize, seed ^ 0xC471u))
    {
        yield return ChunkBuildStage.Trees;
        if (candidate.Biome == null) continue;

        TreeDefinition definition = BiomeSpecies.Select<TreeDefinition>(
            candidate.Biome.Vegetation.Trees, treeRng);
        if (definition == null) continue;

        float size = definition.RollSize(treeRng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);
        Vector2 footprint = definition.TrunkFootprint * size;

        float clearance = Mathf.Max(
            definition.GroundClearance * size, footprint.Length() * 0.5f);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(localPoint, tileSize, clearance) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, footprint,
                obstacles, new Vector2(16, 12)) ||
            WorldPlacement.NearTree(objects, globalPoint, definition, size, obstacles))
            continue;

        Tree tree = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = treeRng.RandiRange(
                0, CarbonTreeDrawing.VariantCount - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(treeRng)
        };

        if (!changes.BindGenerated(tree, definition, "tree", coordinate, candidate.Index))
        {
            tree.Free();
            continue;
        }
        objects.AddChild(tree);
        trees.Add(tree);
        obstacles.Add(tree);
    }

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x5A93u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Plants,
        coordinate, chunkSize, seed ^ 0x7A93u))
    {
        yield return ChunkBuildStage.Plants;
        if (candidate.Biome == null) continue;

        PlantDefinition definition = BiomeSpecies.Select<PlantDefinition>(
            candidate.Biome.Vegetation.Plants, rng);
        if (definition == null) continue;

        float size = definition.RollSize(rng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < clearSquared ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, Vector2.Zero,
                obstacles, new Vector2(32, 24)) ||
            WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size))
            continue;

        Plant plant = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = rng.RandiRange(0,
                Mathf.Max(1, definition.Visual?.ImageVariantCount > 0
                    ? definition.Visual.ImageVariantCount
                    : VegetationAtlas.VariantsPerKind) - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng)
        };

        if (!changes.BindGenerated(plant, definition, "plant", coordinate, candidate.Index))
        {
            placed.Add(localPoint);
            plant.Free();
            continue;
        }
        objects.AddChild(plant);
        plants.Add(plant);
        placed.Add(localPoint);
    }
}
    #endregion

    #region Streaming
    // =========================================================
    // Release all tree and plant instances belonging to one unloaded chunk.
    public IEnumerable<ChunkBuildStage> RemoveSteps(Vector2I coordinate)
    {
        if (_plants.TryGetValue(coordinate, out List<Plant> plants))
        {
            foreach (Plant plant in plants)
            {
                if (GodotObject.IsInstanceValid(plant)) plant.QueueFree();
                yield return ChunkBuildStage.Retiring;
            }
            _plants.Remove(coordinate);
        }

        if (_trees.TryGetValue(coordinate, out List<Tree> trees))
        {
            foreach (Tree tree in trees)
            {
                if (GodotObject.IsInstanceValid(tree)) tree.QueueFree();
                yield return ChunkBuildStage.Retiring;
            }
            _trees.Remove(coordinate);
        }
    }
    #endregion
}
'@
$Changes['WORLD/Contents/Vegetation/GrassSpawner.cs'] = @'
// Populates streamed chunks with weighted grass species from their local biomes.
// Reuses shared placement checks and removes instances with their owning chunk.
using Godot;
using System.Collections.Generic;

public partial class GrassSpawner : Node
{
    #region Configuration
    public WorldGenerator Generator { get; set; }
    #endregion

    #region State
    private readonly Dictionary<Vector2I, List<Grass>> _grass = new();
    #endregion

    #region Generation
// =========================================================
// Spawn biome-selected grass with optional locally blended biome colouring.
public IEnumerable<ChunkBuildStage> PopulateSteps(
    Vector2I coordinate, int chunkSize, Vector2 tileSize, uint seed,
    Node2D groundRoot, Node2D objects, Vector2 spawnPoint)
{
    if (_grass.ContainsKey(coordinate)) yield break;

    ResourceChanges changes = ResourceChanges.Ensure(objects);
    List<Grass> tufts = new();
    List<Vector2> placed = new();
    List<Obstacle> obstacles = WorldPlacement.CollectObstacles(objects);
    _grass.Add(coordinate, tufts);

    using RandomNumberGenerator rng = new();
    rng.Seed = IsoGrid.Hash(
        coordinate.X, coordinate.Y, seed ^ 0x4A55u);

    foreach (BiomeScatterCandidate candidate in BiomeScatter.Generate(
        Generator, BiomePopulationFamily.Grass,
        coordinate, chunkSize, seed ^ 0x6A55u))
    {
        yield return ChunkBuildStage.Grass;
        if (candidate.Biome == null) continue;

        GrassDefinition definition = BiomeSpecies.Select<GrassDefinition>(
            candidate.Biome.Vegetation.Grass, rng);
        if (definition == null) continue;

        float size = definition.RollSize(rng);
        Vector2 localPoint = IsoGrid.TileToWorld(candidate.Tile, tileSize);
        Vector2 globalPoint = groundRoot.ToGlobal(localPoint);

        if (globalPoint.DistanceSquaredTo(spawnPoint) < 72f * 72f ||
            !ChasmFeature.HasGroundClearance(
                localPoint, tileSize, definition.GroundClearance * size) ||
            WorldPlacement.IsBlocked(
                objects, globalPoint, Vector2.Zero,
                obstacles, new Vector2(20, 12)) ||
            WorldPlacement.IsCrowded(
                localPoint, placed, definition.Spacing * size))
            continue;

        Color tint = Generator.SampleGrassTint(
            candidate.Tile, out float tintStrength);

        Grass grass = new()
        {
            Name = $"{definition.Id}_{coordinate.X}_{coordinate.Y}_{candidate.Index}",
            Position = objects.ToLocal(globalPoint),
            Definition = definition,
            Variant = rng.RandiRange(0, VegetationAtlas.VariantsPerKind - 1),
            SizeMultiplier = size,
            Mirror = definition.RollMirror(rng),
            BiomeTint = tint,
            BiomeTintStrength = tintStrength
        };

        if (!changes.BindGenerated(grass, definition, "grass", coordinate, candidate.Index))
        {
            placed.Add(localPoint);
            grass.Free();
            continue;
        }
        objects.AddChild(grass);
        tufts.Add(grass);
        placed.Add(localPoint);
    }
}
    #endregion

    #region Streaming
    // =========================================================
    // Release grass instances belonging to one unloaded chunk.
    public IEnumerable<ChunkBuildStage> RemoveSteps(Vector2I coordinate)
    {
        if (!_grass.TryGetValue(coordinate, out List<Grass> tufts)) yield break;
        foreach (Grass grass in tufts)
        {
            if (GodotObject.IsInstanceValid(grass)) grass.QueueFree();
            yield return ChunkBuildStage.Retiring;
        }
        _grass.Remove(coordinate);
    }
    #endregion
}
'@
$Changes['WORLD/Contents/Vegetation/Grass/Grass.cs'] = @'
// Represents walkable grass using shared cached artwork.
// Short grass keeps wind but does not react to player brushing.
using Godot;

public enum GrassHeight { Short, Medium, Tall }

public partial class Grass : Node2D
{
    #region Configuration
    [ExportGroup("Grass")]
    [Export] public GrassDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }

    public Color BiomeTint { get; set; } = Colors.White;
    public float BiomeTintStrength { get; set; }
    #endregion

    #region Shared Materials
    private static readonly ShaderMaterial[] Materials = new ShaderMaterial[4];

    // =========================================================
    // Reuse material variants instead of creating a material for every tuft.
    private static ShaderMaterial GetGrassMaterial(bool shortGrass, bool tinted)
    {
        int index = (shortGrass ? 2 : 0) + (tinted ? 1 : 0);
        ShaderMaterial material = Materials[index];

        if (material != null && GodotObject.IsInstanceValid(material))
            return material;

        material =
            (ShaderMaterial)VegetationAtlas.GrassWindMaterial.Duplicate();

        material.SetShaderParameter("biome_tint_enabled", tinted);
        material.SetShaderParameter("brush_enabled", !shortGrass);
        Materials[index] = material;
        return material;
    }

    #endregion

    #region Clearing
    // =========================================================
    // Record real consumption/clearing; chunk unloading deliberately does not call this.
    public void Clear()
    {
        if (IsQueuedForDeletion()) return;
        ResourceChanges changes = ResourceChanges.Ensure(this);
        changes.BindScene(this, Definition, "grass");
        changes.Record(this, depleted: true);
        Hide();
        QueueFree();
    }
    #endregion

    #region Lifecycle
// =========================================================
// Attach lit grass with the appropriate shared tint and brushing settings.
public override async void _Ready()
{
    try
    {
        if (Definition == null)
            throw new System.InvalidOperationException(
                "Grass requires a GrassDefinition.");

        ResourceChanges changes = ResourceChanges.Ensure(this);
        changes.BindScene(this, Definition, "grass");
        if (changes.Get(this)?.Depleted == true) { QueueFree(); return; }

        VegetationPlacement.Attach(
            this, Definition.PlacementFootprint *
                Mathf.Max(0.1f, SizeMultiplier), true);

        await VegetationAtlas.EnsureReady(this);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

        float tintStrength = Mathf.Clamp(BiomeTintStrength, 0f, 1f);
        bool tinted = tintStrength > 0f;
        bool shortGrass = Definition.BakedHeight == GrassHeight.Short;
        ShaderMaterial material = GetGrassMaterial(shortGrass, tinted);

        TerrainVisual visual = TerrainVisual.Attach(
            this,
            VegetationAtlas.GetGrassRegion(Definition.BakedHeight, Variant),
            VegetationAtlas.GrassOrigin, Vector2.One, false,
            Definition.Visual, VegetationAtlas.Texture, material);

        float size = Mathf.Max(0.1f, SizeMultiplier);
        visual.Scale = new Vector2(Mirror ? -size : size, size);

        if (tinted &&
            visual.GetNodeOrNull<Sprite2D>("Artwork") is Sprite2D sprite &&
            sprite.Texture is AtlasTexture atlas &&
            atlas.Atlas == VegetationAtlas.Texture)
        {
            sprite.SelfModulate = new Color(
                BiomeTint.R, BiomeTint.G, BiomeTint.B, tintStrength);
        }

        SetProcess(false);
    }
    catch (System.Exception error)
    {
        GD.PushError($"Grass '{Name}' artwork failed: {error}");
    }
}
    #endregion
}
'@
$Changes['ENTITIES/Grazing/GrazingWorld.cs'] = @'
// Indexes nearby grass, reserves feeding targets and remembers consumed tufts.
// Shared resource changes survive chunk rebuilding and campaign saves; no timed regrowth.
using Godot;
using System.Collections.Generic;

public partial class GrazingWorld : Node
{
    #region State
    private const float CellSize = 256f;
    private readonly Dictionary<Vector2I, HashSet<Grass>> _cells = new();
    private readonly Dictionary<Grass, Vector2I> _locations = new();
    private readonly Dictionary<Grass, Entity> _reservations = new();
    private SceneTree _tree;
    #endregion

    #region Lifecycle
    // =========================================================
    // Index existing grass and follow streaming additions/removals.
    public override void _Ready()
    {
        _tree = GetTree();
        _tree.NodeAdded += OnAdded;
        _tree.NodeRemoved += OnRemoved;
        IndexBranch(GetParent().GetParent());
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Remove tree subscriptions when this world ends.
    public override void _ExitTree()
    {
        if (_tree == null) return;
        _tree.NodeAdded -= OnAdded;
        _tree.NodeRemoved -= OnRemoved;
    }

    // =========================================================
    // Reuse one index beneath the current world's Systems node.
    public static GrazingWorld GetOrCreate(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        Node systems = world.GetNode("Systems");
        GrazingWorld existing =
            systems.GetNodeOrNull<GrazingWorld>("GrazingWorld");
        if (existing != null) return existing;

        GrazingWorld created = new() { Name = "GrazingWorld" };
        systems.AddChild(created);
        return created;
    }

    // =========================================================
    // Register initial grass without depending on its artwork ready callback.
    private void IndexBranch(Node node)
    {
        OnAdded(node);
        foreach (Node child in node.GetChildren())
            IndexBranch(child);
    }

    // =========================================================
    // Reject previously consumed tufts and index new stationary grass.
    private void OnAdded(Node node)
    {
        if (node is not Grass grass || _locations.ContainsKey(grass))
            return;

        Vector2I cell = Cell(grass.GlobalPosition);
        if (!_cells.TryGetValue(cell, out HashSet<Grass> members))
            _cells[cell] = members = new();

        members.Add(grass);
        _locations.Add(grass, cell);
    }

    // =========================================================
    // Remove streamed grass and release its reservation.
    private void OnRemoved(Node node)
    {
        if (node is not Grass grass ||
            !_locations.Remove(grass, out Vector2I cell))
            return;

        if (_cells.TryGetValue(cell, out HashSet<Grass> members))
        {
            members.Remove(grass);
            if (members.Count == 0) _cells.Remove(cell);
        }
        _reservations.Remove(grass);
    }
    #endregion

    #region Feeding
    // =========================================================
    // Reserve nearby grass inside the wander zone with a clear approach.
    public Grass Reserve(Entity actor, Vector2 centre, float radius)
    {
        WorldNavigation navigation = WorldNavigation.For(actor);
        if (navigation == null) return null;

        List<Grass> candidates = new();
        Vector2I low = Cell(centre - Vector2.One * radius);
        Vector2I high = Cell(centre + Vector2.One * radius);

        for (int y = low.Y; y <= high.Y; y++)
        for (int x = low.X; x <= high.X; x++)
        {
            if (!_cells.TryGetValue(new Vector2I(x, y), out var members))
                continue;

            foreach (Grass grass in members)
            {
                if (!GodotObject.IsInstanceValid(grass) ||
                    grass.IsQueuedForDeletion() ||
                    !WorldLayerMember.Same(actor, grass) ||
                    centre.DistanceSquaredTo(grass.GlobalPosition) >
                        radius * radius)
                    continue;

                if (_reservations.TryGetValue(grass, out Entity holder))
                {
                    if (GodotObject.IsInstanceValid(holder) &&
                        !holder.IsQueuedForDeletion() && holder.Health.IsAlive)
                        continue;
                    _reservations.Remove(grass);
                }

                candidates.Add(grass);
            }
        }

        candidates.Sort((a, b) =>
            actor.GlobalPosition.DistanceSquaredTo(a.GlobalPosition).CompareTo(
            actor.GlobalPosition.DistanceSquaredTo(b.GlobalPosition)));

        int budget = System.Math.Min(6, candidates.Count);
        for (int i = 0; i < budget; i++)
        {
            Grass grass = candidates[i];
            if (!navigation.CanTravelDirectly(
                actor.GlobalPosition, grass.GlobalPosition))
                continue;

            _reservations[grass] = actor;
            return grass;
        }
        return null;
    }

    // =========================================================
    // Release only a reservation belonging to this creature.
    public void Release(Entity actor, Grass grass)
    {
        if (grass != null &&
            _reservations.TryGetValue(grass, out Entity holder) &&
            holder == actor)
            _reservations.Remove(grass);
    }

    // =========================================================
    // Consume once after the completed grazing timer.
    public bool Consume(Entity actor, Grass grass)
    {
        if (!GodotObject.IsInstanceValid(grass) ||
            grass.IsQueuedForDeletion() ||
            !_reservations.TryGetValue(grass, out Entity holder) ||
            holder != actor)
            return false;

        _reservations.Remove(grass);
        grass.Clear();
        return true;
    }

    // =========================================================
    // Convert world coordinates into a small local lookup cell.
    private static Vector2I Cell(Vector2 point)
    {
        return new Vector2I(
            Mathf.FloorToInt(point.X / CellSize),
            Mathf.FloorToInt(point.Y / CellSize));
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

    placed.Configure(this, definition, anchor);
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
$Changes['WORLD/Contents/Shared/WorldPlacement.cs'] = @'
// Shares basin reservations, obstacle footprints and vegetation spacing checks.
// All placement uses logical ground positions rather than elevated artwork.
using Godot;
using System.Collections.Generic;

public static class WorldPlacement
{
    #region Basin Reservations
// =========================================================
// Keep generated objects out of permanent basins and registered cave openings.
public static bool IsBasinReserved(
    Node context, Vector2 point,
    Vector2 footprint, Vector2 padding)
{
    if (CaveWorld.IsHoleReserved(context, point, footprint, padding))
        return true;

    WaterBasinWorld basins = WaterBasinWorld.Find(context);
    return basins != null &&
        basins.OverlapsWorldFootprint(point, footprint, padding);
}
    #endregion

    #region Obstacles
    // =========================================================
    // Collect solids throughout the world hierarchy, including nested presets.
    public static List<Obstacle> CollectObstacles(Node2D objects)
    {
        List<Obstacle> result = new();

        foreach (Node node in objects.GetTree().GetNodesInGroup("world_obstacles"))
        {
            if (node is not Obstacle obstacle ||
                !GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion() ||
                !objects.IsAncestorOf(obstacle)) continue;

            result.Add(obstacle);
        }

        return result;
    }

    // =========================================================
    // Reject basin reservations before checking loaded solid obstacles.
    public static bool IsBlocked(
        Node context, Vector2 point, Vector2 footprint,
        List<Obstacle> obstacles, Vector2 padding)
    {
        if (IsBasinReserved(context, point, footprint, padding))
            return true;
        if (ResourceChanges.Find(context)?.Blocks(point, footprint, padding) == true)
            return true;

        foreach (Obstacle obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion()) continue;

            Vector2 difference = point - obstacle.GlobalPosition;
            Vector2 separation =
                (footprint + obstacle.Footprint) * 0.5f + padding;

            if (Mathf.Abs(difference.X) < separation.X &&
                Mathf.Abs(difference.Y) < separation.Y)
                return true;
        }

        return false;
    }
    #endregion

    #region Spacing
    // =========================================================
    // Test elliptical spacing against already placed walkable vegetation.
    public static bool IsCrowded(
        Vector2 point, List<Vector2> placed, Vector2 spacing)
    {
        float width = Mathf.Max(1f, spacing.X);
        float depth = Mathf.Max(1f, spacing.Y);

        foreach (Vector2 existing in placed)
        {
            Vector2 difference = point - existing;

            if (difference.X * difference.X / (width * width) +
                difference.Y * difference.Y / (depth * depth) < 1f)
                return true;
        }

        return false;
    }

    // =========================================================
    // Respect both tree species' spacing across currently loaded chunks.
    public static bool NearTree(
        Node context, Vector2 point, TreeDefinition definition,
        float size, List<Obstacle> obstacles)
    {
        if (ResourceChanges.Find(context)?.NearTree(point, definition.Spacing * size) == true)
            return true;
        foreach (Obstacle obstacle in obstacles)
        {
            if (!GodotObject.IsInstanceValid(obstacle) ||
                obstacle.IsQueuedForDeletion() ||
                obstacle is not Tree tree) continue;

            Vector2 spacing = (
                definition.Spacing * size +
                tree.Definition.Spacing * tree.SizeMultiplier) * 0.5f;

            float width = Mathf.Max(1f, spacing.X);
            float depth = Mathf.Max(1f, spacing.Y);
            Vector2 difference = point - tree.GlobalPosition;

            if (difference.X * difference.X / (width * width) +
                difference.Y * difference.Y / (depth * depth) < 1f)
                return true;
        }

        return false;
    }
    #endregion
}
'@
$Changes['WORLD/Contents/GroundResources/GroundResourceWorld.cs'] = @'
// Generates finite deposits on validated flat terrain and owns digging transactions.
// Four shader patches per chunk avoid individual deposit nodes and mesh rebuilds.
using Godot;
using System;
using System.Collections.Generic;

public partial class GroundResourceWorld : Node
{
    #region Configuration
    private const int PatchLimit = 4;
    private const int AttemptsPerPatch = 4;
    #endregion

    #region Runtime Data
    private sealed class Deposit
    {
        public GroundResourceDefinition Definition;
        public Vector2 Centre;
        public float Radius, Phase, Work;
        public int Remaining;
        public Vector3I Key;
    }

    private sealed class ChunkData
    {
        public readonly List<Deposit> Deposits = new();
        public ShaderMaterial Material;
    }

    private readonly Dictionary<Vector2I, ChunkData> _loaded = new();
    private ResourceChanges _changes;
    private GroundResourceCatalog _catalog;
    private ChunkController _chunks;
    private TerrainElevation _elevation;
    private Node2D _ground;
    private ResourceWorld _resources;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the world services after their normal initialization.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _elevation = GetNode<TerrainElevation>("../TerrainElevation");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _resources = ResourceWorld.Find(this);
        _changes = ResourceChanges.Ensure(this);
_catalog = GD.Load<GroundResourceCatalog>(
    "res://WORLD/Contents/GroundResources/GroundResourceCatalog.tres");

        if (_resources == null || _catalog == null)
            throw new InvalidOperationException(
                "Ground resources require ResourceWorld and their catalog.");

        _catalog.Validate(_resources.Catalog);
        AddToGroup("ground_resources");
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Create one world-owned service when the first terrain chunk prepares.
    public static GroundResourceWorld Ensure(Node context)
    {
        GroundResourceWorld service = Find(context);
        if (service != null) return service;

        WorldAtmosphere atmosphere = context.GetTree()
            .GetFirstNodeInGroup("world_atmosphere") as WorldAtmosphere;
        if (atmosphere == null)
            throw new InvalidOperationException(
                "Ground resources require WorldAtmosphere.");

        service = new GroundResourceWorld { Name = "GroundResources" };
        atmosphere.GetParent().AddChild(service);
        return service;
    }

    // =========================================================
    // Resolve the service without assuming a scene root name.
    public static GroundResourceWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("ground_resources")
            as GroundResourceWorld;
    }

    // =========================================================
    // Release any remaining chunk-owned shader materials.
    public override void _ExitTree()
    {
        foreach (ChunkData data in _loaded.Values)
            data.Material?.Dispose();
        _loaded.Clear();
    }
    #endregion

    #region Chunk Preparation
    // =========================================================
    // Try a bounded number of candidates under the existing chunk work iterator.
    public IEnumerable<ChunkBuildStage> Prepare(WorldChunk chunk)
    {
        Release(chunk.Coordinate);
        ChunkData data = new();
        _loaded.Add(chunk.Coordinate, data);

        Vector2 origin = new(
            chunk.Coordinate.X * chunk.ChunkSize,
            chunk.Coordinate.Y * chunk.ChunkSize);
        float low = -0.5f, high = chunk.ChunkSize - 0.5f;

        using RandomNumberGenerator rng = new();
        rng.Seed = IsoGrid.Hash(
            chunk.Coordinate.X, chunk.Coordinate.Y, chunk.Seed ^ 918273u);

        for (int slot = 0; slot < PatchLimit; slot++)
        {
            for (int attempt = 0; attempt < AttemptsPerPatch; attempt++)
            {
                yield return ChunkBuildStage.TerrainData;
                GroundResourceDefinition definition =
                    _catalog.Materials[rng.RandiRange(0, _catalog.Materials.Count - 1)];
                float radius = rng.RandfRange(
                    definition.RadiusTiles.X, definition.RadiusTiles.Y);
                float margin = radius * 1.1f + definition.ClearanceTiles + 1f;
                if (high - low <= margin * 2f) continue;

                Vector2 centre = origin + new Vector2(
                    rng.RandfRange(low + margin, high - margin),
                    rng.RandfRange(low + margin, high - margin));
                float phase = rng.RandfRange(0f, Mathf.Tau);

                bool overlap = false;
                foreach (Deposit existing in data.Deposits)
                {
                    float separation = (radius + existing.Radius) * 1.1f + 0.5f;
                    if (centre.DistanceSquaredTo(existing.Centre) <
                        separation * separation)
                    {
                        overlap = true;
                        break;
                    }
                }
                if (overlap || !IsFlat(centre,
                    radius * 1.1f + definition.ClearanceTiles,
                    definition.MaximumHeightVariation))
                    continue;

                Vector3I key = new(chunk.Coordinate.X, chunk.Coordinate.Y, slot);
                ResourceChangeData saved = _changes.Ground(key);
                if (saved != null && saved.Definition != definition.ResourcePath)
                    throw new InvalidOperationException("Saved ground deposit recipe changed.");
                int removed = saved?.Units ?? 0;
                data.Deposits.Add(new Deposit
                {
                    Definition = definition,
                    Centre = centre,
                    Radius = radius,
                    Phase = phase,
                    Work = Mathf.Min(definition.WorkPerUnit, saved?.Work ?? 0),
                    Remaining = Math.Max(0, definition.UnitsPerDeposit - removed),
                    Key = key
                });
                break;
            }
        }
    }

// =========================================================
// Keep deposits out of permanent basins and registered cave approaches.
private bool IsFlat(Vector2 centre, float radius, float tolerance)
{
    WaterBasinWorld basins = WaterBasinWorld.Find(this);
    if (basins != null && basins.Overlaps(centre, radius))
        return false;

    Vector2 point = _ground.ToGlobal(
        IsoGrid.TileToWorld(centre, _chunks.TileSize));

    Vector2 footprint =
        _chunks.TileSize * (radius * Mathf.Sqrt(2f));

    if (CaveWorld.IsHoleReserved(
            this, point, footprint, Vector2.Zero))
        return false;

    return SurfaceGeometry.IsFlat(
        _elevation, centre, radius, tolerance);
}

    // =========================================================
    // Give chunks containing deposits their own small set of shader parameters.
    public ShaderMaterial BindMaterial(WorldChunk chunk, ShaderMaterial source)
    {
        if (!_loaded.TryGetValue(chunk.Coordinate, out ChunkData data) ||
            data.Deposits.Count == 0) return source;

        data.Material = (ShaderMaterial)source.Duplicate();
        UpdateMaterial(data);
        return data.Material;
    }

    // =========================================================
    // Release visuals while retaining only the world's depletion records.
    public void Release(Vector2I coordinate)
    {
        if (!_loaded.TryGetValue(coordinate, out ChunkData data)) return;
        _loaded.Remove(coordinate);
        data.Material?.Dispose();
    }
    #endregion

    #region Shared Patch Shape
    // =========================================================
    // Shrink area proportionally to the material remaining in the deposit.
    private static float CurrentRadius(Deposit deposit)
    {
        return deposit.Radius * Mathf.Sqrt(
            deposit.Remaining / (float)deposit.Definition.UnitsPerDeposit);
    }

// =========================================================
// Keep deposit boundaries consistent with the shared surface shape formula.
private static float ShapeRadius(Deposit deposit, Vector2 difference)
{
    return CurrentRadius(deposit) *
        SurfaceGeometry.Edge(difference.Angle(), deposit.Phase);
}
    // =========================================================
    // Update four small shader records only when a deposit changes.
    private static void UpdateMaterial(ChunkData data)
    {
        for (int i = 0; i < PatchLimit; i++)
        {
            Vector4 shape = Vector4.Zero;
            Color tint = Colors.Transparent;
            if (i < data.Deposits.Count)
            {
                Deposit deposit = data.Deposits[i];
                shape = new Vector4(deposit.Centre.X, deposit.Centre.Y,
                    CurrentRadius(deposit), deposit.Phase);
                tint = deposit.Definition.SurfaceTint;
            }
            data.Material.SetShaderParameter($"deposit_{i}", shape);
            data.Material.SetShaderParameter($"deposit_color_{i}", tint);
        }
    }
    #endregion

    #region Digging
    // =========================================================
    // Apply shovel work to a visible deposit and spawn each extracted unit.
    public bool Dig(Vector2 globalPoint, int strength, float power)
    {
        if (power <= 0f || !float.IsFinite(power)) return false;
        Vector2 local = _ground.ToLocal(globalPoint);
        Vector2 tile = IsoGrid.WorldToTile(local, _chunks.TileSize);
        Vector2I coordinate = IsoGrid.WorldToChunk(
            local, _chunks.TileSize, _chunks.ChunkSize);

        if (!_loaded.TryGetValue(coordinate, out ChunkData data) ||
            data.Material == null) return false;

        foreach (Deposit deposit in data.Deposits)
        {
            if (deposit.Remaining <= 0 ||
                strength < deposit.Definition.RequiredShovelStrength) continue;

            Vector2 difference = tile - deposit.Centre;
            if (difference.Length() > ShapeRadius(deposit, difference)) continue;

            float required = deposit.Definition.WorkPerUnit;
            deposit.Work = Mathf.Min(required, deposit.Work + power);
            _changes.RecordGround(deposit.Key, deposit.Definition,
                deposit.Definition.UnitsPerDeposit - deposit.Remaining, deposit.Work);
            if (deposit.Work < required) return true;

            if (!_resources.Spawn(deposit.Definition.ItemId, 1, globalPoint, this))
                return false;

            deposit.Work = 0f;
            deposit.Remaining--;
            _changes.RecordGround(deposit.Key, deposit.Definition,
                deposit.Definition.UnitsPerDeposit - deposit.Remaining, deposit.Work);
            UpdateMaterial(data);
            return true;
        }
        return false;
    }
    #endregion

        #region Debug
    // =========================================================
    // Report generated deposits without counting depleted patches as available.
    public string GetDebugSummary()
    {
        int available = 0;
        int visible = 0;

        foreach (ChunkData data in _loaded.Values)
        foreach (Deposit deposit in data.Deposits)
        {
            if (deposit.Remaining <= 0) continue;
            available++;
            if (data.Material != null) visible++;
        }

        return $"Ground resources: {visible} rendered patches | " +
            $"{available} generated patches | {_loaded.Count} tracked chunks";
    }

    // =========================================================
    // Match the digging boundary and project it onto the existing terrain surface.
    public IEnumerable<(string Label, Vector2 Centre, Vector2[] Points)>
        GetDebugFootprints()
    {
        const int segments = 48;

        foreach (ChunkData data in _loaded.Values)
        {
            if (data.Material == null) continue;

            foreach (Deposit deposit in data.Deposits)
            {
                if (deposit.Remaining <= 0) continue;

                Vector2[] points = new Vector2[segments];
                for (int i = 0; i < segments; i++)
                {
                    float angle = Mathf.Tau * i / segments;
                    Vector2 direction = new(
                        Mathf.Cos(angle), Mathf.Sin(angle));
                    Vector2 tile = deposit.Centre +
                        direction * ShapeRadius(deposit, direction);

                    Vector2 point = _ground.ToGlobal(
                        IsoGrid.TileToWorld(tile, _chunks.TileSize));
                    points[i] = point +
                        Vector2.Up * _elevation.SampleWorldHeight(point);
                }

                Vector2 centre = _ground.ToGlobal(
                    IsoGrid.TileToWorld(deposit.Centre, _chunks.TileSize));
                centre += Vector2.Up * _elevation.SampleWorldHeight(centre);

                yield return (
                    $"{deposit.Definition.Id}: {deposit.Remaining}",
                    centre, points);
            }
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
            : "Partial save: surface player/world and changed resources.";
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
            ShowMessage("Partial campaign saved", "World, player and changed resources saved to your selected profile.\n" +
                CampaignSession.SavedPositionFor(this) + "\n" +
                "Drops, containers, buildings, entities and underground restoration are later passes.");
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
$Changes['SYSTEMS/Saving/ResourceChanges.cs'] = @'
// Keeps only changed resources, with stable identities independent of live chunk nodes.
// Placement reservations preserve generation spacing without retaining collision or artwork.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

public sealed class ResourceChangeData
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Definition { get; set; } = "";
    public float Work { get; set; }
    public int Units { get; set; }
    public bool Depleted { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Depth { get; set; }
    public float SpacingX { get; set; }
    public float SpacingY { get; set; }
}

public sealed class ResourceChangesData
{
    public int Version { get; set; } = 1;
    public List<ResourceChangeData> Entries { get; set; } = new();
}

public partial class ResourceChanges : Node
{
    #region State
    public const string Section = "resource-changes";
    private const string Identity = "persistent_resource_id";
    private const string Family = "persistent_resource_kind";
    private const string DefinitionPath = "persistent_resource_definition";
    private const float CellSize = 256f;
    private readonly Dictionary<string, ResourceChangeData> _changes = new();
    private readonly Dictionary<Vector2I, List<ResourceChangeData>> _reservations = new();
    private float _maximumExtent;
    public int Count => _changes.Count;
    #endregion

    #region Ownership
    // =========================================================
    // Resolve the current world rather than caching references across profile changes.
    public static ResourceChanges Find(Node context)
    {
        Node world = WorldConfig.TryFind(context)?.GetParent();
        return world?.GetNodeOrNull<ResourceChanges>("ResourceChanges");
    }

    // =========================================================
    // Give Sandbox and direct scene testing the same session persistence as Campaign.
    public static ResourceChanges Ensure(Node context)
    {
        Node world = WorldConfig.Find(context).GetParent();
        ResourceChanges changes = world.GetNodeOrNull<ResourceChanges>("ResourceChanges");
        if (changes != null) return changes;
        changes = new ResourceChanges { Name = "ResourceChanges" };
        world.AddChild(changes);
        return changes;
    }

    // =========================================================
    // This owner has no frame updates and stores no references to resource nodes.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Save Section
    // =========================================================
    // Accept old world/player saves and reject malformed or unsupported change records.
    public static ResourceChangesData ReadSection(CampaignData data)
    {
        if (!data.Sections.TryGetValue(Section, out JsonElement section))
        {
            if (data.Version == 1) return new ResourceChangesData();
            throw new InvalidDataException("Missing generated-resource save section.");
        }
        ResourceChangesData saved = section.Deserialize<ResourceChangesData>();
        if (saved == null || saved.Version != 1 || saved.Entries == null || saved.Entries.Count > 100000)
            throw new InvalidDataException("Invalid generated-resource save section.");
        HashSet<string> identities = new();
        foreach (ResourceChangeData entry in saved.Entries)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id) || entry.Id.Length > 1024 ||
                !identities.Add(entry.Id) ||
                entry.Kind is not ("rock" or "tree" or "plant" or "grass" or "ore" or "ground") ||
                string.IsNullOrEmpty(entry.Definition) ||
                !entry.Definition.StartsWith("res://", StringComparison.Ordinal) ||
                !entry.Definition.Split("::")[0].EndsWith(".tres", StringComparison.OrdinalIgnoreCase) ||
                (entry.Kind != "ground" && entry.Definition.Contains("::")) || entry.Units < 0 ||
                !float.IsFinite(entry.Work) || entry.Work < 0 ||
                !float.IsFinite(entry.X) || !float.IsFinite(entry.Y) ||
                !float.IsFinite(entry.Width) || !float.IsFinite(entry.Depth) ||
                !float.IsFinite(entry.SpacingX) || !float.IsFinite(entry.SpacingY) ||
                entry.Width < 0 || entry.Depth < 0 || entry.SpacingX < 0 || entry.SpacingY < 0 ||
                entry.Width > 100000 || entry.Depth > 100000 || entry.SpacingX > 100000 || entry.SpacingY > 100000)
                throw new InvalidDataException("Invalid generated-resource change record.");
        }
        return saved;
    }

    // =========================================================
    // Load before the detached world's Ready callbacks or chunk generation can run.
    public void Initialize(CampaignData data)
    {
        foreach (ResourceChangeData entry in ReadSection(data).Entries)
        {
            if (!Godot.FileAccess.FileExists(entry.Definition.Split("::")[0]))
                throw new InvalidDataException("Missing saved resource definition: " + entry.Definition);
            _changes.Add(entry.Id, entry);
            Reserve(entry);
        }
    }

    // =========================================================
    // Snapshot changed records only, including changes belonging to unloaded chunks.
    public void Capture(CampaignData data)
    {
        ResourceChangesData saved = new() { Entries = _changes.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList() };
        data.Sections[Section] = JsonSerializer.SerializeToElement(saved);
        data.Version = 2;
        data.Coverage = "world-player-resources";
    }
    #endregion

    #region Stable Identity
    // =========================================================
    // Bind the original scatter candidate before adding a generated node to the tree.
    public bool BindGenerated(Node2D host, Resource definition, string kind,
        Vector2I chunk, int candidate, string layer = WorldLayerId.Surface)
    {
        string id = FormattableString.Invariant($"{layer}/{kind}/{chunk.X}/{chunk.Y}/{candidate}");
        Bind(host, definition, kind, id);
        return Get(host)?.Depleted != true;
    }

    // =========================================================
    // Give hand-placed scene resources identities relative to their owning world.
    public void BindScene(Node2D host, Resource definition, string kind)
    {
        if (host.HasMeta(Identity)) return;
        Node world = WorldConfig.Find(host).GetParent();
        Bind(host, definition, kind, $"{WorldLayerMember.For(host)}/{kind}/scene/{world.GetPathTo(host)}");
    }

    // =========================================================
    // Keep identity metadata on its host without adding a helper node per resource.
    private void Bind(Node2D host, Resource definition, string kind, string id)
    {
        string path = definition?.ResourcePath ?? "";
        if (string.IsNullOrEmpty(path) || path.Contains("::"))
            throw new InvalidDataException("Persistent resources need separate definition files.");
        if (_changes.TryGetValue(id, out ResourceChangeData saved) && saved.Definition != path)
            throw new InvalidDataException("Saved resource recipe changed: " + id);
        host.SetMeta(Identity, id);
        host.SetMeta(Family, kind);
        host.SetMeta(DefinitionPath, path);
    }

    // =========================================================
    // Resolve a host's change record without retaining the host after unloading.
    public ResourceChangeData Get(Node2D host)
    {
        if (!host.HasMeta(Identity)) return null;
        _changes.TryGetValue(host.GetMeta(Identity).AsString(), out ResourceChangeData saved);
        return saved;
    }

    // =========================================================
    // Record accepted work, depletion or remaining ore units at the mutation point.
    public void Record(Node2D host, float work = 0, int units = 0, bool depleted = false)
    {
        if (!host.HasMeta(Identity)) return;
        string id = host.GetMeta(Identity).AsString();
        if (!_changes.TryGetValue(id, out ResourceChangeData entry))
        {
            Vector2 footprint = host is Obstacle obstacle ? obstacle.Footprint : Vector2.Zero;
            Vector2 spacing = host is Tree tree ? tree.Definition.Spacing * tree.SizeMultiplier : Vector2.Zero;
            entry = new ResourceChangeData
            {
                Id = id, Kind = host.GetMeta(Family).AsString(),
                Definition = host.GetMeta(DefinitionPath).AsString(),
                X = host.GlobalPosition.X, Y = host.GlobalPosition.Y,
                Width = footprint.X, Depth = footprint.Y,
                SpacingX = spacing.X, SpacingY = spacing.Y
            };
            _changes.Add(id, entry);
        }
        bool wasDepleted = entry.Depleted;
        entry.Work = work; entry.Units = units; entry.Depleted = depleted;
        if (depleted && !wasDepleted) Reserve(entry);
    }

    // =========================================================
    // Share chunk/slot identities with shader-based ground deposits.
    public ResourceChangeData Ground(Vector3I slot)
    {
        _changes.TryGetValue(GroundId(slot), out ResourceChangeData entry);
        return entry;
    }

    // =========================================================
    // Ground patches retain extracted units and unfinished shovel work without nodes.
    public void RecordGround(Vector3I slot, GroundResourceDefinition definition, int removed, float work)
    {
        string id = GroundId(slot);
        if (!_changes.TryGetValue(id, out ResourceChangeData entry))
            _changes.Add(id, entry = new ResourceChangeData { Id = id, Kind = "ground", Definition = definition.ResourcePath });
        if (entry.Definition != definition.ResourcePath)
            throw new InvalidDataException("Saved ground deposit recipe changed: " + id);
        entry.Units = removed; entry.Work = work;
        entry.Depleted = removed >= definition.UnitsPerDeposit;
    }

    // =========================================================
    // Format ground identities without culture-sensitive coordinates.
    private static string GroundId(Vector3I slot)
    {
        return FormattableString.Invariant($"surface/ground/{slot.X}/{slot.Y}/{slot.Z}");
    }
    #endregion

    #region Generation Reservations
    // =========================================================
    // Keep only harvested solid footprints in a small spatial index for generation.
    private void Reserve(ResourceChangeData entry)
    {
        if (!entry.Depleted || entry.Kind is not ("rock" or "tree" or "ore")) return;
        Vector2I cell = Cell(new Vector2(entry.X, entry.Y));
        if (!_reservations.TryGetValue(cell, out List<ResourceChangeData> entries))
            _reservations.Add(cell, entries = new());
        entries.Add(entry);
        _maximumExtent = Mathf.Max(_maximumExtent,
            Mathf.Max(Mathf.Max(entry.Width, entry.Depth), Mathf.Max(entry.SpacingX, entry.SpacingY)));
    }

    // =========================================================
    // Query nearby reservation records only during chunk population.
    private IEnumerable<ResourceChangeData> Nearby(Vector2 point, Vector2 size)
    {
        float radius = (_maximumExtent + Mathf.Max(size.X, size.Y)) * 0.5f;
        Vector2I low = Cell(point - Vector2.One * radius), high = Cell(point + Vector2.One * radius);
        for (int y = low.Y; y <= high.Y; y++)
        for (int x = low.X; x <= high.X; x++)
            if (_reservations.TryGetValue(new Vector2I(x, y), out List<ResourceChangeData> entries))
                foreach (ResourceChangeData entry in entries) yield return entry;
    }

    // =========================================================
    // Preserve the source's old generation footprint, while allowing its own candidate.
    public bool Blocks(Vector2 point, Vector2 footprint, Vector2 padding)
    {
        foreach (ResourceChangeData entry in Nearby(point, footprint + padding * 2))
        {
            Vector2 difference = point - new Vector2(entry.X, entry.Y);
            if (difference.LengthSquared() < 0.01f) continue;
            Vector2 separation = (footprint + new Vector2(entry.Width, entry.Depth)) * 0.5f + padding;
            if (Mathf.Abs(difference.X) < separation.X && Mathf.Abs(difference.Y) < separation.Y) return true;
        }
        return false;
    }

    // =========================================================
    // Preserve tree canopy spacing as well as its narrow trunk footprint.
    public bool NearTree(Vector2 point, Vector2 spacing)
    {
        foreach (ResourceChangeData entry in Nearby(point, spacing))
        {
            if (entry.Kind != "tree") continue;
            Vector2 difference = point - new Vector2(entry.X, entry.Y);
            if (difference.LengthSquared() < 0.01f) continue;
            Vector2 combined = (spacing + new Vector2(entry.SpacingX, entry.SpacingY)) * 0.5f;
            float width = Mathf.Max(1, combined.X), depth = Mathf.Max(1, combined.Y);
            if (difference.X * difference.X / (width * width) + difference.Y * difference.Y / (depth * depth) < 1) return true;
        }
        return false;
    }

    // =========================================================
    // Convert a logical ground position to a reservation lookup cell.
    private static Vector2I Cell(Vector2 point)
    {
        return new Vector2I(Mathf.FloorToInt(point.X / CellSize), Mathf.FloorToInt(point.Y / CellSize));
    }
    #endregion
}
'@
$Changes['WORLD/Contents/Rocks/Rock.cs'] = @'
// Represents any solid rock species using its selected dimensions and variation.
// Reuses Obstacle collision, navigation compatibility and sunlight shadows.
using Godot;
using System.Threading.Tasks;

public partial class Rock : Obstacle
{
    #region Configuration
    [ExportGroup("Rock")]
    [Export] public RockDefinition Definition { get; set; }
    [Export] public float BaseWidth { get; set; }
    [Export] public float BaseHeight { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Configuration Hook
    // =========================================================
    // Configure solid dimensions without altering the shared rock definition.
    protected override void ConfigureInstance()
    {
        if (Definition == null)
            throw new System.InvalidOperationException("Rock requires a RockDefinition.");

        SizeMultiplier = Mathf.Max(0.1f, SizeMultiplier);
        BaseWidth = Mathf.Max(16f,
            BaseWidth > 0f ? BaseWidth : Definition.WidthRange.X);
        BaseHeight = Mathf.Max(8f,
            BaseHeight > 0f ? BaseHeight : Definition.HeightRange.X);

        Footprint = new Vector2(
            BaseWidth,
            BaseWidth * Mathf.Max(0.1f, Definition.FootprintDepthRatio))
            * SizeMultiplier;
        Height = BaseHeight * SizeMultiplier;
        Kind = ObstacleKind.Rock;
        VisualOverride = Definition.Visual;
        ResourceHarvest.Attach(this, Definition, "rock");
    }
    #endregion

    #region Artwork
    // =========================================================
    // Apply base dimensions to fallback art and uniform instance variation to either visual.
    protected override async Task AttachArtworkAsync()
    {
        await PlaceholderAtlas.EnsureReady(this);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

        int variant = RockVariant;
        if (variant < 0)
        {
            variant = (int)(IsoGrid.Hash(
                Mathf.RoundToInt(GlobalPosition.X),
                Mathf.RoundToInt(GlobalPosition.Y), 64127u)
                % (uint)RockDrawing.VariantCount);
        }

        TerrainVisual visual = TerrainVisual.Attach(
            this, PlaceholderAtlas.GetRockRegion(variant),
            new Vector2(-80, -120),
            new Vector2(BaseWidth / 96f, BaseHeight / 72f),
            false, VisualOverride);
        visual.Scale = new Vector2(
            Mirror ? -SizeMultiplier : SizeMultiplier, SizeMultiplier);
    }
    #endregion
}
'@
$Changes['WORLD/Contents/Vegetation/Trees/Tree.cs'] = @'
// Represents any tree species using its definition and per-instance variation.
// Only the trunk footprint collides; the canopy remains independent artwork.
using Godot;
using System.Threading.Tasks;

public partial class Tree : Obstacle
{
    #region Configuration
    [ExportGroup("Tree")]
    [Export] public TreeDefinition Definition { get; set; }
    [Export] public int Variant { get; set; }
    [Export] public float SizeMultiplier { get; set; } = 1f;
    [Export] public bool Mirror { get; set; }
    #endregion

    #region Configuration Hook
    // =========================================================
    // Configure the shared obstacle foundation from this tree species.
    protected override void ConfigureInstance()
    {
        if (Definition == null)
            throw new System.InvalidOperationException("Tree requires a TreeDefinition.");

        SizeMultiplier = Mathf.Max(0.1f, SizeMultiplier);
        Footprint = Definition.TrunkFootprint * SizeMultiplier;
        Height = Definition.VisualHeight * SizeMultiplier;
        VisualOverride = Definition.Visual;
        Kind = ObstacleKind.Rock;
        ResourceHarvest.Attach(this, Definition, "carbon");
    }
    #endregion

    #region Artwork
    // =========================================================
    // Attach custom artwork or the cached Carbon Tree fallback with identical size variation.
    protected override async Task AttachArtworkAsync()
    {
        await TreeAtlas.EnsureReady(this);
        if (!GodotObject.IsInstanceValid(this) || !IsInsideTree() || IsQueuedForDeletion()) return;

        TerrainVisual visual = TerrainVisual.Attach(
            this, TreeAtlas.GetRegion(Variant), TreeAtlas.Origin,
            Vector2.One, false, VisualOverride,
            TreeAtlas.Texture, TreeAtlas.WindMaterial);
        visual.Scale = new Vector2(
            Mirror ? -SizeMultiplier : SizeMultiplier, SizeMultiplier);
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
'@
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = 'ac6cdeea593b9e90bde5b0212632b1e3ff9f4a43a64bb87d948214fb8c65d8e4'
$Expected['SYSTEMS/Saving/CampaignRecipe.cs'] = '940cfe5983f8759d9e5a6aca98bc1485241f61cac500b21a60f408b7a4153e30'
$Expected['SYSTEMS/Saving/CampaignStore.cs'] = '15ce0c927f854548811b3e85b2ee58d3d9e48f9094e6ae78c4414f609fcc0618'
$Expected['SYSTEMS/Harvesting/ResourceHarvest.cs'] = 'c8d8e5f072d7d2b6c7319f8e7cf246365d62cb0e173ad82d245c189b04d69cd2'
$Expected['WORLD/Contents/Shared/Obstacle.cs'] = '279d810e5a7a2c18bf2e8081c542182d9d69c14f3b2136ca092e18e6b29a46a3'
$Expected['WORLD/Contents/Vegetation/Plants/Plant.cs'] = '44fa4594d57e28c7ef135101c5fefbc0e5e5b995825e43df794251c7ee66b0da'
$Expected['WORLD/Contents/Ores/OreDeposit.cs'] = '6dd6d962163808e26a64e3e3ea3ffc0f953dded4f53a0a30fa749f14a5fb7201'
$Expected['WORLD/Contents/Rocks/RockSpawner.cs'] = '96e2ae776bb96b770522aa0a8d63b913578ad888b4999d88d439d2443e01e5f5'
$Expected['WORLD/Contents/Vegetation/VegetationSpawner.cs'] = 'b758cbd995ca505b3c0f0595a5828ae5f147a19597a8830943323e663a86a226'
$Expected['WORLD/Contents/Vegetation/GrassSpawner.cs'] = '026a1b62fca0b181566c4ffce11ffaf8706d9138bd734540a3c46fb8bf0d4e9f'
$Expected['WORLD/Contents/Vegetation/Grass/Grass.cs'] = '10a054cccf9e4aca15611c44c597db63c414cc074920971b2269addd7b21e8fe'
$Expected['ENTITIES/Grazing/GrazingWorld.cs'] = '06e8b7451f3ec83682b03e110cbcd09c5c8654886fb06cda8896617ce42049e1'
$Expected['SYSTEMS/Placement/PlacementWorld.cs'] = '8cf56cec026d86069fcaa3d1ce7b610147ad18a1b805ec71dbcd2ec5265d2e17'
$Expected['WORLD/Contents/Shared/WorldPlacement.cs'] = 'f18d01d298d4de2ee1143ac44e08f5106c7bca45af5d3d43e6fa676f6b84636d'
$Expected['WORLD/Contents/GroundResources/GroundResourceWorld.cs'] = '5ca7375253d506e40f0e2ca77926bc185627bf215ac4ebb4e70f4504508acf4d'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = '91e9f687a4c594afb5bc29189342a48b737d8c9bf970aed8e6108fadd36abcb1'
$Expected['WORLD/Contents/Rocks/Rock.cs'] = '6a4a255060612171b201697593c4c03dd9c901908b8609544b2db6e92d81cd03'
$Expected['WORLD/Contents/Vegetation/Trees/Tree.cs'] = 'b59ca1e4d87e69426518eb5ac47154e17d582a107a27a4e65f9bee08015cd30d'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = '6a8167f175144db4a7158645fd04792094970f0da05fb8272c417102bac61877'
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
if (!$Pending.Count) { Write-Host 'Pass 3 is already installed. No files changed.'; return }
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
Write-Host 'Harvest, unload/revisit the chunk, save, quit, then Continue with the same profile.'
Write-Host 'Existing saves upgrade on next Save. Collect rewards: loose drops are Pass 4.'
