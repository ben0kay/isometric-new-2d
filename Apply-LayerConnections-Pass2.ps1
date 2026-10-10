# Installs Layer Connection Planner Pass 2 against c78dcc5. No Git operations.
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
$Changes['WORLD/Layers/WorldLayerDefinition.cs'] = @'
// Describes one world layer independently from its runtime world instance.
using Godot;
using System;

public enum WorldLayerKind { Surface, Underground }

[Tool, GlobalClass]
public partial class WorldLayerDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string Id { get; set; } = "";
    [Export] public string DisplayName { get; set; } = "";
    [Export] public int DepthIndex { get; set; }
    [Export] public WorldLayerKind Kind { get; set; }
    #endregion

    #region Generation
    [ExportGroup("Generation")]
    [Export] public BiomeCatalog Biomes { get; set; }
    [Export] public CaveGenerationSettings CaveSettings { get; set; }
    [Export] public float FloorElevation { get; set; }
    #endregion

    #region Downward Connections
    [ExportGroup("DOWNWARD CONNECTIONS")]
    [Export] public string DownwardLayerId { get; set; } = "";
    [Export(PropertyHint.Range, "0,1,0.01")] public float ConnectionChance { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "64,2048,16")] public float ConnectionSpacingTiles { get; set; } = 256f;
    [Export(PropertyHint.Range, "6,128,1")] public float ConnectionLengthTiles { get; set; } = 24f;
    #endregion

    #region Validation
    // =========================================================
    // Validate ownership and required resources before world construction.
    public void Validate()
    {
        WorldLayerId.Validate(Id);

        if (string.IsNullOrWhiteSpace(DisplayName) || Biomes == null)
            throw new InvalidOperationException(
                $"Layer '{Id}' requires a display name and biome catalog.");

        if (Kind == WorldLayerKind.Surface)
        {
            if (Id != WorldLayerId.Surface || DepthIndex != 0 ||
                CaveSettings != null || FloorElevation != 0f)
                throw new InvalidOperationException(
                    "The surface requires ID 'surface', depth 0, elevation 0 and no cave settings.");
        }
        else if (Kind == WorldLayerKind.Underground)
        {
            if (Id == WorldLayerId.Surface || DepthIndex < 1 ||
                CaveSettings == null || !float.IsFinite(FloorElevation) ||
                FloorElevation >= 0f)
                throw new InvalidOperationException(
                    $"Underground layer '{Id}' requires a positive depth, negative elevation and cave settings.");
        }
        else
            throw new InvalidOperationException($"Layer '{Id}' has an invalid kind.");

        if (DownwardLayerId == null || !float.IsFinite(ConnectionChance) || ConnectionChance < 0f || ConnectionChance > 1f ||
            !float.IsFinite(ConnectionSpacingTiles) || ConnectionSpacingTiles < 64f || ConnectionSpacingTiles > 2048f ||
            !float.IsFinite(ConnectionLengthTiles) || ConnectionLengthTiles < 6f || ConnectionLengthTiles > 128f)
            throw new InvalidOperationException($"Layer '{Id}' has invalid connection rules.");
        if (!string.IsNullOrEmpty(DownwardLayerId))
        {
            WorldLayerId.Validate(DownwardLayerId);
            if (Kind != WorldLayerKind.Underground || DownwardLayerId == Id)
                throw new InvalidOperationException("The surface uses its existing entrance planner; cave targets must be distinct.");
        }

        foreach (BiomeDefinition biome in Biomes.GetEnabledBiomes())
            if ((biome is CaveBiomeDefinition) !=
                (Kind == WorldLayerKind.Underground))
                throw new InvalidOperationException(
                    $"Biome '{biome.Id}' belongs to the wrong generation kind for '{Id}'.");
    }

    // =========================================================
    // Give the runtime its own settings while the definition owns biome selection.
    public CaveGenerationSettings CreateCaveSettings()
    {
        if (Kind != WorldLayerKind.Underground || CaveSettings == null)
            throw new InvalidOperationException($"Layer '{Id}' is not underground.");

        CaveGenerationSettings settings =
            (CaveGenerationSettings)CaveSettings.Duplicate();
        settings.Biomes = Biomes;
        settings.Validate();
        return settings;
    }
    #endregion
}
'@
$Changes['WORLD/Layers/WorldLayerCatalog.cs'] = @'
// Resolves authored world-layer definitions through one validated runtime index.
// IDs are indexed at startup; changing definitions requires restarting the world.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class WorldLayerCatalog : Resource
{
    #region Definitions
    [Export] public string SurfaceEntranceLayerId { get; set; }
        = WorldLayerId.Underground1;
    [Export] public Godot.Collections.Array<WorldLayerDefinition> Layers { get; set; }
        = new();

    private Dictionary<string, WorldLayerDefinition> _index;
    #endregion

    #region Queries
    // =========================================================
    // Build the index once and reject missing or duplicate definitions.
    public void Initialize()
    {
        if (_index != null) return;

        Dictionary<string, WorldLayerDefinition> index =
            new(StringComparer.Ordinal);

        foreach (WorldLayerDefinition layer in Layers)
        {
            if (layer == null)
                throw new InvalidOperationException("The layer catalog has a missing entry.");

            layer.Validate();
            if (!index.TryAdd(layer.Id, layer))
                throw new InvalidOperationException($"Duplicate layer ID: '{layer.Id}'.");
        }

        if (!index.ContainsKey(WorldLayerId.Surface))
            throw new InvalidOperationException("The layer catalog requires 'surface'.");

        if (!index.TryGetValue(SurfaceEntranceLayerId, out WorldLayerDefinition entry) ||
            entry.Kind != WorldLayerKind.Underground)
            throw new InvalidOperationException(
                "SurfaceEntranceLayerId must identify an underground layer.");

        foreach (WorldLayerDefinition layer in Layers)
        {
            if (string.IsNullOrEmpty(layer.DownwardLayerId)) continue;
            if (!index.TryGetValue(layer.DownwardLayerId, out WorldLayerDefinition destination) ||
                destination.Kind != WorldLayerKind.Underground || destination.DepthIndex <= layer.DepthIndex)
                throw new InvalidOperationException($"Layer '{layer.Id}' must connect to a defined deeper underground layer.");
        }
        _index = index;
    }

    // =========================================================
    // Require an exact identity rather than silently selecting another layer.
    public WorldLayerDefinition Get(string id)
    {
        Initialize();
        WorldLayerId.Validate(id);

        return _index.TryGetValue(id, out WorldLayerDefinition layer)
            ? layer : throw new InvalidOperationException($"Unknown world layer: '{id}'.");
    }
    #endregion
}
'@
$Changes['WORLD/Layers/Definitions/Underground1/Underground1.tres'] = @'
[gd_resource type="Resource" script_class="WorldLayerDefinition" load_steps=4 format=3]

