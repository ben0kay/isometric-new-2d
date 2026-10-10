# Installs Layer Query/Tuning Pass 1 against a89c753. No Git operations.
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
$Changes['CONFIG/GlobalConfig.cs'] = @'
// Holds shared game-wide tuning settings.
// WorldConfig inherits these settings on the existing CONFIG node.
using Godot;

public partial class GlobalConfig : Node
{
	#region Biomes
	[ExportGroup("BIOMES")]
	[Export(PropertyHint.Range, "0.25,8,0.25")]
	public float BiomeScaleMultiplier { get; set; } = 1f;
	#endregion

	#region Basins
	[ExportGroup("BASINS")]
	[Export] public bool GenerateBiomeBasins { get; set; } = true;

	[Export(PropertyHint.Range, "16,256,8")]
	public float BasinCandidateSpacingTiles { get; set; } = 64f;

		// Additional budget for basin data requested outside normal chunk preparation.
	[Export(PropertyHint.Range, "0.05,2,0.05")]
	public double BasinQueryBudgetMs { get; set; } = 0.25;
	#endregion

#region Caves
[ExportGroup("CAVES")]
[Export] public bool GenerateCaves { get; set; } = true;

// Scales underground biome regions independently of surface biomes.
// Does not enlarge individual chambers, passages or entrances.
[Export(PropertyHint.Range, "0.25,8,0.25")]
public float CaveBiomeScaleMultiplier { get; set; } = 1f;

// Minimum logical tile distance between surface entrance mouths.
[Export(PropertyHint.Range, "64,1024,8")]
public float MinimumCaveHoleDistanceTiles { get; set; } = 96f;
#endregion

    #region Layer Connection Frequency
    [ExportGroup("LAYER CONNECTION FREQUENCY")]
    // Source/upper layer ID -> candidate probability multiplier; restart after editing.
    [Export] public Godot.Collections.Dictionary<string, float> LayerConnectionFrequency { get; set; }
        = DefaultLayerConnectionFrequency();

    // =========================================================
    // Keep defaults explicit without imposing a maximum depth or naming convention.
    public static Godot.Collections.Dictionary<string, float> DefaultLayerConnectionFrequency() => new()
    {
        ["surface"] = 1f, ["underground_1"] = 0.5f, ["underground_2"] = 0.25f
    };

    // =========================================================
    // Reject invalid tuning and references to layers absent from this world's catalog.
    public void ValidateLayerConnectionFrequency(WorldLayerCatalog catalog)
    {
        if (LayerConnectionFrequency == null || LayerConnectionFrequency.Count > 256)
            throw new System.InvalidOperationException("Invalid layer connection frequency settings.");
        foreach (var entry in LayerConnectionFrequency)
        {
            catalog.Get(entry.Key);
            if (!float.IsFinite(entry.Value) || entry.Value < 0f || entry.Value > 8f)
                throw new System.InvalidOperationException($"Connection frequency for '{entry.Key}' must be between 0 and 8.");
        }
    }

    // =========================================================
    // Scale seeded candidate chance; terrain validity and spacing remain separate.
    public float ScaleLayerConnectionChance(string upperLayer, float baseChance)
    {
        WorldLayerId.Validate(upperLayer);
        if (!float.IsFinite(baseChance) || baseChance < 0f || baseChance > 1f || LayerConnectionFrequency == null)
            throw new System.ArgumentException("Invalid layer connection probability.");
        float multiplier = LayerConnectionFrequency.TryGetValue(upperLayer, out float value) ? value : 1f;
        if (!float.IsFinite(multiplier) || multiplier < 0f || multiplier > 8f)
            throw new System.ArgumentException("Layer connection frequency must be between 0 and 8.");
        return Mathf.Min(1f, baseChance * multiplier);
    }
    #endregion

	#region Layer Transitions
	[ExportGroup("LAYER TRANSITIONS")]
	// Generation settings: restart the world after changing either multiplier.
	[Export(PropertyHint.Range, "0.5,8,0.05")]
	public float EntranceLengthMultiplier { get; set; } = 1f;
	[Export(PropertyHint.Range, "0.5,4,0.05")]
	public float EntranceSlopeMultiplier { get; set; } = 1.1f;
	#endregion

	#region Visibility
	[ExportGroup("VISIBILITY")]
	[Export] public bool ObstructionFadingEnabled { get; set; } = true;

