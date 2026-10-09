# Restore damaged biome resources and update their moved script paths.
# Run from the project root with Godot closed.
$ErrorActionPreference = "Stop"

if (!(Test-Path "project.godot")) {
    throw "Run this script from your Godot project root."
}

$sourceCommit = "f89d107c16140760b8f639409a7a7bf0585f407d"
$backupFolder = Join-Path $env:TEMP (
    "IsometricBiomeBackup-" + (Get-Date -Format "yyyyMMdd-HHmmss")
)

$files = @(
    "WORLD/Generation/Biomes/Definitions/BasaltFlats/BasaltFlats.tres"
    "WORLD/Generation/Biomes/Definitions/CarbonForest/CarbonForest.tres"
    "WORLD/Generation/Biomes/Definitions/RollingHills/RollingHills.tres"
    "WORLD/Generation/Biomes/Definitions/RockyMountains/RockyMountains.tres"
    "WORLD/Generation/Biomes/Definitions/VerdigrisWilds/VerdigrisWilds.tres"
    "WORLD/Generation/Biomes/Definitions/SwampyMarsh/SwampyMarsh.tres"
    "WORLD/Sandbox/Resources/SandboxBiomes.tres"
)

$pathUpdates = [ordered]@{
    "WORLD/Generation/Biomes/BiomeVegetation.cs" =
        "WORLD/Generation/Biomes/Content/BiomeVegetation.cs"
    "WORLD/Generation/Biomes/BiomeSpecies.cs" =
        "WORLD/Generation/Biomes/Content/BiomeSpecies.cs"
    "WORLD/Generation/Biomes/BiomeContent.cs" =
        "WORLD/Generation/Biomes/Content/BiomeContent.cs"
    "WORLD/Generation/Biomes/BiomeGroundProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeGroundProfile.cs"
    "WORLD/Generation/Biomes/BiomeColourProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeColourProfile.cs"
    "WORLD/Generation/Biomes/BiomeBasinProfile.cs" =
        "WORLD/Generation/Biomes/Profiles/BiomeBasinProfile.cs"
    "WORLD/Generation/Biomes/BiomePlacementSettings.cs" =
        "WORLD/Generation/Biomes/Placement/BiomePlacementSettings.cs"
    "WORLD/Generation/Biomes/BiomeScatter.cs" =
        "WORLD/Generation/Biomes/Placement/BiomeScatter.cs"
}

# Download and validate everything before replacing any project files.
$prepared = @{}

foreach ($file in $files) {
    $url = "https://raw.githubusercontent.com/ben0kay/isometric-new-2d/$sourceCommit/$file"
    $text = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content

    if ([string]::IsNullOrWhiteSpace($text) -or
        !$text.StartsWith("[gd_resource") -or
        $text.Contains('[sub_resource type="CSharpScript"')) {
        throw "The downloaded resource is not valid: $file"
    }

    foreach ($entry in $pathUpdates.GetEnumerator()) {
        $text = $text.Replace(
            "res://$($entry.Key)",
            "res://$($entry.Value)"
        )
    }

    foreach ($match in [regex]::Matches(
        $text, 'path="res://([^"]+)"'
    )) {
        $referencedFile = $match.Groups[1].Value

        if (!(Test-Path -LiteralPath $referencedFile)) {
            throw "Missing reference in ${file}: $referencedFile"
        }
    }

    $prepared[$file] = $text
}

# Back up all current resources before writing replacements.
foreach ($file in $files) {
    $backupPath = Join-Path $backupFolder $file
    $backupParent = Split-Path $backupPath -Parent

    New-Item -ItemType Directory -Path $backupParent -Force |
        Out-Null

    Copy-Item -LiteralPath $file -Destination $backupPath
}

$utf8 = New-Object System.Text.UTF8Encoding($false)

foreach ($file in $files) {
    [System.IO.File]::WriteAllText(
        (Join-Path (Get-Location).Path $file),
        $prepared[$file],
        $utf8
    )

    Write-Host "Repaired: $file"
}

Write-Host ""
Write-Host "Backup: $backupFolder"
Write-Host "Reopen Godot, build C#, then test the biome launcher."