[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerDefinition.cs" id="1_definition"]
[ext_resource type="Resource" path="res://WORLD/Generation/Caves/Biomes/CaveBiomeCatalog.tres" id="2_biomes"]
[ext_resource type="Resource" path="res://WORLD/Generation/Caves/DefaultCaveGeneration.tres" id="3_settings"]

[resource]
script = ExtResource("1_definition")
Id = "underground_1"
DisplayName = "Upper Caverns"
DepthIndex = 1
FloorElevation = -160.0
Kind = 1
Biomes = ExtResource("2_biomes")
CaveSettings = ExtResource("3_settings")
DownwardLayerId = "underground_2"
'@
$Changes['WORLD/Layers/WorldLayerRuntime.cs'] = @'
// Owns independent underground runtime worlds and resolves them by exact layer ID.
// Dormant layers own services but build no chunks until activated or preloaded.
using Godot;
using System;
using System.Collections.Generic;

public partial class WorldLayerRuntime : Node
{
    #region State
    public string ActiveLayer { get; private set; } = WorldLayerId.Surface;
    public string SurfaceEntranceLayerId { get; private set; }
    public WorldLayerConnections Connections { get; private set; }
    public LayerConnectionPlanner ConnectionPlanner { get; private set; }
    public CaveWorld SurfaceUnderground => GetUnderground(SurfaceEntranceLayerId);

    private Node2D _surfaceObjects;
    private ChunkController _surfaceChunks;
    private readonly Dictionary<string, CaveWorld> _underground =
        new(StringComparer.Ordinal);
    public IEnumerable<CaveWorld> UndergroundWorlds => _underground.Values;
    #endregion

    #region Construction
    // =========================================================
    // Register one runtime owner for this gameplay world.
    public override void _EnterTree()
    {
        AddToGroup("world_layer_runtime");
        SetProcess(false);
    }

    // =========================================================
    // Build service instances only; chunk construction remains demand-driven.
    public void Initialize(Node world, Player player,
        ChunkController chunks, CaveEntrancePlanner surfacePlanner)
    {
        WorldLayerCatalog catalog = WorldConfig.Find(world).GetLayerCatalog();
        SurfaceEntranceLayerId = catalog.SurfaceEntranceLayerId;
        Connections = new WorldLayerConnections(this);
        _surfaceObjects = world.GetNode<Node2D>("WorldObjects");
        _surfaceChunks = chunks;
        Node2D ground = world.GetNode<Node2D>("GroundChunks");

        foreach (WorldLayerDefinition definition in catalog.Layers)
        {
            if (definition.Kind != WorldLayerKind.Underground) continue;
            bool surfaceDestination = definition.Id == SurfaceEntranceLayerId;

            CaveWorld cave = new()
            {
                Name = definition.Id,
                LayerId = definition.Id,
                Planner = surfaceDestination ? surfacePlanner : null
            };
            AddChild(cave);
            cave.Build(ground.GlobalPosition, chunks.TileSize,
                SeedFor(chunks.WorldSeed, definition.Id),
                surfaceDestination ? surfacePlanner.Settings
                    : definition.CreateCaveSettings());
            cave.Streaming.ConfigurePlayer(player);
            cave.SetActive(false);
            _underground.Add(definition.Id, cave);
        }

        ConnectionPlanner = new LayerConnectionPlanner(this, WorldConfig.Find(world));
        ActivateLayer(WorldLayerId.Surface);
        GD.Print($"[Layers] {_underground.Count} independent underground worlds ready.");
    }

    // =========================================================
    // Derive deterministic seeds without relying on randomized string hashes.
    private static uint SeedFor(uint worldSeed, string id)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char character in id)
                hash = (hash ^ character) * 16777619u;
            return worldSeed ^ hash;
        }
    }
    #endregion

    #region Queries
    // =========================================================
    // Find the runtime registry in the current gameplay scene.
    public static WorldLayerRuntime Find(Node context)
    {
        if (context == null || !context.IsInsideTree()) return null;
        return context.GetTree().GetFirstNodeInGroup(
            "world_layer_runtime") as WorldLayerRuntime;
    }

    // =========================================================
    // Resolve a constructed underground world without falling back to another.
    public CaveWorld GetUnderground(string id)
    {
        return _underground.TryGetValue(id, out CaveWorld cave)
            ? cave : throw new InvalidOperationException(
                $"No underground runtime exists for '{id}'.");
    }

    // =========================================================
    // Query optional ownership without creating any world or chunk.
    public CaveWorld TryGetUnderground(string id)
    {
        return id != null && _underground.TryGetValue(id, out CaveWorld cave)
            ? cave : null;
    }

    // =========================================================
    // Select the exact layer's world-object root.
    public Node2D ObjectsFor(string id)
    {
        return id == WorldLayerId.Surface
            ? _surfaceObjects : GetUnderground(id).Objects;
    }

    // =========================================================
    // Check completed terrain in the requested layer.
    public bool IsAvailable(string id, Vector2 point, float clearance = 0f)
    {
        return id == WorldLayerId.Surface
            ? _surfaceChunks.IsNavigationPointAvailable(point, clearance)
            : GetUnderground(id).Streaming.IsAvailable(point, clearance);
    }
    #endregion

    #region Activation
    // =========================================================
    // Activate one underground world and disable all other underground collisions.
    public void ActivateLayer(string id)
    {
        if (id != WorldLayerId.Surface) GetUnderground(id);
        ActiveLayer = id;

        foreach (CaveWorld cave in _underground.Values)
        {
            cave.Streaming.CancelPreload();
            cave.SetActive(cave.LayerId == id);
            cave.Streaming.SetEntrancePreloading(
                id == WorldLayerId.Surface &&
                cave.LayerId == SurfaceEntranceLayerId);
        }
    }

    // =========================================================
    // Request destination preparation without revealing or activating its world.
    public void Preload(string id, Vector2 point)
    {
        GetUnderground(id).Streaming.RequestPreload(point);
    }

    // =========================================================
    // Release temporary preparation once a connection no longer needs it.
    public void CancelPreload(string id)
    {
        GetUnderground(id).Streaming.CancelPreload();
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

        if (Worlds != null)
            foreach (int step in Worlds.ConnectionPlanner.PrepareArea(area, layer))
                yield return step;
        if (layer != WorldLayerId.Surface && layer != Worlds?.SurfaceEntranceLayerId)
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
            return Worlds.ConnectionPlanner.PinArea(area, layer);
        }

        IDisposable water = _basins.PinArea(area.Grow(4f));
        IDisposable entrances = null;
        IDisposable connections = null;

        try
        {
            entrances = _planner?.PinArea(area);
            connections = Worlds?.ConnectionPlanner.PinArea(area, layer);
        }
        catch
        {
            entrances?.Dispose();
            connections?.Dispose();
            water.Dispose();
            throw;
        }

        return new GenerationLease(() =>
        {
            connections?.Dispose();
            entrances?.Dispose();
            water.Dispose();
        });
    }
}
'@
$Changes['WORLD/Layers/Connections/WorldLayerConnections.cs'] = @'
// Registers shared connection metadata and indexes it into both underground worlds.
// Surface candidate discovery and debug placement feed the same traversal records.
using Godot;
using System;
using System.Collections.Generic;