	[Export(PropertyHint.Range, "0,100,1")]
	public float ObstructingSpriteOpacityPercent { get; set; } = 35f;

	[Export(PropertyHint.Range, "0.05,2,0.05")]
	public float ObstructionFadeSeconds { get; set; } = 0.2f;
	#endregion

#region Debug Map
[ExportGroup("DEBUG MAP")]

[ExportSubgroup("Biome Preview")]
[Export(PropertyHint.Range, "16,8192,16")]
public float DebugMapRadiusTiles { get; set; } = 1024f;

[Export(PropertyHint.Range, "128,4096,128")]
public float DebugBiomeSearchRadiusTiles { get; set; } = 1024f;

[ExportSubgroup("Points Of Interest")]
[Export(PropertyHint.Range, "16,1024,16")]
public float DebugMapPoiRadiusTiles { get; set; } = 128f;

[Export(PropertyHint.Range, "0.1,2,0.1")]
public double DebugMapPoiBudgetMs { get; set; } = 0.5;
#endregion

	#region Cave Discovery
	[ExportGroup("CAVE DISCOVERY")]
	[Export(PropertyHint.Range, "128,512,16")]
	public float CaveDiscoveryRadiusTiles { get; set; } = 128f;
	#endregion

	#region Navigation
[ExportGroup("NAVIGATION")]

[ExportSubgroup("Work Budget")]
// Shared soft budget across surface and cave route planning.
[Export(PropertyHint.Range, "0.05,3,0.05")]
public double NavigationBudgetMs { get; set; } = 0.35;

[Export(PropertyHint.Range, "1,8,1")]
public int NavigationSearchesPerTick { get; set; } = 2;

[ExportSubgroup("Search Areas")]
[Export(PropertyHint.Range, "16,64,8")]
public int NavigationCellSize { get; set; } = 32;

[Export(PropertyHint.Range, "1,32,1")]
public float NavigationAgentClearance { get; set; } = 12f;

// Padding uses logical world units, rather than terrain tiles.
[Export(PropertyHint.Range, "64,512,32")]
public int NavigationInitialPadding { get; set; } = 128;

[Export(PropertyHint.Range, "128,2048,64")]
public int NavigationMaximumPadding { get; set; } = 1024;

[Export(PropertyHint.Range, "256,16384,256")]
public int NavigationMaximumGridCells { get; set; } = 4096;

[Export(PropertyHint.Range, "1,16,1")]
public int NavigationCachedGridsPerLayer { get; set; } = 4;

[ExportSubgroup("Request Timing")]
// Urgent requests are distributed across this many physics ticks.
[Export(PropertyHint.Range, "1,12,1")]
public int NavigationStaggerTicks { get; set; } = 4;

[Export(PropertyHint.Range, "0.25,5,0.25")]
public double NavigationFailedRetrySeconds { get; set; } = 1.0;
#endregion

#region Enemy Updates
[ExportGroup("ENEMY UPDATES")]

[ExportSubgroup("Scheduling")]
[Export(PropertyHint.Range, "1,12,1")]
public int EnemyOnScreenStaggerTicks { get; set; } = 3;

[Export(PropertyHint.Range, "3,60,1")]
public int EnemyOffScreenStaggerTicks { get; set; } = 12;

[ExportSubgroup("Off Screen")]
[Export(PropertyHint.Range, "0.1,3,0.05")]
public double EnemyOffScreenTargetInterval { get; set; } = 0.75;

[Export(PropertyHint.Range, "0.1,2,0.05")]
public double EnemyOffScreenDecisionInterval { get; set; } = 0.4;

[ExportSubgroup("Screen Checks")]
[Export(PropertyHint.Range, "1,60,1")]
public int EnemyScreenCheckTicks { get; set; } = 12;

[Export(PropertyHint.Range, "0,512,16")]
public float EnemyScreenMarginPixels { get; set; } = 128f;
#endregion

#region Eclipse
[ExportGroup("ECLIPSE")]

[Export(PropertyHint.Range, "0.1,240,0.1,or_greater")]
public double DayDurationMinutes { get; set; } = 45.0;

[Export(PropertyHint.Range, "0,120,0.1,or_greater")]
public double EclipseDurationMinutes { get; set; } = 15.0;

[Export(PropertyHint.Range, "0,4,0.05,or_greater")]
public float EclipseDarknessMultiplier { get; set; } = 1f;
#endregion
}
'@
$Changes['WORLD/Generation/Caves/Surface/CaveSurfaceSampler.cs'] = @'
// Checks surface generation data without loading surface chunks.
// Results are cached within this world instance and evaluated incrementally.
using Godot;
using System.Collections.Generic;

