# Organizes world infrastructure and updates project references.
# Preserves UIDs without changing generation or gameplay logic.
$ErrorActionPreference = "Stop"

if (!(Test-Path "project.godot")) {
    throw "Run this script from your Godot project root."
}

#region File Moves
$moves = [ordered]@{
    "WORLD/WorldObjectDefinition.cs" =
        "WORLD/Contents/Shared/WorldObjectDefinition.cs"
    "WORLD/WorldPlacement.cs" =
        "WORLD/Contents/Shared/WorldPlacement.cs"
    "WORLD/Obstacle.cs" =
        "WORLD/Contents/Shared/Obstacle.cs"

    "WORLD/Generation/InfiniteWorldGeneration.cs" =
        "WORLD/Streaming/InfiniteWorldGeneration.cs"
    "WORLD/Generation/GenerationCellCache.cs" =
        "WORLD/Streaming/GenerationCellCache.cs"
    "WORLD/Generation/GenerationMetadataLease.cs" =
        "WORLD/Streaming/GenerationMetadataLease.cs"

    "WORLD/Contents/Water/BiomeBasinGenerator.cs" =
        "WORLD/Generation/Terrain/Features/Basins/BiomeBasinGenerator.cs"

    "WORLD/world_infinite.tscn" =
        "WORLD/Scenes/world_infinite.tscn"
    "WORLD/world_test.tscn" =
        "WORLD/Scenes/world_test.tscn"

    "WORLD/Generation/Biomes/BiomeSampler.cs" =
        "WORLD/Generation/Biomes/Sampling/BiomeSampler.cs"
    "WORLD/Generation/Biomes/BiomeBlend.cs" =
        "WORLD/Generation/Biomes/Sampling/BiomeBlend.cs"
    "WORLD/Generation/Biomes/WorldClimate.cs" =
        "WORLD/Generation/Biomes/Sampling/WorldClimate.cs"

    "WORLD/Generation/Biomes/BiomeBasinProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeBasinProfile.cs"
    "WORLD/Generation/Biomes/BiomeColourProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeColourProfile.cs"
    "WORLD/Generation/Biomes/BiomeGroundProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeGroundProfile.cs"

    "WORLD/Generation/Biomes/BiomeContent.cs" =
        "WORLD/Generation/Biomes/Content/BiomeContent.cs"
    "WORLD/Generation/Biomes/BiomeVegetation.cs" =
        "WORLD/Generation/Biomes/Content/BiomeVegetation.cs"
    "WORLD/Generation/Biomes/BiomeSpecies.cs" =
        "WORLD/Generation/Biomes/Content/BiomeSpecies.cs"

    "WORLD/Generation/Biomes/BiomeScatter.cs" =
        "WORLD/Generation/Biomes/Placement/BiomeScatter.cs"
    "WORLD/Generation/Biomes/BiomePlacementSettings.cs" =
        "WORLD/Generation/Biomes/Placement/BiomePlacementSettings.cs"
}
#endregion

#region Preflight
# =========================================================
# Validate every source and destination before changing files.
foreach ($pair in $moves.GetEnumerator()) {
    if (!(Test-Path -LiteralPath $pair.Key -PathType Leaf)) {
        throw "Source file missing: $($pair.Key)"
    }

    if (Test-Path -LiteralPath $pair.Value) {
        throw "Destination already exists: $($pair.Value)"
    }

    if ($pair.Key.EndsWith(".cs")) {
        if (!(Test-Path -LiteralPath "$($pair.Key).uid")) {
            throw "Script UID missing: $($pair.Key).uid"
        }

        if (Test-Path -LiteralPath "$($pair.Value).uid") {
            throw "Destination UID already exists: $($pair.Value).uid"
        }
    }
}
#endregion

#region Reference Updates
# =========================================================
# Prepare exact path updates across scripts, resources and project settings.
$encoding = New-Object System.Text.UTF8Encoding($false)
$updates = @()

$files = Get-ChildItem -Recurse -File | Where-Object {
    $_.Extension -in @(
        ".cs", ".tscn", ".tres", ".gd", ".godot",
        ".gdshader", ".gdshaderinc", ".md", ".cfg", ".csproj"
    ) -and
    $_.FullName -notmatch '[\\/](\.git|\.godot|\.vs|bin|obj)[\\/]'
}

foreach ($file in $files) {
    $original = [System.IO.File]::ReadAllText($file.FullName)
    $text = $original

    foreach ($pair in $moves.GetEnumerator()) {
        $text = $text.Replace($pair.Key, $pair.Value)
    }

    if ($text -ne $original) {
        $updates += [pscustomobject]@{
            Path = $file.FullName
            Text = $text
        }
    }
}

# =========================================================
# Write only files whose references changed.
foreach ($update in $updates) {
    [System.IO.File]::WriteAllText(
        $update.Path, $update.Text, $encoding)

    Write-Host "Updated references: $($update.Path)"
}
#endregion

#region Move Files
# =========================================================
# Create destination folders and move scripts with their original UIDs.
foreach ($pair in $moves.GetEnumerator()) {
    $directory = Split-Path -Parent $pair.Value

    if (!(Test-Path -LiteralPath $directory)) {
        New-Item -ItemType Directory -Path $directory |
            Out-Null
    }

    Move-Item -LiteralPath $pair.Key -Destination $pair.Value

    if ($pair.Key.EndsWith(".cs")) {
        Move-Item -LiteralPath "$($pair.Key).uid" `
            -Destination "$($pair.Value).uid"
    }

    Write-Host "Moved: $($pair.Key) -> $($pair.Value)"
}
#endregion

Write-Host ""
Write-Host "World organization complete."
Write-Host "Population, cave folders and biome-specific assets remain in place."
Write-Host "Reopen Godot, build, and test the debug biome launcher."