public sealed class WorldLayerConnections
{
    #region State
    private readonly WorldLayerRuntime _runtime;
    private readonly Dictionary<string, WorldLayerConnection> _records = new();
    public IEnumerable<WorldLayerConnection> All => _records.Values;
    public WorldLayerConnections(WorldLayerRuntime runtime) { _runtime = runtime; }
    #endregion

    #region Registration
    // =========================================================
    // Validate both definitions before carving either side of a connection.
    public void Register(WorldLayerConnection connection, bool rebuild = false)
    {
        if (_records.TryGetValue(connection.Id, out WorldLayerConnection existing))
        {
            if (existing == connection) return;
            throw new InvalidOperationException($"Duplicate connection '{connection.Id}'.");
        }
        WorldLayerCatalog catalog = WorldConfig.Find(_runtime).GetLayerCatalog();
        WorldLayerDefinition upper = catalog.Get(connection.UpperLayer);
        WorldLayerDefinition lower = catalog.Get(connection.LowerLayer);
        if (lower.Kind != WorldLayerKind.Underground ||
            lower.DepthIndex <= upper.DepthIndex ||
            connection.RimHeight <= lower.FloorElevation + 32f)
            throw new InvalidOperationException("Connection must descend to a deeper layer.");

        _records.Add(connection.Id, connection);
        CaveWorld destination = _runtime.GetUnderground(connection.LowerLayer);
        CaveWorld source = _runtime.TryGetUnderground(connection.UpperLayer);
        connection.SampleArea = connection.RampArea.Expand(
            destination.Generator.RoomCentre(connection.AnchorCell));
        if (source != null) connection.SampleArea = connection.SampleArea.Expand(connection.UpperAnchor);
        connection.SampleArea = connection.SampleArea.Grow(Mathf.Max(8f,
            Mathf.Max(destination.Settings.MaximumTunnelWidth(), source?.Settings.MaximumTunnelWidth() ?? 0f) * 0.5f + 4f));
        destination.AttachConnection(connection);
        source?.AttachDeparture(connection);
        if (!rebuild) return;
        destination.Streaming.InvalidateArea(connection.RampArea.Expand(
            destination.Generator.RoomCentre(connection.AnchorCell)).Grow(4f));
        source?.Streaming.InvalidateArea(connection.RampArea.Expand(
            connection.UpperAnchor).Grow(4f));
    }

    // =========================================================
    // Release streamed surface decisions when no terrain or traveller pins them.
    public void Remove(WorldLayerConnection connection)
    {
        if (!_records.Remove(connection.Id)) return;
        _runtime.GetUnderground(connection.LowerLayer).DetachConnection(connection);
        _runtime.TryGetUnderground(connection.UpperLayer)?.DetachDeparture(connection);
    }
    #endregion

    #region Queries
    // =========================================================
    // Enumerate connections touching an exact layer, without depth-name assumptions.
    public IEnumerable<WorldLayerConnection> ForLayer(string layer)
    {
        foreach (WorldLayerConnection connection in _records.Values)
            if (connection.UpperLayer == layer || connection.LowerLayer == layer)
                yield return connection;
    }

    // =========================================================
    // Give pursuit the next known connection along the layer graph.
    public WorldLayerConnection Next(string from, string to, Vector2 position)
    {
        if (from == to) return null;
        Queue<string> queue = new();
        Dictionary<string, WorldLayerConnection> first = new();
        HashSet<string> visited = new() { from };
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            // Prefer a nearby mouth for the first hop when several join the same depths.
            List<WorldLayerConnection> links = new(ForLayer(current));
            if (current == from) links.Sort((a, b) =>
                a.UpperPosition.DistanceSquaredTo(position).CompareTo(
                    b.UpperPosition.DistanceSquaredTo(position)));
            foreach (WorldLayerConnection link in links)
            {
                string next = link.Other(current);
                if (!visited.Add(next)) continue;
                first[next] = current == from ? link : first[current];
                if (next == to) return first[next];
                queue.Enqueue(next);
            }
        }
        return null;
    }
    #endregion
}
'@
$Changes['WORLD/Layers/Connections/LayerConnectionPlanner.cs'] = @'
// Plans permanent cave-to-cave corridors from stable layer IDs and absolute cells.
// Both endpoints prepare identical metadata; unused decisions have a bounded cache.
using Godot;
using System;
using System.Collections.Generic;

public sealed class LayerConnectionPlanner
{
    #region Plans
    private sealed class Pair
    {
        public CaveWorld Upper, Lower;
        public float Pitch, Reach, Length, Chance;
        public uint Seed;
        public GenerationCellCache<WorldLayerConnection> Cells;
    }
    private readonly List<Pair> _pairs = new();
    private readonly Dictionary<string, List<Pair>> _byLayer = new(StringComparer.Ordinal);
    private readonly WorldLayerRuntime _runtime;
    #endregion