public sealed class CaveSurfaceSampler
{
    #region Results
    public sealed class Result
    {
        public bool Accepted;
        public string Reason = "Not checked";
        public string BiomeId;
        public float RimHeight;
        public float ClearRadius;

        // =========================================================
        // Reuse a completed cached decision.
        public void CopyFrom(Result other)
        {
            Accepted = other.Accepted;
            Reason = other.Reason;
            BiomeId = other.BiomeId;
            RimHeight = other.RimHeight;
            ClearRadius = other.ClearRadius;
        }
    }
    #endregion

    #region State
    private const int CacheLimit = 256;

    private readonly Node _world;
    private readonly float _connectionFrequency;
    private readonly ChunkController _chunks;
    private readonly WorldGenerator _generator;
    private readonly TerrainElevation _elevation;
    private readonly TerrainSlopeWorld _slopes;
    private readonly Node2D _ground;
    private readonly WaterBasinWorld _basins;

    private readonly Dictionary<
        (Vector2 Point, Vector2 Direction, bool IgnoreChance), Result> _cache = new();

    private readonly Queue<
        (Vector2 Point, Vector2 Direction, bool IgnoreChance)> _order = new();
    #endregion

    #region Construction
    // =========================================================
    // Connect to lightweight surface services belonging to this world.
    public CaveSurfaceSampler(Node world, ChunkController chunks)
    {
        _world = world;
        WorldConfig config = WorldConfig.Find(world);
        config.ValidateLayerConnectionFrequency(config.GetLayerCatalog());
        _connectionFrequency = config.LayerConnectionFrequency.TryGetValue(WorldLayerId.Surface, out float frequency)
            ? frequency : 1f;
        _chunks = chunks;
        _generator = world.GetNode<WorldGenerator>("Systems/WorldGenerator");
        _elevation = world.GetNode<TerrainElevation>("Systems/TerrainElevation");
        _ground = world.GetNode<Node2D>("GroundChunks");
        _slopes = TerrainSlopeWorld.Ensure(world);
        _basins = WaterBasinWorld.Find(world);
    }
    #endregion

    #region Sampling
    // =========================================================
    // Validate a mouth and its outside landing using generation data only.
    public IEnumerable<int> Evaluate(
        Vector2 point, Vector2 direction,
        bool ignoreChance, Result result)
    {
        var key = (point, direction, ignoreChance);

        if (_cache.TryGetValue(key, out Result cached))
        {
            result.CopyFrom(cached);
            yield break;
        }

        result.Accepted = false;
        result.Reason = "Outside world";

        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(point), _chunks.TileSize);

        BiomeDefinition biome = _generator.GetBiome(tile);
        CaveHoleProfile profile =
            biome.GetFeature<CaveHoleProfile>("cave_holes");

        result.BiomeId = biome.Id;

        if (profile == null || !profile.Enabled)
        {
            result.Reason = "Biome does not permit cave holes";
            Remember(key, result);
            yield break;
        }

        profile.Validate();
        result.ClearRadius = profile.ClearRadius;

        yield return 0;

        uint hash = IsoGrid.Hash(
            Mathf.RoundToInt(tile.X),
            Mathf.RoundToInt(tile.Y),
            _chunks.WorldSeed ^ 0xCA7E015u);

        double roll = hash / 4294967296.0;

        if (!ignoreChance && roll >= Mathf.Min(1f, profile.Chance * _connectionFrequency))
        {
            result.Reason = "Probability check";
            Remember(key, result);
            yield break;
        }

        Vector2 outside = point -
            IsoGrid.TileToWorld(direction * 0.9f, _chunks.TileSize);

        Vector2 footprint = Vector2.One * (profile.ClearRadius * 2f);

        foreach (Vector2 centre in new[] { point, outside })
        {
            if (!_chunks.IsDestinationWithinBounds(
                    centre, profile.ClearRadius))
            {
                result.Reason = "Outside world";
                Remember(key, result);
                yield break;
            }

            if (_basins != null &&
                _basins.OverlapsWorldFootprint(
                    centre, footprint, Vector2.Zero))
            {
                result.Reason = "Basin reservation";
                Remember(key, result);
                yield break;
            }

            if (!ChasmFeature.HasGroundClearance(
                    _ground.ToLocal(centre),
                    _chunks.TileSize,
                    profile.ClearRadius))
            {
                result.Reason = "Chasm clearance";
                Remember(key, result);
                yield break;
            }

            yield return 0;
        }

