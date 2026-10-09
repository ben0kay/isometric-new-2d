# Applies the mechanical references for the combined entity migration.
# Run from the project root before pasting the replacement C# files.
$ErrorActionPreference = "Stop"

if (!(Test-Path "project.godot")) {
    throw "Run this script from the Godot project root."
}

$renames = [ordered]@{
    "ENTITIES/Core/EnemyMotor.cs" = "ENTITIES/Core/EntityMotor.cs"
    "ENTITIES/Core/EnemyCombatSettings.cs" = "ENTITIES/Core/EntityCombatSettings.cs"
    "ENTITIES/Core/EnemyCombat.cs" = "ENTITIES/Core/EntityCombatController.cs"
    "ENTITIES/Core/EnemyPresentation.cs" = "ENTITIES/Core/EntityPresentation.cs"
}

foreach ($pair in $renames.GetEnumerator()) {
    if (!(Test-Path $pair.Key)) {
        throw "Missing source file: $($pair.Key)"
    }

    if (Test-Path $pair.Value) {
        throw "Destination already exists: $($pair.Value)"
    }
}

foreach ($pair in $renames.GetEnumerator()) {
    Move-Item -LiteralPath $pair.Key -Destination $pair.Value

    if (Test-Path "$($pair.Key).uid") {
        Move-Item -LiteralPath "$($pair.Key).uid" `
            -Destination "$($pair.Value).uid"
    }
}

$typeChanges = [ordered]@{
    "EnemyMotor" = "EntityMotor"
    "EnemyCombatSettings" = "EntityCombatSettings"
    "EnemyCombat" = "EntityCombatController"
    "EnemyPresentation" = "EntityPresentation"
    "EnemyDefinition" = "EntityDefinition"
    "MaxVitality" = "MaxHealth"
    "ThreatSpeed" = "MoveSpeed"
    "TickMotor" = "ReturnBeforeWaiting"
}

# These files contain old actor type references.
# CombatTeam.Enemy remains unchanged.
$actorConsumers = @(
    "ENTITIES/Core/EnemyPopulation.cs",
    "ENTITIES/Core/EntityCombatController.cs",
    "ENTITIES/Core/EntityPresentation.cs",
    "COMBAT/Hitboxes/CombatHitbox.cs",
    "DEBUG/Overlays/EnemyRangeDebug.cs",
    "DEBUG/Overlays/WorldEnemyRangesDebug.cs",
    "DEBUG/Overlays/WorldCollisionDebug.cs",
    "WORLD/Layers/CaveEnemyPursuit.cs",
    "WORLD/Layers/WorldLayerController.cs"
)

$root = (Get-Location).Path
$encoding = New-Object System.Text.UTF8Encoding($false)

$files = Get-ChildItem -Recurse -File | Where-Object {
    $_.Extension -in ".cs", ".tscn", ".tres" -and
    $_.FullName -notmatch '[\\/](\.git|\.godot|\.vs)[\\/]'
}

foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length + 1).Replace("\", "/")
    $text = [System.IO.File]::ReadAllText($file.FullName)
    $original = $text

    foreach ($pair in $typeChanges.GetEnumerator()) {
        $pattern = "\b" + [regex]::Escape($pair.Key) + "\b"
        $text = [regex]::Replace($text, $pattern, $pair.Value)
    }

    if ($relative -in $actorConsumers) {
        $text = [regex]::Replace(
            $text, '(?<!CombatTeam\.)\bEnemy\b', 'Entity')
    }

    $text = $text.Replace(
        "res://ENTITIES/Core/Enemy.tscn",
        "res://ENTITIES/Core/Entity.tscn")

    $text = $text.Replace(
        "res://ENTITIES/Core/Enemy.cs",
        "res://ENTITIES/Core/Entity.cs")

    # References must use the existing shared scripts' UIDs.
    $text = $text.Replace(
        "uid://vgi4tpvjvb5s", "uid://cv62vuncf01ti")

    $text = $text.Replace(
        "uid://dl04ovcirdmql", "uid://b88qfb65xbv3q")

    if ($relative -eq "ENTITIES/Core/EntityThreatResponseComponent.cs") {
        $text = $text.Replace(
            "private Node2D Target =>",
            "public Node2D Target =>")
    }

    if ($text -ne $original) {
        [System.IO.File]::WriteAllText(
            $file.FullName, $text, $encoding)

        Write-Host "Updated $relative"
    }
}

Write-Host ""
Write-Host "References updated. Complete the C# replacements before building."