    #region Construction
    // =========================================================
    // Snapshot rules once; adding a depth requires definitions rather than new code.
    public LayerConnectionPlanner(WorldLayerRuntime runtime, WorldConfig config)
    {
        _runtime = runtime;
        foreach (WorldLayerDefinition definition in config.GetLayerCatalog().Layers)
        {
            if (definition.Kind != WorldLayerKind.Underground || string.IsNullOrEmpty(definition.DownwardLayerId)) continue;
            CaveWorld upper = runtime.GetUnderground(definition.Id);
            CaveWorld lower = runtime.GetUnderground(definition.DownwardLayerId);
            float length = WorldLayerConnection.LengthFor(config, definition.ConnectionLengthTiles);
            float reach = upper.Settings.CellSpacingTiles * 1.5f + upper.Settings.MaximumChamberRadius() + 8f + length +
                lower.Settings.CellSpacingTiles * 1.5f + lower.Settings.MaximumChamberRadius() +
                Mathf.Max(upper.Settings.MaximumTunnelWidth(), lower.Settings.MaximumTunnelWidth()) + 8f;
            Pair pair = new()
            {
                Upper = upper, Lower = lower, Length = length, Reach = reach,
                Pitch = Mathf.Max(definition.ConnectionSpacingTiles, reach * 2f + 16f),
                Chance = config.ScaleLayerConnectionChance(definition.Id, definition.ConnectionChance),
                Seed = StableSeed(upper.WorldSeed, definition.Id + ">" + lower.LayerId)
            };
            pair.Cells = new GenerationCellCache<WorldLayerConnection>(512, connection =>
            {
                if (connection != null) runtime.Connections.Remove(connection);
            });
            _pairs.Add(pair);
        }
        _pairs.Sort((a, b) => string.CompareOrdinal(a.Upper.LayerId, b.Upper.LayerId));
        foreach (Pair pair in _pairs)
            foreach (string layer in new[] { pair.Upper.LayerId, pair.Lower.LayerId })
            {
                if (!_byLayer.TryGetValue(layer, out List<Pair> pairs))
                    _byLayer.Add(layer, pairs = new List<Pair>());
                pairs.Add(pair);
            }
    }

    // =========================================================
    // Stable hashing is independent of runtime string hash randomization.
    private static uint StableSeed(uint seed, string identity)
    {
        unchecked { foreach (char character in identity) seed = (seed ^ character) * 16777619u; }
        return seed;
    }
    #endregion

    #region Candidate Geometry
    // =========================================================
    // Resolve only the pairs touching this layer, without scanning every depth.
    private IEnumerable<Pair> PairsFor(string layer) =>
        _byLayer.TryGetValue(layer, out List<Pair> pairs) ? pairs : Array.Empty<Pair>();

    // =========================================================
    // Conservatively include corridors whose remote endpoint touches this area.
    private static void Range(Pair pair, Rect2 area, out Vector2I first, out Vector2I last)
    {
        Rect2 nearby = area.Grow(pair.Reach);
        first = new(Mathf.FloorToInt(nearby.Position.X / pair.Pitch), Mathf.FloorToInt(nearby.Position.Y / pair.Pitch));
        last = new(Mathf.CeilToInt(nearby.End.X / pair.Pitch), Mathf.CeilToInt(nearby.End.Y / pair.Pitch));
    }

    // =========================================================
    // Compute raw decisions without consulting registered routes or physical chunks.
    private static WorldLayerConnection Candidate(Pair pair, Vector2I cell)
    {
        uint hash = IsoGrid.Hash(cell.X, cell.Y, pair.Seed);
        if (hash / 4294967296.0 >= pair.Chance) return null;
        Vector2 centre = new((cell.X + 0.5f) * pair.Pitch, (cell.Y + 0.5f) * pair.Pitch);
        Vector2I upperCell = pair.Upper.Generator.RoomCell(centre);
        Vector2 anchor = pair.Upper.Generator.RoomCentre(upperCell);
        Vector2 direction = (hash & 3u) switch
        { 0 => Vector2.Right, 1 => Vector2.Left, 2 => Vector2.Up, _ => Vector2.Down };
        Vector2 mouth = anchor + direction * Mathf.Ceil(pair.Upper.Settings.MaximumChamberRadius() + 6f);
        mouth = new(Mathf.Round(mouth.X), Mathf.Round(mouth.Y));
        Vector2 end = mouth + direction * pair.Length;
        Vector2I lowerCell = pair.Lower.Generator.RoomCell(end + direction * (pair.Lower.Settings.MaximumChamberRadius() + 4f));
        float height = pair.Upper.FloorElevation + pair.Upper.Generator.Biomes.At(mouth).FloorHeight(mouth);
        if (height <= pair.Lower.FloorElevation + 32f) return null;
        WorldLayerConnection connection = new($"L_{pair.Upper.LayerId}_{pair.Lower.LayerId}_{cell.X}_{cell.Y}",
            pair.Upper.LayerId, pair.Lower.LayerId, mouth, direction,
            pair.Upper.TileToWorld(mouth), height, pair.Length, lowerCell, anchor);
        connection.SampleArea = connection.RampArea.Expand(anchor).Expand(pair.Lower.Generator.RoomCentre(lowerCell)).Grow(
            Mathf.Max(8f, Mathf.Max(pair.Upper.Settings.MaximumTunnelWidth(), pair.Lower.Settings.MaximumTunnelWidth()) * 0.5f + 4f));
        return connection;
    }

    // =========================================================
    // Resolve adjacent-depth overlaps from raw seeded plans, never discovery order.
    private bool Conflicts(Pair owner, WorldLayerConnection candidate)
    {
        foreach (Pair other in _pairs)
        {
            if (other == owner || (other.Upper.LayerId != owner.Lower.LayerId && other.Lower.LayerId != owner.Upper.LayerId &&
                other.Upper.LayerId != owner.Upper.LayerId && other.Lower.LayerId != owner.Lower.LayerId)) continue;
            Range(other, candidate.SampleArea, out Vector2I first, out Vector2I last);
            for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                WorldLayerConnection neighbor = Candidate(other, new(x, y));
                if (neighbor != null && string.CompareOrdinal(neighbor.Id, candidate.Id) < 0 &&
                    neighbor.SampleArea.Intersects(candidate.SampleArea)) return true;
            }
        }
        return false;
    }
    #endregion

    #region Chunk Preparation And Lifetime
    // =========================================================
    // Pin every relevant pair until the requesting chunk or traveller releases it.
    public IDisposable PinArea(Rect2 area, string layer)
    {
        List<IDisposable> leases = new();
        foreach (Pair pair in PairsFor(layer))
        {
            if (pair.Upper.LayerId != layer && pair.Lower.LayerId != layer) continue;
            Range(pair, area, out Vector2I first, out Vector2I last);
            leases.Add(pair.Cells.Pin(first, last));
        }
        return new GenerationLease(() => { foreach (IDisposable lease in leases) lease.Dispose(); });
    }

    // =========================================================
    // Yield between candidates so existing chunk build budgets bound this work.
    public IEnumerable<int> PrepareArea(Rect2 area, string layer)
    {
        using IDisposable protection = PinArea(area, layer);
        foreach (Pair pair in PairsFor(layer))
        {
            if (pair.Upper.LayerId != layer && pair.Lower.LayerId != layer) continue;
            Range(pair, area, out Vector2I first, out Vector2I last);
            for (int y = first.Y; y <= last.Y; y++)
            for (int x = first.X; x <= last.X; x++)
            {
                Vector2I cell = new(x, y);
                if (pair.Cells.ContainsKey(cell)) continue;
                yield return 0;
                WorldLayerConnection connection = Candidate(pair, cell);
                if (connection != null && Conflicts(pair, connection)) connection = null;
                if (!pair.Cells.TryAdd(cell, connection) || connection == null) continue;
                _runtime.Connections.Register(connection);
            }
        }
    }
    #endregion
}
'@
$Changes['WORLD/Generation/Caves/CaveWorld.cs'] = @'
// Owns one independent underground world, its terrain and runtime services.
// Only the surface-connected layer owns surface entrance metadata.
using Godot;
using System.Collections.Generic;