        float minimum = float.MaxValue;
        float maximum = float.MinValue;

        // Sample both the mouth and the outside landing.
        foreach (Vector2 centre in new[] { point, outside })
        {
            for (int y = -2; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                Vector2 samplePoint = centre +
                    new Vector2(x, y) * (profile.ClearRadius * 0.5f);

                Vector2 sampleTile = IsoGrid.WorldToTile(
                    _ground.ToLocal(samplePoint), _chunks.TileSize);

                BiomeDefinition sampleBiome =
                    _generator.GetBiome(sampleTile);

                CaveHoleProfile sampleProfile =
                    sampleBiome.GetFeature<CaveHoleProfile>("cave_holes");

                if (sampleProfile == null || !sampleProfile.Enabled)
                {
                    result.Reason = "Clearance crosses a forbidden biome";
                    Remember(key, result);
                    yield break;
                }

                if (!_slopes.HasClearance(samplePoint, 0f))
                {
                    result.Reason = "Terrain too steep";
                    Remember(key, result);
                    yield break;
                }

                float height = _elevation.SampleWorldHeight(samplePoint);
                minimum = Mathf.Min(minimum, height);
                maximum = Mathf.Max(maximum, height);

                if (maximum - minimum > profile.MaximumHeightVariation)
                {
                    result.Reason = "Height variation too large";
                    Remember(key, result);
                    yield break;
                }

                yield return 0;
            }
        }

        result.RimHeight = _elevation.SampleWorldHeight(point);
        result.Accepted = true;
        result.Reason = "Suitable";
        Remember(key, result);
    }

    // =========================================================
    // Bound cached decisions so sampling cannot grow memory indefinitely.
    private void Remember(
        (Vector2 Point, Vector2 Direction, bool IgnoreChance) key,
        Result result)
    {
        if (_cache.ContainsKey(key)) return;

        while (_cache.Count >= CacheLimit)
            _cache.Remove(_order.Dequeue());

        Result copy = new();
        copy.CopyFrom(result);
        _cache.Add(key, copy);
        _order.Enqueue(key);
    }
    #endregion
}
'@
$Changes['WORLD/Streaming/InfiniteWorldGeneration.cs'] = @'
// Connects local surface metadata and the shared infinite cave network.
// Both chunk streamers prepare the same seeded reservations before construction.
using Godot;
using System;
using System.Collections.Generic;

public partial class InfiniteWorldGeneration : Node
{
    #region Configuration
    [Export] public bool Enabled { get; set; } = true;
    #endregion

    #region State
    private Node _world;
    private WaterBasinWorld _basins;
    private CaveEntrancePlanner _planner;
    private bool _initialized;

    public WorldLayerRuntime Worlds { get; private set; }
    public WorldGenerationQueries Queries { get; private set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before streamers begin their first frame of construction.
    public override void _EnterTree()
    {
        AddToGroup("infinite_generation");
    }

    // =========================================================
    // Build shared services without scanning any world-sized area.
    public override void _Ready()
    {
        _world = GetParent();
        _basins = WaterBasinWorld.Find(_world);

        ChunkController chunks =
            _world.GetNode<ChunkController>("Systems/ChunkController");
        WorldConfig config = WorldConfig.Find(_world);

        if (_basins == null)
            throw new InvalidOperationException("Missing water basin service.");

        if (!float.IsFinite(config.CaveDiscoveryRadiusTiles) ||
            config.CaveDiscoveryRadiusTiles < 128f)
            throw new InvalidOperationException(
                "CaveDiscoveryRadiusTiles must be finite and at least 128.");

        if (Enabled && config.GenerateCaves)
        {
            WorldLayerDefinition definition =
                config.GetLayerCatalog().Get(config.GetLayerCatalog().SurfaceEntranceLayerId);
            CaveGenerationSettings settings = definition.CreateCaveSettings();

            _planner = new CaveEntrancePlanner(
                _world, chunks, settings, definition.FloorElevation);
            Player player = _world.GetNode<Player>("WorldObjects/Player");
            Worlds = new WorldLayerRuntime { Name = "LayerWorlds" };
            AddChild(Worlds);
            Worlds.Initialize(_world, player, chunks, _planner);

            WorldLayerController layers = new() { Name = "WorldLayers" };
            AddChild(layers);
            layers.Configure(_world, player, Worlds);
        }

        Queries = new WorldGenerationQueries(_world, Worlds);
        _initialized = true;
        SetProcess(false);
    }

    // =========================================================
    // Resolve shared planning from either layer's chunk builder.
    public static InfiniteWorldGeneration Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup(
            "infinite_generation") as InfiniteWorldGeneration;
    }
    #endregion

