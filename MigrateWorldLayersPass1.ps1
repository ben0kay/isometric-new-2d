# Migrates the fixed Surface/Cave enum to data-driven layer identities.
# Run once from the project root with Godot closed.
$ErrorActionPreference = "Stop"
$projectRoot = $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$pending = [ordered]@{}

if (!(Test-Path (Join-Path $projectRoot "project.godot"))) {
    throw "Place this script in the Godot project root."
}
if (!(Test-Path (Join-Path $projectRoot "WORLD/Layers/WorldLayer.cs"))) {
    throw "WorldLayer.cs is missing. This migration may already be applied."
}

# =========================================================
# Read into memory; nothing is written until all replacements pass.
function Read-Source([string]$path) {
    if ($pending.Contains($path)) { return $pending[$path] }

    $fullPath = Join-Path $projectRoot $path
    if (!(Test-Path $fullPath)) { throw "Missing file: $path" }

    $text = [IO.File]::ReadAllText($fullPath).Replace("`r`n", "`n")
    $pending[$path] = $text
    return $text
}

# =========================================================
# Normalize line endings and require exactly one matching block.
function Replace-Once([string]$path, [string]$before, [string]$after) {
    $text = Read-Source $path
    $before = $before.Replace("`r`n", "`n").Replace("`r", "`n")
    $after = $after.Replace("`r`n", "`n").Replace("`r", "`n")

    $index = $text.IndexOf($before, [StringComparison]::Ordinal)

    if ($index -lt 0) {
        throw "Block not found in $path. No files have been written. Expected:`n$before"
    }

    $second = $text.IndexOf(
        $before, $index + $before.Length,
        [StringComparison]::Ordinal)

    if ($second -ge 0) {
        throw "Multiple matching blocks in $path. No files have been written. Expected:`n$before"
    }

    $pending[$path] = $text.Replace($before, $after)
}