public partial class CaveWorld : Node2D
{
    #region State
    public string LayerId { get; set; } = "";
    public WorldLayerDefinition Definition { get; private set; }
    public CaveGenerationSettings Settings { get; private set; }
    public uint WorldSeed { get; private set; }
    public CaveGenerator Generator { get; private set; }
    public CaveChunkController Streaming { get; private set; }

    public Node2D Root { get; private set; }
    public Node2D Objects { get; private set; }
    public CaveTerrainElevation Elevation { get; private set; }
    public Vector2 TileSize { get; private set; }
        public float FloorElevation { get; private set; }

    public ShaderMaterial GroundMaterial { get; private set; }
    public ImageTexture WhiteTexture { get; private set; }

    private WorldLayerMember _objectsMember;
    private readonly List<WorldLayerConnection> _connections = new();
    public IReadOnlyList<WorldLayerConnection> Connections => _connections;
    private const int ConnectionCellTiles = 128;
    private readonly Dictionary<Vector2I, List<WorldLayerConnection>> _connectionIndex = new();
    private readonly Dictionary<Vector2I, List<WorldLayerConnection>> _departureIndex = new();
    private readonly List<WorldLayerConnection> _departures = new();
    public IReadOnlyList<WorldLayerConnection> Departures => _departures;
    public bool Active { get; private set; }
        public CaveEntrancePlanner Planner { get; set; }
    #endregion

    #region Construction
    // =========================================================
    // Keep world ownership free of per-frame processing.
    public override void _EnterTree()
    {
        SetProcess(false);
    }

    // =========================================================
    // Build one shared cave network at the configured underground elevation.
    public void Build(
        Vector2 origin, Vector2 tileSize,
        uint worldSeed, CaveGenerationSettings settings)
    {
        Definition = WorldConfig.Find(this).GetLayerCatalog().Get(LayerId);
        if (Definition.Kind != WorldLayerKind.Underground)
            throw new System.InvalidOperationException(
                $"Layer '{LayerId}' cannot use cave generation.");

        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();
        TileSize = tileSize;
        FloorElevation = Definition.FloorElevation;
        WorldSeed = worldSeed;

        if (!float.IsFinite(FloorElevation) || FloorElevation >= 0f)
            throw new System.InvalidOperationException(
                "The underground layer floor elevation must be finite and below zero.");

                Generator = new CaveGenerator(
            Settings, worldSeed, this, FloorElevation);

        Root = new Node2D { Name = "CaveLayer" };
        AddChild(Root);
        Root.GlobalPosition = origin;
        WorldLayerMember.Attach(Root, LayerId);

        Objects = new Node2D { Name = "Objects", YSortEnabled = true };
        Root.AddChild(Objects);
        _objectsMember = WorldLayerMember.Attach(Objects, LayerId);

        Elevation = new CaveTerrainElevation
        {
            Name = "Elevation",
            World = this
        };
        AddChild(Elevation);

        GroundMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(
                "res://WORLD/Generation/Caves/Rendering/CaveGround.gdshader")
        };

        using Image image = Image.CreateEmpty(
            1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        WhiteTexture = ImageTexture.CreateFromImage(image);

        Root.AddChild(new Polygon2D
        {
            Name = "UndergroundBackground",
            ZIndex = -4,
            ZAsRelative = false,
            Color = new Color("#080d12"),
            Polygon = new[]
            {
                new Vector2(-1000000, -1000000),
                new Vector2(1000000, -1000000),
                new Vector2(1000000, 1000000),
                new Vector2(-1000000, 1000000)
            }
        });

        Streaming = new CaveChunkController { Name = "CaveStreaming" };
        AddChild(Streaming);
        Streaming.Configure(this);
        AddChild(new WorldNavigation { Name = "Navigation", Cave = this });
    }
    #endregion

    #region Activation
    // =========================================================
    // Separate chunk work from drawing and object processing on inactive depths.
    public void SetActive(bool active)
    {
        Active = active;
        Root.Visible = active;
        Root.Modulate = Colors.White;
        _objectsMember.SetActive(active);
        Streaming.SetActive(active);
    }
    // =========================================================
    // Reveal a paused departure layer without restoring its collisions or processing.
    public void SetPreview(float opacity)
    {
        if (Active) return;
        bool visible = opacity > 0.001f;
        if (Root.Visible != visible) Root.Visible = visible;
        Color colour = Root.Modulate;
        if (Mathf.IsEqualApprox(colour.A, opacity)) return;
        colour.A = opacity;
        Root.Modulate = colour;
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Convert logical world positions into the shared network coordinates.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(Root.ToLocal(point), TileSize);
    }

    // =========================================================
    // Convert shared cave coordinates back into logical world positions.
    public Vector2 TileToWorld(Vector2 tile)
    {
        return Root.ToGlobal(IsoGrid.TileToWorld(tile, TileSize));
    }

    // =========================================================
    // Project mesh vertices using the same entrance-aware height field.
    public Vector2 VisiblePoint(Vector2 tile)
    {
        return IsoGrid.TileToWorld(tile, TileSize) +
            Vector2.Up * Generator.VertexHeight(tile);
    }

    // =========================================================
    // Find the closest surface hole for underground preloading while above ground.
    public WorldLayerConnection NearestConnection(Vector2 point)
    {
        WorldLayerConnection best = null;
        float distance = float.MaxValue;

        foreach (WorldLayerConnection hole in _connections)
        {
            float candidate = point.DistanceSquaredTo(hole.UpperPosition);
            if (candidate >= distance) continue;
            distance = candidate;
            best = hole;
        }

        return best;
    }

    // =========================================================
    // Identify an entrance ramp without using the player's screen direction.
    public WorldLayerConnection TransitionAt(Vector2 tile)
    {
        foreach (WorldLayerConnection hole in _connections)
        {
            Vector2 local = hole.Coordinates(tile);
            if (local.X >= -1.5f &&
                local.X <= hole.TunnelLength + 6f &&
                Mathf.Abs(local.Y) < 2.5f)
                return hole;
        }
        return null;
    }

