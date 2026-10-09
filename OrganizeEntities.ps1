# Organizes existing entity files and updates their paths.
# Preserves script UIDs and leaves population files in Core.
$ErrorActionPreference = "Stop"

if (!(Test-Path "project.godot")) {
    throw "Run this script from your Godot project root."
}

#region File Moves
$moves = [ordered]@{
    "ENTITIES/Core/Behaviours/EntityBehaviorSettings.cs" =
        "ENTITIES/Core/EntityBehaviorSettings.cs"

    "ENTITIES/Core/EntityMotor.cs" =
        "ENTITIES/Movement/EntityMotor.cs"
    "ENTITIES/Core/EntityWandering.cs" =
        "ENTITIES/Movement/EntityWandering.cs"

    "ENTITIES/Core/Behaviours/EntityCombat.cs" =
        "ENTITIES/Combat/EntityCombat.cs"
    "ENTITIES/Core/Behaviours/EntityCombatMovement.cs" =
        "ENTITIES/Combat/EntityCombatMovement.cs"
    "ENTITIES/Core/Behaviours/EntityTargeting.cs" =
        "ENTITIES/Combat/EntityTargeting.cs"
    "ENTITIES/Core/EntityCombatController.cs" =
        "ENTITIES/Combat/EntityCombatController.cs"
    "ENTITIES/Core/EntityCombatSettings.cs" =
        "ENTITIES/Combat/EntityCombatSettings.cs"
    "ENTITIES/Core/MeleeCombatSettings.cs" =
        "ENTITIES/Combat/MeleeCombatSettings.cs"
    "ENTITIES/Core/RangedCombatSettings.cs" =
        "ENTITIES/Combat/RangedCombatSettings.cs"
    "ENTITIES/Core/EntityThreatResponseComponent.cs" =
        "ENTITIES/Combat/EntityThreatResponseComponent.cs"

    "ENTITIES/Core/EntityGroup.cs" =
        "ENTITIES/Groups/EntityGroup.cs"
    "ENTITIES/Core/EntityGroupMember.cs" =
        "ENTITIES/Groups/EntityGroupMember.cs"
    "ENTITIES/Core/GroupRoaming.cs" =
        "ENTITIES/Groups/GroupRoaming.cs"

    "ENTITIES/Core/EntityGrazing.cs" =
        "ENTITIES/Grazing/EntityGrazing.cs"
    "ENTITIES/Core/GrazingWorld.cs" =
        "ENTITIES/Grazing/GrazingWorld.cs"

    "ENTITIES/Core/EntityPresentation.cs" =
        "ENTITIES/Presentation/EntityPresentation.cs"

    "ENTITIES/Core/EntityDeathLoot.cs" =
        "ENTITIES/Death/EntityDeathLoot.cs"

    "ENTITIES/Core/Sequences/EntityAction.cs" =
        "ENTITIES/Sequences/EntityAction.cs"
    "ENTITIES/Core/Sequences/EntitySequence.cs" =
        "ENTITIES/Sequences/EntitySequence.cs"
    "ENTITIES/Core/Sequences/EntitySequenceDefinition.cs" =
        "ENTITIES/Sequences/EntitySequenceDefinition.cs"
    "ENTITIES/Core/Sequences/FireEntityAction.cs" =
        "ENTITIES/Sequences/FireEntityAction.cs"
    "ENTITIES/Core/Sequences/DodgeEntityAction.cs" =
        "ENTITIES/Sequences/DodgeEntityAction.cs"
    "ENTITIES/Core/Sequences/WaitEntityAction.cs" =
        "ENTITIES/Sequences/WaitEntityAction.cs"
    "ENTITIES/Core/Sequences/ShootDodgeShoot.tres" =
        "ENTITIES/Sequences/ShootDodgeShoot.tres"
}
#endregion

#region Preflight
# =========================================================
# Validate every move before changing project files.
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
# Prepare exact path replacements without changing class names or UIDs.
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
# Apply prepared reference changes while the editor is closed.
foreach ($update in $updates) {
    [System.IO.File]::WriteAllText(
        $update.Path, $update.Text, $encoding)

    Write-Host "Updated references: $($update.Path)"
}
#endregion

#region Move Files
# =========================================================
# Move each file and its existing script UID together.
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

# =========================================================
# Remove the two old folders only when completely empty.
foreach ($directory in @(
    "ENTITIES/Core/Behaviours",
    "ENTITIES/Core/Sequences"
)) {
    if (Test-Path -LiteralPath $directory) {
        $remaining = @(
            Get-ChildItem -LiteralPath $directory -Force
        )

        if ($remaining.Count -eq 0) {
            Remove-Item -LiteralPath $directory
        }
    }
}
#endregion

Write-Host ""
Write-Host "Entity organization complete."
Write-Host "Population remains in Core. Sequences are independent."
Write-Host "Reopen Godot, build, and test."