    #region Local Preparation
    // =========================================================
    // Finish local water and entrance decisions before geometry or props sample them.
    public IEnumerable<int> PrepareArea(
        Rect2 area, string layer = WorldLayerId.Surface)
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Infinite generation did not initialize successfully.");

        if (layer != WorldLayerId.Surface &&
            layer != Worlds?.SurfaceEntranceLayerId)
        {
            Worlds?.GetUnderground(layer);
            yield break;
        }

        foreach (int step in _basins.PrepareArea(area.Grow(2f)))
            yield return step;

        if (_planner != null)
            foreach (int step in _planner.PrepareArea(area, Worlds.SurfaceUnderground))
                yield return step;
    }
    #endregion

        // =========================================================
    // Hold both surface and entrance metadata until the owning chunk retires.
    public IDisposable PinArea(
        Rect2 area, string layer = WorldLayerId.Surface)
    {
        if (!_initialized)
            throw new InvalidOperationException(
                "Infinite generation did not initialize successfully.");

        if (layer != WorldLayerId.Surface &&
            layer != Worlds?.SurfaceEntranceLayerId)
        {
            Worlds?.GetUnderground(layer);
            return new GenerationLease(() => { });
        }

        IDisposable water = _basins.PinArea(area.Grow(4f));
        IDisposable entrances = null;

        try
        {
            entrances = _planner?.PinArea(area);
        }
        catch
        {
            water.Dispose();
            throw;
        }

        return new GenerationLease(() =>
        {
            entrances?.Dispose();
            water.Dispose();
        });
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
    // Include entity recipes so death identities cannot silently change species or prefab.
    public static void CaptureEntityDefinitions(CampaignData data)
    {
        foreach (EntitySaveData actor in EntitySaves.ReadSection(data).Entities)
        {
            Stamp(actor.Definition, data.Resources); Stamp(actor.Scene, data.Resources);
        }
        foreach (EntityDeathData entry in EntityDeaths.ReadSection(data).Entries)
        {
            Stamp(entry.Definition, data.Resources);
            Stamp(entry.Scene, data.Resources);
        }
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
        // Older campaigns had no multiplier. Preserve the old surface probability,
        // then include these defaults in the recipe written on the next successful Save.
        const string frequency = nameof(GlobalConfig.LayerConnectionFrequency);
        if (!data.Settings["CONFIG"].ContainsKey(frequency))
        {
            WorldConfig config = WorldConfig.Find(world);
            config.LayerConnectionFrequency = GlobalConfig.DefaultLayerConnectionFrequency();
            data.Settings["CONFIG"][frequency] = GD.VarToStr(config.Get(frequency));
        }
        world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed = data.Seed;
    }
}
'@
$Changes['WORLD/Generation/Queries/WorldGenerationQueries.cs'] = @'
// Queries this world's seeded biomes without loading chunks or spawning content.
// Planned feature/connection queries will extend this facade in the next pass.
using Godot;
using System;

public sealed class WorldGenerationQueries
{
    #region Services
    private readonly WorldConfig _config;
    private readonly WorldGenerator _surface;
    private readonly Node2D _ground;
    private readonly ChunkController _chunks;
    private readonly WorldLayerRuntime _layers;

    // =========================================================
    // Reuse existing generation services rather than constructing duplicate worlds.
    public WorldGenerationQueries(Node world, WorldLayerRuntime layers)
    {
        _config = WorldConfig.Find(world);
        _surface = world.GetNode<WorldGenerator>("Systems/WorldGenerator");
        _ground = world.GetNode<Node2D>("GroundChunks");
        _chunks = world.GetNode<ChunkController>("Systems/ChunkController");
        _layers = layers;
        _config.ValidateLayerConnectionFrequency(_config.GetLayerCatalog());
    }
    #endregion