        // =========================================================
    // Read only the surface biome above a logical cave location.
    public BiomeDefinition GetSurfaceBiome(Vector2 caveTile)
    {
        WorldGenerator surface = GetTree().GetFirstNodeInGroup(
            "world_generator") as WorldGenerator;
        Node2D ground = surface.GetNode<Node2D>("../../GroundChunks");

        Vector2 surfaceTile = IsoGrid.WorldToTile(
            ground.ToLocal(TileToWorld(caveTile)), TileSize);
        return surface.GetBiome(surfaceTile);
    }
    #endregion

    #region Surface Reservations
// =========================================================
// Reserve both the surface mouth and its outside landing before props spawn.
public static bool IsHoleReserved(
    Node context, Vector2 point, Vector2 footprint, Vector2 padding)
{
    WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
    if (runtime == null) return false;
    CaveWorld world = runtime.SurfaceUnderground;

    float objectRadius = footprint.Length() * 0.5f + padding.Length();
    foreach (WorldLayerConnection hole in world.Connections)
    {
        if (hole.UpperLayer != WorldLayerId.Surface) continue;
        float radius = hole.ClearRadius + objectRadius;
        float squared = radius * radius;
        if (point.DistanceSquaredTo(hole.UpperPosition) < squared ||
            point.DistanceSquaredTo(hole.OutsidePosition(world.TileSize)) < squared)
            return true;
    }
    return false;
}
    #endregion

        #region Streamed Entrances
    // =========================================================
    // Register one prepared entrance before nearby terrain and objects generate.
    public void AttachConnection(WorldLayerConnection hole)
    {
        if (_connections.Contains(hole)) return;
        _connections.Add(hole);
        if (hole.UpperLayer != WorldLayerId.Surface) IndexConnection(_connectionIndex, hole, true);

        PackedScene scene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");

        CaveEntrance marker = scene.Instantiate<CaveEntrance>();
        marker.Name = $"Hole_{hole.Id}";
        marker.World = this;
        marker.Connection = hole;
        hole.Marker = marker;

        AddChild(marker);
        marker.GlobalPosition = hole.UpperPosition;
        marker.QueueRedraw();
    }

    // =========================================================
    // Release distant cached entrance artwork; its seed can recreate it later.
    public void DetachConnection(WorldLayerConnection hole)
    {
        _connections.Remove(hole);
        if (hole.UpperLayer != WorldLayerId.Surface) IndexConnection(_connectionIndex, hole, false);

        if (GodotObject.IsInstanceValid(hole.Marker))
            hole.Marker.QueueFree();

        hole.Marker = null;
    }

    // =========================================================
    // Index the upper approach independently from lower ramp ownership.
    public void AttachDeparture(WorldLayerConnection connection)
    {
        if (_departures.Contains(connection)) return;
        _departures.Add(connection);
        IndexConnection(_departureIndex, connection, true);
    }

    // =========================================================
    // Release an explicitly removed upper approach.
    public void DetachDeparture(WorldLayerConnection connection)
    {
        _departures.Remove(connection);
        IndexConnection(_departureIndex, connection, false);
    }

    // =========================================================
    // Restrict terrain sampling to mouths near the requested cave coordinate.
    public IEnumerable<WorldLayerConnection> NearbyConnections(Vector2 tile)
    {
        if (Planner != null)
            foreach (WorldLayerConnection connection in Planner.Nearby(tile))
                yield return connection;
        foreach (WorldLayerConnection connection in IndexedConnections(_connectionIndex, tile))
            if (connection.SampleArea.HasPoint(tile))
                yield return connection;
    }
    // =========================================================
    // Limit source floor/elevation sampling to corridors near the queried tile.
    public IEnumerable<WorldLayerConnection> NearbyDepartures(Vector2 tile) =>
        IndexedConnections(_departureIndex, tile);

    // =========================================================
    // Resolve one spatial bucket without scanning distant retained records.
    private static IEnumerable<WorldLayerConnection> IndexedConnections(
        Dictionary<Vector2I, List<WorldLayerConnection>> index, Vector2 tile)
    {
        Vector2I cell = new(Mathf.FloorToInt(tile.X / ConnectionCellTiles), Mathf.FloorToInt(tile.Y / ConnectionCellTiles));
        return index.TryGetValue(cell, out List<WorldLayerConnection> entries) ? entries : System.Array.Empty<WorldLayerConnection>();
    }

    // =========================================================
    // Index the complete corridor/room connector footprint and remove empty buckets.
    private static void IndexConnection(Dictionary<Vector2I, List<WorldLayerConnection>> index,
        WorldLayerConnection connection, bool add)
    {
        Rect2 area = connection.SampleArea;
        Vector2I first = new(Mathf.FloorToInt(area.Position.X / ConnectionCellTiles), Mathf.FloorToInt(area.Position.Y / ConnectionCellTiles));
        Vector2I last = new(Mathf.FloorToInt(area.End.X / ConnectionCellTiles), Mathf.FloorToInt(area.End.Y / ConnectionCellTiles));
        for (int y = first.Y; y <= last.Y; y++)
        for (int x = first.X; x <= last.X; x++)
        {
            Vector2I cell = new(x, y);
            if (add)
            {
                if (!index.TryGetValue(cell, out List<WorldLayerConnection> entries)) index.Add(cell, entries = new());
                entries.Add(connection);
            }
            else if (index.TryGetValue(cell, out List<WorldLayerConnection> entries))
            {
                entries.Remove(connection);
                if (entries.Count == 0) index.Remove(cell);
            }
        }
    }
    #endregion
}
'@
$Changes['WORLD/Generation/Caves/CaveGenerator.cs'] = @'
// Samples connected biome-shaped chambers and winding passages.
// Caches reusable layout geometry while preserving registered entrance ramps.
using Godot;
using System.Collections.Generic;

public sealed class CaveGenerator
{
#region State
public CaveGenerationSettings Settings { get; }
public CaveBiomeWorld Biomes { get; }
public float HubX { get; }

private const int CacheLimit = 1024;
private const int PassageSegments = 12;

private readonly uint _seed;
private readonly float _baseHeight;
private readonly float _entranceRadius;
private readonly CaveWorld _world;

private readonly Dictionary<Vector2I, Room> _rooms = new();
private readonly Queue<Vector2I> _roomOrder = new();

private sealed class Passage
{
    public Vector2[] Points;
    public float[] Radii;
    public Rect2 Bounds;
}

private sealed class Room
{
    public Vector2 Centre;
    public CaveBiomeWorld.Sample Profile;
    public Passage Horizontal, Vertical;
}
#endregion

