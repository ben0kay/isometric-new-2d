# Read-only checks for Godot resource files and sprite artwork.
# Reports setup problems; does not compile scripts or run Godot.
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = "Godot Resource Checker"
$form.ClientSize = New-Object System.Drawing.Size(960, 600)
$form.StartPosition = "CenterScreen"
$form.MinimumSize = $form.Size

$folder = New-Object System.Windows.Forms.TextBox
$folder.Location = New-Object System.Drawing.Point(15, 20)
$folder.Size = New-Object System.Drawing.Size(700, 25)
$folder.Anchor = "Top, Left, Right"
$form.Controls.Add($folder)

# The toolbox currently lives directly inside the Godot project.
$candidate = [IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "..\..\.."))
if (Test-Path -LiteralPath (Join-Path $candidate "project.godot")) {
    $folder.Text = $candidate
}

$browse = New-Object System.Windows.Forms.Button
$browse.Text = "Browse"
$browse.Location = New-Object System.Drawing.Point(725, 18)
$browse.Size = New-Object System.Drawing.Size(95, 28)
$browse.Anchor = "Top, Right"
$form.Controls.Add($browse)

$scan = New-Object System.Windows.Forms.Button
$scan.Text = "Check Project"
$scan.Location = New-Object System.Drawing.Point(830, 18)
$scan.Size = New-Object System.Drawing.Size(115, 28)
$scan.Anchor = "Top, Right"
$form.Controls.Add($scan)

$help = New-Object System.Windows.Forms.Label
$help.Text = "Select the folder containing project.godot. Checks are read-only."
$help.Location = New-Object System.Drawing.Point(15, 57)
$help.Size = New-Object System.Drawing.Size(920, 25)
$form.Controls.Add($help)

$results = New-Object System.Windows.Forms.ListView
$results.Location = New-Object System.Drawing.Point(15, 90)
$results.Size = New-Object System.Drawing.Size(930, 450)
$results.Anchor = "Top, Bottom, Left, Right"
$results.View = "Details"
$results.FullRowSelect = $true
$results.GridLines = $true
[void]$results.Columns.Add("Level", 75)
[void]$results.Columns.Add("File", 340)
[void]$results.Columns.Add("Finding", 490)
$form.Controls.Add($results)

$status = New-Object System.Windows.Forms.Label
$status.Text = "Ready. Double-click a finding to open its containing folder."
$status.Location = New-Object System.Drawing.Point(15, 555)
$status.Size = New-Object System.Drawing.Size(930, 30)
$status.Anchor = "Bottom, Left, Right"
$form.Controls.Add($status)
#endregion

#region Reporting
# =========================================================
# Record a finding without changing the inspected file.
function Add-Finding($level, $file, $message) {
    [void]$script:Findings.Add([PSCustomObject]@{
        Level = $level
        File = $file
        Message = $message
    })
}