    #region Biomes
    // =========================================================
    // Sample logical global ground coordinates, never height-shifted screen positions.
    public BiomeDefinition BiomeAt(string layer, Vector2 worldPosition)
    {
        if (!worldPosition.IsFinite())
            throw new ArgumentException("Biome queries require finite world coordinates.");
        WorldLayerDefinition definition = _config.GetLayerCatalog().Get(layer);
        if (definition.Kind == WorldLayerKind.Surface)
            return _surface.GetBiome(IsoGrid.WorldToTile(
                _ground.ToLocal(worldPosition), _chunks.TileSize));
        CaveWorld cave = _layers?.GetUnderground(layer)
            ?? throw new InvalidOperationException($"Underground generation is unavailable for '{layer}'.");
        return cave.Generator.Biomes.GetBiome(cave.WorldToTile(worldPosition));
    }
    #endregion

    #region Connection Tuning
    // =========================================================
    // Scale the upper layer's candidate probability without generating a corridor.
    public float ConnectionChance(string upperLayer, float baseChance)
    {
        _config.GetLayerCatalog().Get(upperLayer);
        return _config.ScaleLayerConnectionChance(upperLayer, baseChance);
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
2. **Permanent connection planner — pending.** Explicit layer-pair rules, seeded stable connection IDs, discovery from either side, bounded planning/caching, valid corridor endpoints and shared spacing rules. Replace reliance on the temporary deep-cavern test without making that debug script permanent save data.
3. **Restore integration — pending; completes save Pass 6.2.** Reconstruct nearby routes before terrain/player activation, support loading at any defined depth, preserve necessary route discovery/pins and verify travel back through multiple layers. Save only non-reconstructible identities or gameplay modifications.
4. **Return to save Pass 6.3 and Pass 7.** Liquid/basin changes, then combined persistence and menu verification.

## Save Boundaries

Seeded biomes, base chambers and unchanged natural corridors are reconstructed. Player layer/position, harvested vegetation, destroyed resources, entities, items, buildings, boss defeat and changed/player-created connections are persistent gameplay state. Feature/lair planning and flower hooks are future work; Pass 1 supplies biome queries only.

## Checks

Biome queries must match the existing samplers at the same position, work for inactive layers, leave chunk counts/active layer unchanged and reject unknown IDs. Frequency 1 preserves surface decisions; 0 rejects normal surface probability checks; 0.5/2 scale and cap candidate probability. Existing explicit debug probability bypasses remain intact. Test loading unexplored routes from below when the permanent planner is implemented.

Pass 1 automated verification passed: biome equality against existing samplers at positive/negative coordinates for all three currently defined layers; unchanged underground chunk counts/active layer; multiplier scaling/capping, normal surface 0/1 decisions and explicit debug bypass; invalid IDs/values; typed dictionary save-recipe roundtrip and migration of recipes missing the setting. Local visuals and changed-layout exploration still need gameplay checks.
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
'@
$Expected['CONFIG/GlobalConfig.cs'] = 'cc723a1409e76875bdf7da29f78f824edf24d437b032528be374687dce7b11ae'
$Expected['WORLD/Generation/Caves/Surface/CaveSurfaceSampler.cs'] = '68552d4eb1227a567544816d8fd62fff47fb7d620198c8dfeb65de04a1f95e86'
$Expected['WORLD/Streaming/InfiniteWorldGeneration.cs'] = '9b83fec26ad94c8580af53c7507b7cda0779889d0742f028c1026816f7ffe581'
$Expected['SYSTEMS/Saving/CampaignRecipe.cs'] = '78513ab7980a2f0b2d8d4f2f1c82ec4457d5497fef80d4fbbcbcfc4001eb78d5'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = '6c6a630661771d4a63eada657a1b676ee4d59429d63bd0616a7988de9e0ae978'
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '004952890fb3f909f5c704a99d0775d868ab932ae67a020beac6fcfd7e2ac177'
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
if (!$Pending.Count) { Write-Host 'Layer Query/Tuning Pass 1 is already installed. No files changed.'; return }
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
Write-Host 'Reopen Godot and build C#. CONFIG now exposes LAYER CONNECTION FREQUENCY.'
Write-Host 'Surface multiplier 1 preserves existing probability. Restart/use a new campaign to test changed generation tuning.'
Write-Host 'Deeper frequency entries await the permanent connection planner; debug connections are unchanged.'