    #region Construction
// =========================================================
// Apply global underground biome scale without modifying the shared resource.
public CaveGenerator(
    CaveGenerationSettings settings, uint worldSeed,
    CaveWorld world, float baseHeight)
{
    settings.Validate();

    float multiplier = WorldConfig.Find(world).CaveBiomeScaleMultiplier;
    float biomeSize = settings.BiomeSizeTiles * multiplier;

    if (!float.IsFinite(multiplier) || multiplier <= 0f ||
        !float.IsFinite(biomeSize) || biomeSize <= 0f)
    {
        throw new System.InvalidOperationException(
            "CaveBiomeScaleMultiplier must produce a finite, positive biome size.");
    }

    Settings = (CaveGenerationSettings)settings.Duplicate();
    Settings.BiomeSizeTiles = biomeSize;

    _seed = worldSeed ^ Settings.SeedOffset;
    _world = world;
    _baseHeight = baseHeight;
    _entranceRadius = Settings.MaximumTunnelWidth() * 0.5f;
    HubX = Settings.EntranceTunnelLengthTiles + 8f;
    Biomes = new CaveBiomeWorld(Settings, _seed);
}
    #endregion

    #region Floor Sampling
// =========================================================
// Preserve entrance ramps, then sample biome-shaped rooms and connected routes.
public bool IsFloor(Vector2I tile)
{
    Vector2 point = new(tile.X, tile.Y);

    foreach (WorldLayerConnection departure in _world.NearbyDepartures(point))
    {
        Vector2 local = departure.Coordinates(point);
        if (local.X >= -2f && local.X <= 1f && Mathf.Abs(local.Y) <= 3f)
            return local.X <= 0f && Mathf.Abs(local.Y) <= 1f;
        if (NearSegment(point, departure.UpperAnchor, departure.TileAt(-1f), 2f))
            return true;
    }

    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
    {
        Vector2 local = hole.Coordinates(point);

        if (local.X >= -2f && local.X <= hole.TunnelLength &&
            Mathf.Abs(local.Y) <= 3f)
        {
            return local.X >= 0f && Mathf.Abs(local.Y) <= 1f;
        }
    }

    foreach (WorldLayerConnection hole in _world.NearbyConnections(point))
    {
        Vector2 end = hole.TileAt(hole.TunnelLength);
        Vector2 room = Centre(hole.AnchorCell.X, hole.AnchorCell.Y);

        if (point.DistanceSquaredTo(end) <= 9f ||
            NearSegment(point, end, room, _entranceRadius))
            return true;
    }

    float spacing = Settings.CellSpacingTiles;
    int cx = Mathf.FloorToInt((point.X - HubX) / spacing + 0.5f);
    int cy = Mathf.FloorToInt(point.Y / spacing + 0.5f);

    for (int x = cx - 1; x <= cx + 1; x++)
    for (int y = cy - 1; y <= cy + 1; y++)
    {
        Room room = GetRoom(x, y);

        if (room.Profile.ChamberDistance(
            point, room.Centre, x, y) <= 0f ||
            InPassage(point, room.Horizontal) ||
            InPassage(point, room.Vertical))
            return true;
    }

    return false;
}

    // =========================================================
    // Keep the outside edge of each registered entrance physically open.
    public bool IsMouthEdge(Vector2I tile, Vector2I neighbour)
    {
        Vector2 a = new(tile.X, tile.Y);
        Vector2 b = new(neighbour.X, neighbour.Y);

        foreach (WorldLayerConnection departure in _world.NearbyDepartures(a))
        {
            Vector2 localA = departure.Coordinates(a);
            Vector2 localB = departure.Coordinates(b);
            if (localA.X <= 0f && localA.X > -1.01f && Mathf.Abs(localA.Y) <= 1f &&
                localB.X > 0f) return true;
        }

        foreach (WorldLayerConnection hole in _world.NearbyConnections(a))
        {
            Vector2 localA = hole.Coordinates(a);
            Vector2 localB = hole.Coordinates(b);

            if (localA.X >= 0f && localA.X < 1.01f &&
                Mathf.Abs(localA.Y) <= 1f && localB.X < 0f)
                return true;
        }

        return false;
    }
    #endregion

    #region Heights
    // =========================================================
    // Join entrance elevation to the local underground floor.
    public float VertexHeight(Vector2 tile)
    {
        float floor = _baseHeight + Biomes.At(tile).FloorHeight(tile);

        foreach (WorldLayerConnection departure in _world.NearbyDepartures(tile))
        {
            Vector2 local = departure.Coordinates(tile);
            if (local.X >= -2f && local.X <= 0.6f && Mathf.Abs(local.Y) <= 2.5f)
                return Mathf.Lerp(floor, departure.RimHeight,
                    Mathf.Clamp((local.X + 2f) / 2f, 0f, 1f));
        }

        foreach (WorldLayerConnection hole in _world.NearbyConnections(tile))
        {
            Vector2 local = hole.Coordinates(tile);
            if (local.X < -2f || local.X > hole.TunnelLength ||
                Mathf.Abs(local.Y) > 2.5f)
                continue;

            float t = Mathf.Clamp(local.X / hole.TunnelLength, 0f, 1f);
            t = t * t * (3f - 2f * t);
            return Mathf.Lerp(hole.RimHeight, floor, t);
        }

        return floor;
    }

    // =========================================================
    // Match the rendered floor triangles when sampling actor elevation.
    public float HeightAt(Vector2 tile)
    {
        Vector2 centre = new(
            Mathf.Floor(tile.X + 0.5f),
            Mathf.Floor(tile.Y + 0.5f));

        float u = tile.X - centre.X + 0.5f;
        float v = tile.Y - centre.Y + 0.5f;

        float a = VertexHeight(centre + new Vector2(-0.5f, -0.5f));
        float b = VertexHeight(centre + new Vector2(0.5f, -0.5f));
        float c = VertexHeight(centre + new Vector2(0.5f, 0.5f));
        float d = VertexHeight(centre + new Vector2(-0.5f, 0.5f));

        return v <= u
            ? a * (1f - u) + b * (u - v) + c * v
            : a * (1f - v) + c * u + d * (v - u);
    }
    #endregion

    #region Connection Anchors
    // =========================================================
    // Expose the shared chamber lattice for reserved connection approaches.
    public Vector2 RoomCentre(Vector2I cell) { return Centre(cell.X, cell.Y); }

    // =========================================================
    // Select a nearby normal chamber for the lower landing connector.
    public Vector2I RoomCell(Vector2 point)
    {
        return new Vector2I(Mathf.RoundToInt((point.X - HubX) / Settings.CellSpacingTiles),
            Mathf.RoundToInt(point.Y / Settings.CellSpacingTiles));
    }
    #endregion

