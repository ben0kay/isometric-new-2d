# Read-only lighting and shadow inspector.
# Windows PowerShell 5.1. Launch with -STA.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

# Find project.godot by walking upward from this script.
$script:ProjectRoot = $PSScriptRoot
while ($script:ProjectRoot -and
       !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot "project.godot"))) {
    $script:ProjectRoot = Split-Path -Parent $script:ProjectRoot
}

$script:Rows = New-Object System.Collections.Generic.List[object]

function Add-Setting($kind, $file, $section, $name, $value, $line) {
    $relative = $file.Substring($script:ProjectRoot.Length).TrimStart('\', '/')

    $script:Rows.Add([PSCustomObject]@{
        Kind = $kind
        Setting = $name
        Value = $value
        Section = $section
        File = $relative
        Line = $line
        FullPath = $file
    })
}

function Read-Settings {
    $script:Rows.Clear()

    # Exported properties in the shared configuration classes.
    $configPaths = @(
        "VISUALS/Lighting/WorldLightingSettings.cs"
        "VISUALS/Lighting/VisualLightingSettings.cs"
        "VISUALS/Shadows/GroundShadowSettings.cs"
    )

    $settingNames = @{}

    foreach ($relative in $configPaths) {
        $path = Join-Path $script:ProjectRoot $relative
        if (!(Test-Path -LiteralPath $path)) { continue }

        $lines = [IO.File]::ReadAllLines($path)
        $group = "Configuration"
        $exportPending = $false

        for ($i = 0; $i -lt $lines.Length; $i++) {
            $text = $lines[$i].Trim()

            if ($text -match '\[ExportGroup\("([^"]+)"\)\]') {
                $group = $Matches[1]
                $exportPending = $false
            }

            if ($text -match '\[Export(?:\(|\])') {
                $exportPending = $true
            }

            if ($exportPending -and
                $text -match 'public\s+(\w+)\s+(\w+)\s*\{\s*get;\s*set;\s*\}\s*(?:=\s*(.+);)?') {

                $type = $Matches[1]
                $name = $Matches[2]
                $value = $Matches[3]

                if ([string]::IsNullOrWhiteSpace($value)) {
                    $value = "Implicit default ($type)"
                }

                $settingNames[$name] = $true
                Add-Setting "C# default" $path $group $name $value ($i + 1)
                $exportPending = $false
            }
        }
    }

    # Scan source folders, skipping generated and toolbox content.
    $excluded = @(".git", ".godot", ".vs", "bin", "obj", "BEN_TOOLBOX")

    function Get-ProjectFiles([string]$folder) {
        foreach ($item in Get-ChildItem -LiteralPath $folder -Force) {
            if ($item.PSIsContainer) {
                if ($excluded -notcontains $item.Name) {
                    Get-ProjectFiles $item.FullName
                }
            }
            elseif ($item.Extension -in @(
                ".tres", ".tscn", ".gdshader", ".gdshaderinc"
            ) -or $item.Name -eq "project.godot") {
                $item
            }
        }
    }

    $files = @(Get-ProjectFiles $script:ProjectRoot)

    foreach ($file in $files) {
        $lines = [IO.File]::ReadAllLines($file.FullName)

        # Shader declarations, including shared world-lighting globals.
        if ($file.Extension -in @(".gdshader", ".gdshaderinc")) {
            $relevantShader = $file.FullName -match '(?i)(Lighting|Shadow|GroundSun)'
            $section = "Shader"

            for ($i = 0; $i -lt $lines.Length; $i++) {
                $text = $lines[$i].Trim()

                if ($text -match '^(global\s+)?uniform\s+\w+\s+(\w+)(.*);') {
                    $isGlobal = ![string]::IsNullOrWhiteSpace($Matches[1])
                    $name = $Matches[2]
                    $tail = $Matches[3]

                    if (!$relevantShader -and
                        $name -notmatch '(?i)(^wl_|sun|shadow|ambient|light)') {
                        continue
                    }

                    $value = "No declaration default"
                    if ($tail -match '=\s*(.+)$') {
                        $value = $Matches[1].Trim()
                    }

                    $kind = if ($isGlobal) {
                        "Shader global declaration"
                    } else {
                        "Shader default"
                    }

                    Add-Setting $kind $file.FullName $section $name $value ($i + 1)
                }
            }

            continue
        }

        # Resolve ExtResource IDs to their saved paths for readable links.
        $resources = @{}

        foreach ($text in $lines) {
            if ($text -match '^\[ext_resource\b') {
                $resourcePath = $null
                $resourceId = $null

                if ($text -match '\bpath="([^"]+)"') {
                    $resourcePath = $Matches[1]
                }
                if ($text -match '\bid="([^"]+)"') {
                    $resourceId = $Matches[1]
                }

                if ($resourcePath -and $resourceId) {
                    $resources[$resourceId] = $resourcePath
                }
            }
        }

        # Process complete sections so script references can identify
        # lighting and shadow configuration blocks.
        $blocks = New-Object System.Collections.Generic.List[object]
        $header = ""
        $entries = New-Object System.Collections.Generic.List[object]

        for ($i = 0; $i -lt $lines.Length; $i++) {
            $text = $lines[$i].Trim()

            if ($text.StartsWith("[")) {
                if ($header) {
                    $blocks.Add([PSCustomObject]@{
                        Header = $header
                        Entries = @($entries.ToArray())
                    })
                }

                $header = $text
                $entries = New-Object System.Collections.Generic.List[object]
            }
            elseif ($text -match '^([^=]+?)\s*=\s*(.*)$') {
                $entries.Add([PSCustomObject]@{
                    Name = $Matches[1].Trim()
                    Value = $Matches[2].Trim()
                    Line = $i + 1
                })
            }
        }

        if ($header) {
            $blocks.Add([PSCustomObject]@{
                Header = $header
                Entries = @($entries.ToArray())
            })
        }

        foreach ($block in $blocks) {
            if ($block.Header -match '^\[ext_resource\b') {
                continue
            }

            $context = $block.Header
            foreach ($entry in $block.Entries) {
                $context += " " + $entry.Value

                if ($entry.Value -match 'ExtResource\("([^"]+)"\)') {
                    $id = $Matches[1]
                    if ($resources.ContainsKey($id)) {
                        $context += " " + $resources[$id]
                    }
                }
            }

            $relevantBlock = $context -match '(?i)(Lighting|Shadow|GroundSun)'
            if ($file.FullName -match '(?i)(Lighting|Shadow)' -and
                $block.Header -eq "[resource]") {
                $relevantBlock = $true
            }

            foreach ($entry in $block.Entries) {
                $name = $entry.Name

                $namedSetting = $name -match '(?i)(sun|shadow|ambient|lighting|^wl_)'
                $configSetting = $relevantBlock -and (
                    $settingNames.ContainsKey($name) -or
                    $name -eq "Profile" -or
                    $name -eq "script" -or
                    $name.StartsWith("shader_parameter/")
                )

                $globalSetting = $file.Name -eq "project.godot" -and
                    $block.Header -eq "[shader_globals]" -and
                    $namedSetting

                if (!$namedSetting -and !$configSetting -and !$globalSetting) {
                    continue
                }

                $value = $entry.Value
                if ($value -match 'ExtResource\("([^"]+)"\)') {
                    $id = $Matches[1]
                    if ($resources.ContainsKey($id)) {
                        $value += " -> " + $resources[$id]
                    }
                }

                $kind = if ($globalSetting) {
                    "Project shader global"
                } elseif ($file.Extension -eq ".tscn") {
                    "Scene saved value"
                } else {
                    "Resource saved value"
                }

                Add-Setting $kind $file.FullName $block.Header `
                    $name $value $entry.Line
            }
        }
    }
}

$form = New-Object System.Windows.Forms.Form
$form.Text = "Lighting & Shadow Inspector - Read Only"
$form.Size = New-Object System.Drawing.Size(1250, 780)
$form.MinimumSize = New-Object System.Drawing.Size(850, 550)
$form.StartPosition = "CenterScreen"
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = "Fill"
$layout.ColumnCount = 1
$layout.RowCount = 4
[void]$layout.RowStyles.Add(
    (New-Object System.Windows.Forms.RowStyle("Absolute", 48)))
[void]$layout.RowStyles.Add(
    (New-Object System.Windows.Forms.RowStyle("Absolute", 55)))
[void]$layout.RowStyles.Add(
    (New-Object System.Windows.Forms.RowStyle("Percent", 100)))
[void]$layout.RowStyles.Add(
    (New-Object System.Windows.Forms.RowStyle("Absolute", 42)))
$form.Controls.Add($layout)

$toolbar = New-Object System.Windows.Forms.FlowLayoutPanel
$toolbar.Dock = "Fill"

$refresh = New-Object System.Windows.Forms.Button
$refresh.Text = "Refresh"
$refresh.AutoSize = $true
$toolbar.Controls.Add($refresh)

$choose = New-Object System.Windows.Forms.Button
$choose.Text = "Choose project"
$choose.AutoSize = $true
$toolbar.Controls.Add($choose)

$open = New-Object System.Windows.Forms.Button
$open.Text = "Open selected file"
$open.AutoSize = $true
$toolbar.Controls.Add($open)

$searchLabel = New-Object System.Windows.Forms.Label
$searchLabel.Text = "Search"
$searchLabel.AutoSize = $true
$searchLabel.Margin = New-Object System.Windows.Forms.Padding(12, 7, 0, 0)
$toolbar.Controls.Add($searchLabel)

$search = New-Object System.Windows.Forms.TextBox
$search.Width = 270
$toolbar.Controls.Add($search)
$layout.Controls.Add($toolbar, 0, 0)

$note = New-Object System.Windows.Forms.Label
$note.Dock = "Fill"
$note.Text = "Saved configuration only. C# defaults and resource/scene values are listed separately; these are not resolved live values. Scenes may use different profiles."
$layout.Controls.Add($note, 0, 1)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock = "Fill"
$grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.MultiSelect = $false
$grid.SelectionMode = "FullRowSelect"
$grid.RowHeadersVisible = $false
$grid.AutoGenerateColumns = $false
$grid.AutoSizeRowsMode = "AllCells"
$grid.BackgroundColor = [Drawing.Color]::White

foreach ($spec in @(
    @("Kind", 170),
    @("Setting", 170),
    @("Value", 240),
    @("Section", 220),
    @("File", 290),
    @("Line", 60)
)) {
    $column = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $column.Name = $spec[0]
    $column.HeaderText = $spec[0]
    $column.Width = $spec[1]
    $column.SortMode = "Automatic"
    [void]$grid.Columns.Add($column)
}

$layout.Controls.Add($grid, 0, 2)

$status = New-Object System.Windows.Forms.Label
$status.Dock = "Fill"
$status.Text = "Ready."
$layout.Controls.Add($status, 0, 3)

function Show-Rows {
    $grid.Rows.Clear()
    $query = $search.Text.Trim()
    $count = 0

    foreach ($entry in $script:Rows) {
        $haystack = "$($entry.Kind) $($entry.Setting) $($entry.Value) " +
                    "$($entry.Section) $($entry.File)"

        if ($query -and
            $haystack.IndexOf($query, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            continue
        }

        $index = $grid.Rows.Add([object[]]@(
            $entry.Kind,
            $entry.Setting,
            $entry.Value,
            $entry.Section,
            $entry.File,
            $entry.Line
        ))

        $grid.Rows[$index].Tag = $entry
        $count++
    }

    $status.Text = "$count of $($script:Rows.Count) settings | $script:ProjectRoot"
}

function Refresh-Settings {
    $refresh.Enabled = $false
    $form.UseWaitCursor = $true

    try {
        if (!$script:ProjectRoot -or
            !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot "project.godot"))) {
            throw "Choose the folder containing project.godot."
        }

        Read-Settings
        Show-Rows
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Inspector")
    }
    finally {
        $refresh.Enabled = $true
        $form.UseWaitCursor = $false
    }
}

function Open-Selected {
    if (!$grid.CurrentRow) { return }

    $entry = $grid.CurrentRow.Tag
    if (!$entry) { return }

    try {
        # Opens the file in its associated application.
        # The inspector itself never writes to it.
        Start-Process -FilePath $entry.FullPath
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Unable to open file")
    }
}

$refresh.Add_Click({ Refresh-Settings })
$search.Add_TextChanged({ Show-Rows })
$open.Add_Click({ Open-Selected })
$grid.Add_CellDoubleClick({ Open-Selected })

$choose.Add_Click({
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        if ($script:ProjectRoot) {
            $picker.SelectedPath = $script:ProjectRoot
        }

        if ($picker.ShowDialog() -eq "OK") {
            if (!(Test-Path -LiteralPath (
                Join-Path $picker.SelectedPath "project.godot"))) {
                throw "Select the folder containing project.godot."
            }

            $script:ProjectRoot = $picker.SelectedPath
            Refresh-Settings
        }
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Choose project")
    }
    finally {
        $picker.Dispose()
    }
})

$form.Add_Shown({
    if ($script:ProjectRoot) {
        Refresh-Settings
    } else {
        $status.Text = "Click Choose project and select your Godot project folder."
    }
})

try {
    [void]$form.ShowDialog()
}
finally {
    $form.Dispose()
}