# =========================================================
# Convert existing consumers, excluding generated/build directories.
Get-ChildItem -LiteralPath $projectRoot -Recurse -File -Filter "*.cs" |
    ForEach-Object {
        $path = $_.FullName.Substring($projectRoot.Length + 1).Replace('\', '/')

        if ($path -eq "WORLD/Layers/WorldLayer.cs" -or
            $path -match '(^|/)(\.git|\.godot|\.vs|bin|obj)(/|$)') {
            return
        }

        $text = [IO.File]::ReadAllText($_.FullName).Replace("`r`n", "`n")
        if ($text -notmatch '\bWorldLayer\b') { return }

        $text = $text.Replace("WorldLayer.Surface", "WorldLayerId.Surface")
        $text = $text.Replace("WorldLayer.Cave", "WorldLayerId.Underground1")
        $pending[$path] = [regex]::Replace($text, '\bWorldLayer\b', 'string')
    }

# =========================================================
# Update defaults, configuration, construction and presentation.
$before = 'private string _lastLayer = (string)(-1);'
$after = 'private string _lastLayer;'
Replace-Once 'VISUALS/Shadows/GroundShadowWorld.cs' $before $after

$before = 'public string Layer { get; private set; }'
$after = 'public string Layer { get; private set; } = WorldLayerId.Surface;'
Replace-Once 'WORLD/Layers/WorldLayerMember.cs' $before $after

$before = @'
    public static WorldLayerMember Attach(Node root, string layer)
    {
'@
$after = @'
    public static WorldLayerMember Attach(Node root, string layer)
    {
        WorldLayerId.Validate(layer);
'@
Replace-Once 'WORLD/Layers/WorldLayerMember.cs' $before $after

$before = @'
public void SetLayer(string layer)
{
    Layer = layer;
'@
$after = @'
public void SetLayer(string layer)
{
    WorldLayerId.Validate(layer);
    Layer = layer;
'@
Replace-Once 'WORLD/Layers/WorldLayerMember.cs' $before $after

$before = '    #region Lookup'
$after = @'
    #region Layers
    [ExportGroup("WORLD LAYERS")]
    [Export] public WorldLayerCatalog LayerCatalog { get; set; }

    private WorldLayerCatalog _resolvedLayers;

    // =========================================================
    // Resolve and validate this world's layer registry once.
    public WorldLayerCatalog GetLayerCatalog()
    {
        if (_resolvedLayers != null) return _resolvedLayers;

        WorldLayerCatalog catalog = LayerCatalog ?? GD.Load<WorldLayerCatalog>(
            "res://WORLD/Layers/WorldLayers.tres");

        if (catalog == null)
            throw new InvalidOperationException("Missing world layer catalog.");

        catalog.Initialize();
        _resolvedLayers = catalog;
        return catalog;
    }
    #endregion

    #region Lookup
'@
Replace-Once 'CONFIG/WorldConfig.cs' $before $after

$before = @'
    [Export] public CaveGenerationSettings GenerationSettings { get; set; }

'@
$after = ''
Replace-Once 'WORLD/Streaming/InfiniteWorldGeneration.cs' $before $after

$before = @'
            CaveGenerationSettings settings = GenerationSettings ??
                GD.Load<CaveGenerationSettings>(
                    "res://WORLD/Generation/Caves/DefaultCaveGeneration.tres");

            if (settings == null)
                throw new InvalidOperationException("Missing cave settings.");
'@
$after = @'
            WorldLayerDefinition definition =
                config.GetLayerCatalog().Get(WorldLayerId.Underground1);
            CaveGenerationSettings settings = definition.CreateCaveSettings();
'@
Replace-Once 'WORLD/Streaming/InfiniteWorldGeneration.cs' $before $after

$before = @'
    if (Catalog == null)
        throw new InvalidOperationException(
            "WorldGenerator requires a BiomeCatalog.");
'@
$after = @'
    // Explicit catalogs remain available for sandbox scenes.
    Catalog ??= WorldConfig.Find(this).GetLayerCatalog()
        .Get(WorldLayerId.Surface).Biomes;
'@
Replace-Once 'WORLD/Generation/WorldGenerator.cs' $before $after

$before = '    #region State'
$after = @'
    #region State
    public string LayerId { get; set; } = WorldLayerId.Underground1;
    public WorldLayerDefinition Definition { get; private set; }
'@
Replace-Once 'WORLD/Generation/Caves/CaveWorld.cs' $before $after

$before = '        Settings = (CaveGenerationSettings)settings.Duplicate();'
$after = @'
        Definition = WorldConfig.Find(this).GetLayerCatalog().Get(LayerId);
        if (Definition.Kind != WorldLayerKind.Underground)
            throw new System.InvalidOperationException(
                $"Layer '{LayerId}' cannot use cave generation.");

        Settings = (CaveGenerationSettings)settings.Duplicate();
'@
Replace-Once 'WORLD/Generation/Caves/CaveWorld.cs' $before $after

$before = 'WorldLayerMember.Attach(this, WorldLayerId.Underground1)'
$after = 'WorldLayerMember.Attach(this, world.LayerId)'
Replace-Once 'WORLD/Generation/Caves/Chunks/CaveChunk.cs' $before $after

$before = @'
    public string Layer => Cave == null
        ? WorldLayerId.Surface : WorldLayerId.Underground1;
'@
$after = '    public string Layer => Cave?.LayerId ?? WorldLayerId.Surface;'
Replace-Once 'WORLD/Navigation/WorldNavigation.cs' $before $after

$before = '    public CaveWorld Cave { get; private set; }'
$after = @'
    public CaveWorld Cave { get; private set; }
    public WorldLayerDefinition CurrentDefinition =>
        _config.GetLayerCatalog().Get(Current);
'@
Replace-Once 'WORLD/Layers/WorldLayerController.cs' $before $after

$before = '    _config = WorldConfig.Find(world);'
$after = @'
    _config = WorldConfig.Find(world);
    _config.GetLayerCatalog().Get(Current);
'@
Replace-Once 'WORLD/Layers/WorldLayerController.cs' $before $after

$before = 'WorldLayerMember.Attach(cave.Root, WorldLayerId.Underground1)'
$after = 'WorldLayerMember.Attach(cave.Root, cave.LayerId)'
Replace-Once 'WORLD/Layers/WorldLayerController.cs' $before $after

$before = '$"CAVE | {Cave.Streaming.ReadyCount}'
$after = '$"{CurrentDefinition.DisplayName} | {Cave.Streaming.ReadyCount}'
Replace-Once 'WORLD/Layers/WorldLayerController.cs' $before $after

$before = '$"SURFACE | Nearest hole:'
$after = '$"{CurrentDefinition.DisplayName} | Nearest hole:'
Replace-Once 'WORLD/Layers/WorldLayerController.cs' $before $after

$before = '[node name="CONFIG"'
$after = @'
[ext_resource type="Resource" path="res://WORLD/Layers/WorldLayers.tres" id="2_layers"]

[node name="CONFIG"
'@
Replace-Once 'CONFIG/WorldConfig.tscn' $before $after

$before = 'script = ExtResource("1_config")'
$after = @'
script = ExtResource("1_config")
LayerCatalog = ExtResource("2_layers")
'@
Replace-Once 'CONFIG/WorldConfig.tscn' $before $after

$before = @'
[ext_resource type="Resource" uid="uid://htudqle6rjei" path="res://WORLD/Generation/Biomes/BiomeCatalog.tres" id="12_biomes"]

'@
$after = ''
Replace-Once 'WORLD/Scenes/world_infinite.tscn' $before $after

$before = @'
Catalog = ExtResource("12_biomes")

'@
$after = ''
Replace-Once 'WORLD/Scenes/world_infinite.tscn' $before $after

$before = 'The current system supports Surface and Cave layers.'
$after = @'
Pass 1 uses stable string identities: surface and underground_1.
WorldLayerDefinition supplies display names, depths, generation kinds,
and biome references. WorldLayerCatalog validates and resolves those IDs.
The old fixed WorldLayer enum has been removed.

Only the existing surface and first cave are instantiated currently.
Independent additional layer worlds and generalized connections are
planned for passes 2 and 3.
'@
Replace-Once 'WORLD/Generation/Caves/README.md' $before $after

$before = @'
1. Measure expensive work during surface/cave transitions.
2. Improve shared scheduling and destination preparation.
3. Introduce extensible layer definitions and explicit connections.
4. Test Surface -> Caves -> Deep Caves and the return journey.
5. Add richer underground content after traversal is reliable.

Multiple underground layers and a shared generation scheduler are planned;
they are not yet completed features.
'@
$after = @'
1. Establish layer identities and definitions (pass 1).
2. Support independent runtime layer worlds (pass 2).
3. Generalize connections and test two underground depths (pass 3).
4. Add richer underground content after traversal is reliable.

See NOTES/OngoingWork/README.md for the migration handoff.
Transition profiling and a shared generation scheduler remain separate
future work. Additional playable underground layers are not implemented
by pass 1.
'@
Replace-Once 'WORLD/Generation/Caves/README.md' $before $after

# =========================================================
# Stage the new identity, definition and registry files.
$newFiles = [ordered]@{}

$newFiles['WORLD/Layers/WorldLayerId.cs'] = @'
// Stable world-layer identities shared by actors, projectiles and world services.
// Display names and generation choices belong to layer definitions.
using System;

public static class WorldLayerId
{
    public const string Surface = "surface";
    public const string Underground1 = "underground_1";

    // =========================================================
    // Reject empty or unstable IDs without restricting future layer names.
    public static void Validate(string id)
    {
        if (string.IsNullOrEmpty(id))
            throw new ArgumentException("A world layer requires an ID.");

        foreach (char character in id)
            if (!(character >= 'a' && character <= 'z') &&
                !(character >= '0' && character <= '9') &&
                character != '_')
                throw new ArgumentException(
                    $"Layer ID '{id}' must use lowercase letters, digits or underscores.");
    }
}
'@

$newFiles['WORLD/Layers/WorldLayerDefinition.cs'] = @'
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
                CaveSettings != null)
                throw new InvalidOperationException(
                    "The surface layer must use ID 'surface', depth 0 and no cave settings.");
        }
        else if (Kind == WorldLayerKind.Underground)
        {
            if (Id == WorldLayerId.Surface || DepthIndex < 1 ||
                CaveSettings == null)
                throw new InvalidOperationException(
                    $"Underground layer '{Id}' requires a positive depth and cave settings.");
        }
        else
            throw new InvalidOperationException($"Layer '{Id}' has an invalid kind.");

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

$newFiles['WORLD/Layers/WorldLayerCatalog.cs'] = @'
// Resolves authored world-layer definitions through one validated runtime index.
// IDs are indexed at startup; changing definitions requires restarting the world.
using Godot;
using System;
using System.Collections.Generic;

[Tool, GlobalClass]
public partial class WorldLayerCatalog : Resource
{
    #region Definitions
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

$newFiles['WORLD/Layers/Definitions/Surface/Surface.tres'] = @'
[gd_resource type="Resource" script_class="WorldLayerDefinition" load_steps=3 format=3]

[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerDefinition.cs" id="1_definition"]
[ext_resource type="Resource" path="res://WORLD/Generation/Biomes/BiomeCatalog.tres" id="2_biomes"]

[resource]
script = ExtResource("1_definition")
Id = "surface"
DisplayName = "Surface"
DepthIndex = 0
Kind = 0
Biomes = ExtResource("2_biomes")
'@

$newFiles['WORLD/Layers/Definitions/Underground1/Underground1.tres'] = @'
[gd_resource type="Resource" script_class="WorldLayerDefinition" load_steps=4 format=3]

[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerDefinition.cs" id="1_definition"]
[ext_resource type="Resource" path="res://WORLD/Generation/Caves/Biomes/CaveBiomeCatalog.tres" id="2_biomes"]
[ext_resource type="Resource" path="res://WORLD/Generation/Caves/DefaultCaveGeneration.tres" id="3_settings"]

[resource]
script = ExtResource("1_definition")
Id = "underground_1"
DisplayName = "Upper Caverns"
DepthIndex = 1
Kind = 1
Biomes = ExtResource("2_biomes")
CaveSettings = ExtResource("3_settings")
'@

$newFiles['WORLD/Layers/WorldLayers.tres'] = @'
[gd_resource type="Resource" script_class="WorldLayerCatalog" load_steps=5 format=3]

[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerCatalog.cs" id="1_catalog"]
[ext_resource type="Script" path="res://WORLD/Layers/WorldLayerDefinition.cs" id="2_definition"]
[ext_resource type="Resource" path="res://WORLD/Layers/Definitions/Surface/Surface.tres" id="3_surface"]
[ext_resource type="Resource" path="res://WORLD/Layers/Definitions/Underground1/Underground1.tres" id="4_underground"]

[resource]
script = ExtResource("1_catalog")
Layers = Array[ExtResource("2_definition")]([ExtResource("3_surface"), ExtResource("4_underground")])
'@

# =========================================================
# Reject conflicting new files before writing anything.
foreach ($path in $newFiles.Keys) {
    if (Test-Path (Join-Path $projectRoot $path)) {
        throw "New file already exists: $path. No files have been written."
    }
    $pending[$path] = $newFiles[$path]
}

# =========================================================
# Ensure staged C# no longer refers to the removed enum.
foreach ($path in $pending.Keys) {
    if ($path.EndsWith(".cs") -and $pending[$path] -match '\bWorldLayer\b') {
        throw "Unconverted WorldLayer reference in $path. No files have been written."
    }
}

# =========================================================
# Save staged files, then remove the obsolete enum.
foreach ($path in $pending.Keys) {
    $fullPath = Join-Path $projectRoot $path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullPath)) |
        Out-Null
    [IO.File]::WriteAllText($fullPath, $pending[$path], $utf8)
}

Remove-Item -LiteralPath (Join-Path $projectRoot "WORLD/Layers/WorldLayer.cs")
$oldUid = Join-Path $projectRoot "WORLD/Layers/WorldLayer.cs.uid"
if (Test-Path $oldUid) { Remove-Item -LiteralPath $oldUid }

Write-Host "Pass 1 applied. Build, reopen Godot, then test surface/cave traversal."
Write-Host "CONFIG -> WORLD LAYERS -> Layer Catalog contains the layer definitions."
Write-Host "After testing, delete this one-time migration script."