    #region Cached Layout
    // =========================================================
    // Preserve shared room anchors so every biome and entrance can connect.
    private Vector2 Centre(int x, int y)
    {
        return new Vector2(
            HubX + x * Settings.CellSpacingTiles,
            y * Settings.CellSpacingTiles);
    }

    // =========================================================
    // Cache room profiles and curved paths without retaining unlimited world data.
    private Room GetRoom(int x, int y)
    {
        Vector2I key = new(x, y);
        if (_rooms.TryGetValue(key, out Room existing)) return existing;

        Vector2 centre = Centre(x, y);
        CaveBiomeWorld.Sample profile = Biomes.At(centre);

        Room room = new()
        {
            Centre = centre,
            Profile = profile,
            Horizontal = BuildPassage(
                centre, Centre(x - 1, y),
                IsoGrid.Hash(x, y, _seed ^ 5u))
        };

        float chance = profile.ExtraConnections;
        bool vertical = x % 4 == 0 ||
            Random(x, y, 6) < chance;

        if (vertical)
        {
            room.Vertical = BuildPassage(
                centre, Centre(x, y - 1),
                IsoGrid.Hash(x, y, _seed ^ 7u));
        }

        if (_rooms.Count >= CacheLimit)
            _rooms.Remove(_roomOrder.Dequeue());

        _rooms.Add(key, room);
        _roomOrder.Enqueue(key);
        return room;
    }

    // =========================================================
    // Build one curved route, blending biome width and bend along its length.
    private Passage BuildPassage(Vector2 a, Vector2 b, uint seed)
    {
        Passage passage = new()
        {
            Points = new Vector2[PassageSegments + 1],
            Radii = new float[PassageSegments + 1]
        };

        Vector2 normal = (b - a).Normalized().Orthogonal();
        Vector2 minimum = a, maximum = a;
        float maximumRadius = 0f;

        for (int i = 0; i <= PassageSegments; i++)
        {
            float t = (float)i / PassageSegments;
            Vector2 basePoint = a.Lerp(b, t);
            CaveBiomeWorld.Sample profile = Biomes.At(basePoint);

            Vector2 point = basePoint +
                normal * profile.PassageOffset(t, seed);

            float radius = profile.TunnelWidth * 0.5f;
            passage.Points[i] = point;
            passage.Radii[i] = radius;
            maximumRadius = Mathf.Max(maximumRadius, radius);

            minimum = new Vector2(
                Mathf.Min(minimum.X, point.X),
                Mathf.Min(minimum.Y, point.Y));
            maximum = new Vector2(
                Mathf.Max(maximum.X, point.X),
                Mathf.Max(maximum.Y, point.Y));
        }

        passage.Bounds = new Rect2(minimum, maximum - minimum)
            .Grow(maximumRadius + 0.01f);
        return passage;
    }

    // =========================================================
    // Reject distant routes before testing their short connected segments.
    private static bool InPassage(Vector2 point, Passage passage)
    {
        if (passage == null || !passage.Bounds.HasPoint(point))
            return false;

        for (int i = 0; i < passage.Points.Length - 1; i++)
        {
            float radius = Mathf.Max(
                passage.Radii[i], passage.Radii[i + 1]);

            if (NearSegment(
                point, passage.Points[i], passage.Points[i + 1], radius))
                return true;
        }

        return false;
    }

    // =========================================================
    // Include rounded segment ends so adjacent route sections remain connected.
    private static bool NearSegment(
        Vector2 point, Vector2 a, Vector2 b, float radius)
    {
        Vector2 line = b - a;
        float length = line.LengthSquared();
        float t = length > 0.0001f
            ? Mathf.Clamp((point - a).Dot(line) / length, 0f, 1f) : 0f;

        return point.DistanceSquaredTo(a + line * t) <= radius * radius;
    }

    // =========================================================
    // Choose optional links consistently without random-generator allocations.
    private float Random(int x, int y, uint salt)
    {
        uint hash = IsoGrid.Hash(x, y, _seed ^ salt);
        return (hash & 0xFFFFFFu) / 16777216f;
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
3. **Restore integration — pending; completes save Pass 6.2.** Reconstruct nearby routes before terrain/player activation, support loading at any defined depth, preserve necessary route discovery/pins and verify travel back through multiple layers. Save only non-reconstructible identities or gameplay modifications.
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
'@
$Expected['WORLD/Layers/WorldLayerDefinition.cs'] = 'e90f4c74cbe978b0582f77ed39df83bdb13348f05c23a7e1677f5845d1252b14'
$Expected['WORLD/Layers/WorldLayerCatalog.cs'] = 'f32ae036cf538cc04000a80810d87b3beefe70061ec514fe123b41ab205825c1'
$Expected['WORLD/Layers/Definitions/Underground1/Underground1.tres'] = 'b9368aa4883e59e4915dd4bd570b6b389bbe47ad2ab9b1b26e4eafb4db2306b0'
$Expected['WORLD/Layers/WorldLayerRuntime.cs'] = '8914263212540bcdc3d992c239e533d253b54012a7a3c5d33a7b60fe9ba6f901'
$Expected['WORLD/Streaming/InfiniteWorldGeneration.cs'] = '6e7c2a2f54318f8adeb413c4bd2da8665be9bcaf010478ab4af3deea98e43631'
$Expected['WORLD/Layers/Connections/WorldLayerConnections.cs'] = '58af6e7a3495f068f6588c66817ef002418827c3578e9bc4cf6e696d1cb90149'
$Expected['WORLD/Generation/Caves/CaveWorld.cs'] = 'c21437088ccd9993168e1809520221a44fa918a6e5c3d2aa429457fb8f8b2664'
$Expected['WORLD/Generation/Caves/CaveGenerator.cs'] = '24c34c894dd62998996cd11377ca0e089c9eba670d716a2890f3e4dbdc62d4f5'
$Expected['NOTES/OngoingWork/LayerGenerationConnections.md'] = '166994a8d1e56a39ef48e82123d8236fd7441358fbf5a5331b13870a8c1395d1'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = 'd44d3c8ff0e6bbbc1f7f4948dc3fcc2c0ae7a8acaa436e47d3222ce7b4645beb'
$Expected['WORLD/Layers/WorldLayerController.cs'] = '76052010e2ff6f17cef9e843c291965283d86a2527616a4edac65accfed6ce15'
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
if (!$Pending.Count) { Write-Host 'Layer Connection Planner Pass 2 is already installed. No files changed.'; return }
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
Write-Host 'Reopen Godot and build C#. Test the changed generation recipe with a new campaign.'
Write-Host 'Test natural Upper Caverns / Deep Caverns corridors with DeepCavernsTest disabled.'
Write-Host 'Deeper manual saving remains gated until restore integration. Existing save files were not modified.'