# =========================================================
# Display findings and keep their full paths for folder navigation.
function Show-Findings($root) {
    $results.BeginUpdate()
    try {
        $results.Items.Clear()

        foreach ($finding in $script:Findings) {
            $relative = $finding.File.Substring($root.Length).TrimStart('\')
            $row = New-Object System.Windows.Forms.ListViewItem($finding.Level)
            [void]$row.SubItems.Add($relative)
            [void]$row.SubItems.Add($finding.Message)
            $row.Tag = $finding.File

            switch ($finding.Level) {
                "Error" { $row.ForeColor = [System.Drawing.Color]::Firebrick }
                "Warning" { $row.ForeColor = [System.Drawing.Color]::DarkOrange }
                "Notice" { $row.ForeColor = [System.Drawing.Color]::SteelBlue }
            }

            [void]$results.Items.Add($row)
        }
    }
    finally { $results.EndUpdate() }
}
#endregion

#region Resource Checks
# =========================================================
# Check text resource paths, reference declarations and artwork folders.
function Check-Resource($file, $root, $species) {
    $text = [IO.File]::ReadAllText($file.FullName)

    # Check explicit res:// paths, including ImageFolder.
    $paths = [regex]::Matches(
        $text, '(?m)^\s*\w*[Pp]ath\s*=\s*"(?<path>res://[^"]+)"|' +
        '\bpath\s*=\s*"(?<path>res://[^"]+)"|' +
        '(?m)^\s*ImageFolder\s*=\s*"(?<path>res://[^"]+)"')

    $checked = @{}
    foreach ($match in $paths) {
        $resourcePath = $match.Groups["path"].Value
        if ($checked.ContainsKey($resourcePath)) { continue }
        $checked[$resourcePath] = $true

        $local = Join-Path $root ($resourcePath.Substring(6).Replace('/', '\'))
        if (!(Test-Path -LiteralPath $local)) {
            Add-Finding "Error" $file.FullName "Missing path: $resourcePath"
        }
    }

    # Resource calls must refer to declarations within this same file.
    $external = @{}
    $internal = @{}

    foreach ($match in [regex]::Matches(
        $text, '(?m)^\[ext_resource\b[^\r\n]*\bid="([^"]+)"[^\r\n]*\]')) {
        $id = $match.Groups[1].Value
        if ($external.ContainsKey($id)) {
            Add-Finding "Error" $file.FullName "Duplicate external reference ID: $id"
        }
        $external[$id] = $true
    }

    foreach ($match in [regex]::Matches(
        $text, '(?m)^\[sub_resource\b[^\r\n]*\bid="([^"]+)"[^\r\n]*\]')) {
        $id = $match.Groups[1].Value
        if ($internal.ContainsKey($id)) {
            Add-Finding "Error" $file.FullName "Duplicate subresource ID: $id"
        }
        $internal[$id] = $true
    }

    foreach ($match in [regex]::Matches(
        $text, '\b(ExtResource|SubResource)\("([^"]+)"\)')) {
        $kind = $match.Groups[1].Value
        $id = $match.Groups[2].Value
        $declared = if ($kind -eq "ExtResource") { $external } else { $internal }

        if (!$declared.ContainsKey($id)) {
            Add-Finding "Error" $file.FullName "$kind refers to undeclared ID: $id"
        }
    }

    # Only check PNG folders explicitly assigned to ImageFolder.
    foreach ($match in [regex]::Matches(
        $text, '(?m)^\s*ImageFolder\s*=\s*"([^"]*)"')) {
        $resourcePath = $match.Groups[1].Value
        if ([string]::IsNullOrWhiteSpace($resourcePath)) { continue }

        if (!$resourcePath.StartsWith("res://")) {
            Add-Finding "Warning" $file.FullName `
                "ImageFolder is not a res:// project path: $resourcePath"
            continue
        }

        $local = Join-Path $root ($resourcePath.Substring(6).Replace('/', '\'))
        if (Test-Path -LiteralPath $local -PathType Container) {
            $pngs = @(Get-ChildItem -LiteralPath $local -File |
                Where-Object { $_.Extension -ieq ".png" })

            if ($pngs.Count -eq 0) {
                Add-Finding "Warning" $file.FullName `
                    "ImageFolder contains no PNGs: $resourcePath"
            }
        }
        elseif (Test-Path -LiteralPath $local) {
            Add-Finding "Error" $file.FullName "ImageFolder points to a file."
        }
    }

    # A missing Visual can be intentional, so this is a notice.
    if ($text -match 'script_class="PlantDefinition"' -and
        $text -notmatch '(?m)^\s*Visual\s*=\s*(ExtResource|SubResource)\(') {
        Add-Finding "Notice" $file.FullName `
            "Plant has no assigned Visual; baked fallback artwork will be used."
    }

    # Check species IDs within each definition class.
    $classMatch = [regex]::Match($text, 'script_class="([^"]+)"')
    $idMatch = [regex]::Match($text, '(?m)^\s*Id\s*=\s*"([^"]*)"')

    $class = $classMatch.Groups[1].Value
    if ($class -match '^(Plant|Tree|Grass|Rock|Entity)Definition$') {
        if (!$idMatch.Success -or
            [string]::IsNullOrWhiteSpace($idMatch.Groups[1].Value)) {
            Add-Finding "Warning" $file.FullName `
                "Species has no explicit non-empty Id; check its default."
        }
        else {
            $id = $idMatch.Groups[1].Value
            $key = "$class|$id"

            if ($species.ContainsKey($key)) {
                Add-Finding "Warning" $file.FullName `
                    "Duplicate species Id '$id' also appears in: $($species[$key])"
            }
            else {
                $species[$key] = $file.FullName.Substring($root.Length).TrimStart('\')
            }
        }
    }
}

# =========================================================
# Confirm a PNG can be decoded; release its file handle immediately.
function Check-Png($file) {
    $image = $null
    try {
        $image = [System.Drawing.Image]::FromFile($file.FullName)
        if ($image.Width -le 0 -or $image.Height -le 0) {
            throw "Invalid image dimensions."
        }
    }
    catch {
        Add-Finding "Error" $file.FullName `
            ("PNG cannot be read: " + $_.Exception.Message)
    }
    finally {
        if ($image) { $image.Dispose() }
    }
}
#endregion

#region Project Scan
# =========================================================
# Inspect project assets while skipping caches, build outputs and the toolbox.
function Check-Project($root) {
    $script:Findings = New-Object 'System.Collections.Generic.List[object]'
    $species = @{}

    $files = @(Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object {
            $_.FullName.Substring($root.Length) -notmatch
                '(?i)[\\/](\.godot|\.git|\.vs|bin|obj|BEN_TOOLBOX|RAW)[\\/]' -and
            $_.Extension -in ".tres", ".tscn", ".png"
        })

    $index = 0
    foreach ($file in $files) {
        $index++
        $status.Text = "Checking $index of $($files.Count): $($file.Name)"

        # Repaint progress without allowing another scan to start.
        if ($index % 20 -eq 0) { $form.Refresh() }

        try {
            if ($file.Extension -ieq ".png") {
                Check-Png $file
            }
            else {
                Check-Resource $file $root $species
            }
        }
        catch {
            Add-Finding "Error" $file.FullName `
                ("Unable to inspect file: " + $_.Exception.Message)
        }
    }

    Show-Findings $root

    $errors = @($script:Findings | Where-Object Level -eq "Error").Count
    $warnings = @($script:Findings | Where-Object Level -eq "Warning").Count
    $notices = @($script:Findings | Where-Object Level -eq "Notice").Count

    $status.Text = "Checked $($files.Count) files: " +
        "$errors errors, $warnings warnings, $notices notices."
}
#endregion

#region Events
# =========================================================
# Select the Godot project directory.
$browse.Add_Click({
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        if ($picker.ShowDialog() -eq "OK") {
            $folder.Text = $picker.SelectedPath
        }
    }
    finally { $picker.Dispose() }
})

# =========================================================
# Run a read-only scan and report unexpected failures.
$scan.Add_Click({
    $scan.Enabled = $false
    $browse.Enabled = $false
    $folder.Enabled = $false
    $form.UseWaitCursor = $true

    try {
        if (!(Test-Path -LiteralPath $folder.Text -PathType Container)) {
            throw "Select an existing project folder."
        }

        $root = (Resolve-Path -LiteralPath $folder.Text).ProviderPath.TrimEnd('\')
        if (!(Test-Path -LiteralPath (Join-Path $root "project.godot"))) {
            throw "Select the folder containing project.godot."
        }

        $results.Items.Clear()
        Check-Project $root
    }
    catch {
        $status.Text = "Scan stopped."
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Resource Checker")
    }
    finally {
        $scan.Enabled = $true
        $browse.Enabled = $true
        $folder.Enabled = $true
        $form.UseWaitCursor = $false
    }
})

# =========================================================
# Open the containing folder of a selected finding.
$results.Add_DoubleClick({
    if ($results.SelectedItems.Count -eq 0) { return }

    try {
        $path = $results.SelectedItems[0].Tag
        $directory = Split-Path -Parent $path
        Start-Process -FilePath "explorer.exe" `
            -ArgumentList ('"' + $directory + '"')
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Unable to open folder")
    }
})
#endregion

[void]$form.ShowDialog()
$form.Dispose()