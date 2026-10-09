# BEN TOOLBOX - Asset Usage Finder (read-only).
# Traces direct resource/script references and transitive references to a selected file.
# Includes PNG variants loaded by VisualDefinition.ImageFolder in .tres resources.
# Does not run Godot, modify assets, or claim to detect all dynamic references.
# Windows PowerShell 5.1, STA. Launch via the BEN TOOLBOX menu.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

#region Project and paths
# =========================================================
# Locate the Godot project automatically, including nested toolbox scripts.
$script:ProjectRoot = $PSScriptRoot
while ($script:ProjectRoot -and !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'project.godot') -PathType Leaf)) {
    $parent = Split-Path -Parent $script:ProjectRoot
    if (!$parent -or $parent -eq $script:ProjectRoot) { $script:ProjectRoot = $null; break }
    $script:ProjectRoot = $parent
}
$script:Busy = $false
$script:Incoming = @{}
$script:Results = @()
$script:Selected = $null
$script:Report = ''
$script:ResRegex = [regex]::new('(?:"(?<double>res://[^"]+)"|''(?<single>res://[^'']+)''|(?<bare>res://[^\s\)\],};]+))', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
$script:FileTypes = @('.tres', '.tscn', '.cs', '.gd', '.gdshader', '.gdshaderinc', '.godot', '.json', '.cfg', '.ini', '.gdextension', '.ps1')
$script:IgnoredFolders = @('.git', '.godot', '.vs', '.idea', 'bin', 'obj', '.save-pass-backups', 'node_modules')

# =========================================================
# Keep resolved resource paths inside the selected project.
function Convert-ResPath([string]$path) {
    if (!$script:ProjectRoot -or !$path.StartsWith('res://', [StringComparison]::OrdinalIgnoreCase)) { return $null }
    try {
        $relative = $path.Substring(6).Replace('/', '\')
        $resolved = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $relative))
        $root = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (!$resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $null }
        return $resolved
    }
    catch { return $null }
}

# =========================================================
# Convert file selection from absolute, res://, or project-relative form.
function Resolve-AssetPath([string]$inputPath) {
    if (!$script:ProjectRoot) { throw 'Choose a Godot project first.' }
    $inputPath = $inputPath.Trim().Trim('"', "'")
    if (!$inputPath) { throw 'Choose an asset to inspect.' }
    if ($inputPath.StartsWith('res://', [StringComparison]::OrdinalIgnoreCase)) {
        $full = Convert-ResPath $inputPath
    }
    elseif ([IO.Path]::IsPathRooted($inputPath)) {
        $full = [IO.Path]::GetFullPath($inputPath)
    }
    else {
        $full = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $inputPath))
    }
    if (!$full) { throw 'This resource path is outside the project.' }
    $root = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Select a file inside your Godot project.'
    }
    if (!(Test-Path -LiteralPath $full -PathType Leaf)) { throw "File does not exist: $full" }
    return $full
}

# =========================================================
# Produce a readable project-relative path, including Windows separators.
function Get-Relative([string]$path) {
    $root = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $path.Substring($root.Length).Replace('\', '/')
    }
    return $path
}

# =========================================================
# Read the saved Godot UID when available, including script UID sidecars.
function Get-AssetUid([string]$path) {
    if ($path -match '\.(tres|tscn)$') {
        foreach ($line in @(Get-Content -LiteralPath $path -TotalCount 5 -ErrorAction Stop)) {
            if ($line -match '^\[gd_(resource|scene)\b.*\buid="(uid://[^"]+)"') { return $Matches[2] }
        }
    }
    $sidecar = $path + '.uid'
    if (Test-Path -LiteralPath $sidecar -PathType Leaf) {
        $value = ([IO.File]::ReadAllText($sidecar)).Trim()
        if ($value -match '^uid://[A-Za-z0-9_]+$') { return $value }
    }
    return ''
}
#endregion

#region Reference indexing
# =========================================================
# Record a dependency in the reverse index: source file -> referenced file.
function Add-Edge([string]$source, [string]$target, [int]$line, [string]$kind) {
    if (!$target -or $source.Equals($target, [StringComparison]::OrdinalIgnoreCase)) { return }
    if (![IO.File]::Exists($target)) { return }
    if (!$script:Incoming.ContainsKey($target)) {
        $script:Incoming[$target] = New-Object 'System.Collections.Generic.List[object]'
    }
    $script:Incoming[$target].Add([PSCustomObject]@{
        Source = $source; Target = $target; Line = $line; Kind = $kind
    })
}

# =========================================================
# Enumerate editable text sources without generated caches or junction loops.
function Get-SourceFiles {
    $folders = New-Object 'System.Collections.Generic.Stack[string]'
    $folders.Push($script:ProjectRoot)
    while ($folders.Count -gt 0) {
        $folder = $folders.Pop()
        try { $items = @(Get-ChildItem -LiteralPath $folder -Force -ErrorAction Stop) }
        catch { continue }
        foreach ($item in $items) {
            if ($item.PSIsContainer) {
                if ($script:IgnoredFolders -contains $item.Name) { continue }
                if (!$includeToolbox.Checked -and $item.Name -eq 'BEN_TOOLBOX') { continue }
                if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                $folders.Push($item.FullName)
            }
            elseif ($script:FileTypes -contains $item.Extension.ToLowerInvariant() -or $item.Name -eq 'project.godot') {
                $item.FullName
            }
        }
    }
}

# =========================================================
# Scan explicit res:// and uid:// references plus the image-folder convention.
function Index-References([string]$asset, [string]$uid) {
    $script:Incoming = @{}
    $isPng = [IO.Path]::GetExtension($asset).Equals('.png', [StringComparison]::OrdinalIgnoreCase)
    $assetFolder = [IO.Path]::GetDirectoryName($asset)
    $scanned = 0
    $unreadable = 0

    foreach ($source in @(Get-SourceFiles)) {
        $scanned++
        if (($scanned % 30) -eq 0) {
            $status.Text = "Scanning $scanned text files: $(Get-Relative $source)"
            [Windows.Forms.Application]::DoEvents()
        }

        try { $lines = [IO.File]::ReadAllLines($source) }
        catch { $unreadable++; continue }

        for ($i = 0; $i -lt $lines.Length; $i++) {
            $line = $lines[$i]
            $lineNumber = $i + 1
            $referencedTargets = @{}

            foreach ($match in $script:ResRegex.Matches($line)) {
                $value = if ($match.Groups['double'].Success) { $match.Groups['double'].Value }
                         elseif ($match.Groups['single'].Success) { $match.Groups['single'].Value }
                         else { $match.Groups['bare'].Value }
                $target = Convert-ResPath $value
                if (!$target -or $referencedTargets.ContainsKey($target)) { continue }
                $referencedTargets[$target] = $true
                Add-Edge $source $target $lineNumber 'res:// reference'
            }

            # A VisualDefinition loads every immediate PNG in its ImageFolder,
            # even though those PNG names never occur in the .tres text.
            if ($isPng -and $line -match '^\s*ImageFolder\s*=\s*["''](?<folder>res://[^"'']+)["'']') {
                $folderPath = Convert-ResPath $Matches['folder']
                if ($folderPath -and $folderPath.TrimEnd('\', '/').Equals(
                    $assetFolder.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Edge $source $asset $lineNumber 'ImageFolder PNG variant'
                }
            }

            if ($uid -and $line.IndexOf($uid, [StringComparison]::OrdinalIgnoreCase) -ge 0 -and
                !$referencedTargets.ContainsKey($asset)) {
                Add-Edge $source $asset $lineNumber 'uid:// reference'
            }
        }
    }
    return [PSCustomObject]@{ Scanned = $scanned; Unreadable = $unreadable }
}

# =========================================================
# Traverse incoming resource links without revisiting files or looping forever.
function Find-Usage([string]$asset) {
    $script:Results = @()
    $visited = @{}
    $visited[$asset] = $true
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue([PSCustomObject]@{ File = $asset; Depth = 0; Chain = @((Get-Relative $asset)) })
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if (!$script:Incoming.ContainsKey($node.File)) { continue }

        foreach ($edge in $script:Incoming[$node.File]) {
            if ($visited.ContainsKey($edge.Source)) { continue }
            $visited[$edge.Source] = $true
            $depth = $node.Depth + 1
            $chain = @((Get-Relative $edge.Source)) + @($node.Chain)
            $script:Results += [PSCustomObject]@{
                Scope = $(if ($depth -eq 1) { 'Direct' } else { 'Indirect' })
                Hops = $depth
                File = Get-Relative $edge.Source
                Line = $edge.Line
                Reference = $edge.Kind
                Via = $(if ($depth -eq 1) { '-' } else { Get-Relative $node.File })
                Chain = ($chain -join ' -> ')
                FullPath = $edge.Source
            }
            $queue.Enqueue([PSCustomObject]@{ File = $edge.Source; Depth = $depth; Chain = $chain })
        }
    }
}
#endregion

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Asset Usage Finder - Read Only'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(1320, 830)
$form.MinimumSize = New-Object System.Drawing.Size(900, 580)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'
$layout.ColumnCount = 1; $layout.RowCount = 5
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 50)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 50)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 82)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent', 100)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 82)))
$form.Controls.Add($layout)

# =========================================================
# Shared helpers for compact controls on the two toolbar rows.
function Add-Button($parent, [string]$label, [int]$width) {
    $control = New-Object System.Windows.Forms.Button
    $control.Text = $label; $control.Width = $width; $control.Height = 30
    $parent.Controls.Add($control)
    return $control
}
function Add-Bar([int]$row) {
    $panel = New-Object System.Windows.Forms.FlowLayoutPanel
    $panel.Dock = 'Fill'; $panel.WrapContents = $false; $panel.AutoScroll = $true
    $layout.Controls.Add($panel, 0, $row)
    return $panel
}
function Add-Caption($parent, [string]$label) {
    $control = New-Object System.Windows.Forms.Label
    $control.Text = $label; $control.AutoSize = $true
    $control.Margin = New-Object System.Windows.Forms.Padding(7, 12, 6, 0)
    $parent.Controls.Add($control)
}

$projectBar = Add-Bar 0
Add-Caption $projectBar 'Project:'
$projectBox = New-Object System.Windows.Forms.TextBox
$projectBox.Width = 850; $projectBox.ReadOnly = $true
$projectBox.Text = $script:ProjectRoot
$projectBar.Controls.Add($projectBox)
$projectChoose = Add-Button $projectBar 'Choose project...' 145

$assetBar = Add-Bar 1
Add-Caption $assetBar 'Asset:'
$assetBox = New-Object System.Windows.Forms.TextBox
$assetBox.Width = 720
$assetBar.Controls.Add($assetBox)
$assetChoose = Add-Button $assetBar 'Browse file...' 120
$run = Add-Button $assetBar 'Find usage / Refresh' 170
$includeToolbox = New-Object System.Windows.Forms.CheckBox
$includeToolbox.Text = 'Include toolbox files'
$includeToolbox.AutoSize = $true; $includeToolbox.Checked = $false
$assetBar.Controls.Add($includeToolbox)

$note = New-Object System.Windows.Forms.Label
$note.Dock = 'Fill'
$note.Padding = New-Object System.Windows.Forms.Padding(10, 6, 10, 0)
$note.Text = "Select a project file and click Find usage. Direct hits reference the file; indirect hits trace .tres/.tscn/script dependencies back to it. PNG ImageFolder variants are included. No project files are changed."
$layout.Controls.Add($note, 0, 2)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock = 'Fill'; $grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false; $grid.AllowUserToDeleteRows = $false
$grid.RowHeadersVisible = $false; $grid.AutoGenerateColumns = $false
$grid.SelectionMode = 'FullRowSelect'; $grid.MultiSelect = $false
$layout.Controls.Add($grid, 0, 3)

foreach ($spec in @(
    @('Scope', 'Usage', 85), @('Hops', 'Hops', 60),
    @('File', 'Referencing file', 350), @('Line', 'Line', 65),
    @('Reference', 'Reference type', 175), @('Via', 'Depends through', 310),
    @('Chain', 'Dependency chain', 420)
)) {
    $col = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $col.Name = $spec[0]; $col.DataPropertyName = $spec[0]
    $col.HeaderText = $spec[1]; $col.Width = $spec[2]
    [void]$grid.Columns.Add($col)
}
$grid.Columns['Chain'].AutoSizeMode = 'Fill'

$footer = New-Object System.Windows.Forms.TableLayoutPanel
$footer.Dock = 'Fill'; $footer.RowCount = 2; $footer.ColumnCount = 1
[void]$footer.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 38)))
[void]$footer.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent', 100)))
$layout.Controls.Add($footer, 0, 4)
$buttons = New-Object System.Windows.Forms.FlowLayoutPanel
$buttons.Dock = 'Fill'; $footer.Controls.Add($buttons, 0, 0)
$openFile = Add-Button $buttons 'Locate referencing file' 190
$copyPath = Add-Button $buttons 'Copy selected path' 160
$copyReport = Add-Button $buttons 'Copy results' 140
$status = New-Object System.Windows.Forms.Label
$status.Dock = 'Fill'; $status.Padding = New-Object System.Windows.Forms.Padding(8, 4, 0, 0)
$status.Text = 'Ready. Results detect saved paths and UIDs, not every possible dynamic runtime load.'
$footer.Controls.Add($status, 0, 1)
#endregion

#region Search and interactions
# =========================================================
# Display a sortable result table and a copyable plain-text report.
function Present-Results([string]$asset, $stats) {
    $table = New-Object System.Data.DataTable
    [void]$table.Columns.Add('Scope', [string])
    [void]$table.Columns.Add('Hops', [int])
    [void]$table.Columns.Add('File', [string])
    [void]$table.Columns.Add('Line', [int])
    [void]$table.Columns.Add('Reference', [string])
    [void]$table.Columns.Add('Via', [string])
    [void]$table.Columns.Add('Chain', [string])
    [void]$table.Columns.Add('FullPath', [string])

    $report = New-Object 'System.Collections.Generic.List[string]'
    $report.Add("Asset: $(Get-Relative $asset)")
    $report.Add('Usage Finder scans saved paths, UIDs, and PNG ImageFolder references.')
    foreach ($hit in $script:Results) {
        $row = $table.NewRow()
        foreach ($name in @('Scope', 'Hops', 'File', 'Line', 'Reference', 'Via', 'Chain', 'FullPath')) {
            $row[$name] = $hit.$name
        }
        [void]$table.Rows.Add($row)
        $report.Add(('{0} ({1} hops) {2}:{3} [{4}] via {5}' -f
            $hit.Scope, $hit.Hops, $hit.File, $hit.Line, $hit.Reference, $hit.Via))
    }
    $table.DefaultView.Sort = 'Hops ASC, File ASC'
    $grid.DataSource = $table.DefaultView
    $script:Report = $report -join [Environment]::NewLine
    $direct = @($script:Results | Where-Object { $_.Hops -eq 1 }).Count
    $indirect = $script:Results.Count - $direct
    $status.Text = ('{0} direct and {1} indirect file(s) | {2} text files scanned | {3} unreadable | Read-only. No matches does not prove unused.' -f
        $direct, $indirect, $stats.Scanned, $stats.Unreadable)
}

# =========================================================
# Index the current saved project state and trace incoming dependencies.
function Invoke-Search {
    if ($script:Busy) { return }
    $script:Busy = $true
    $run.Enabled = $false; $assetChoose.Enabled = $false; $projectChoose.Enabled = $false
    $includeToolbox.Enabled = $false; $form.UseWaitCursor = $true
    try {
        if (!$script:ProjectRoot -or !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'project.godot') -PathType Leaf)) {
            throw 'Choose the folder containing project.godot.'
        }
        $asset = Resolve-AssetPath $assetBox.Text
        $script:Selected = $asset
        $assetBox.Text = Get-Relative $asset
        $status.Text = 'Reading project references...'
        $uid = Get-AssetUid $asset
        $stats = Index-References $asset $uid
        Find-Usage $asset
        Present-Results $asset $stats
    }
    catch {
        $status.Text = 'Search failed: ' + $_.Exception.Message
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Asset Usage Finder')
    }
    finally {
        $script:Busy = $false
        if (!$form.IsDisposed) {
            $run.Enabled = $true; $assetChoose.Enabled = $true
            $projectChoose.Enabled = $true; $includeToolbox.Enabled = $true
            $form.UseWaitCursor = $false
        }
    }
}

# =========================================================
# Locate selected results with Explorer instead of changing source files.
function Get-SelectedResult {
    if (!$grid.CurrentRow -or !$grid.CurrentRow.DataBoundItem) { return $null }
    return [string]$grid.CurrentRow.DataBoundItem['FullPath']
}
function Locate-Result {
    $path = Get-SelectedResult
    if ($path -and (Test-Path -LiteralPath $path -PathType Leaf)) {
        Start-Process -FilePath 'explorer.exe' -ArgumentList ('/select,"{0}"' -f $path)
    }
}

$assetChoose.Add_Click({
    if (!$script:ProjectRoot) {
        [void][System.Windows.Forms.MessageBox]::Show('Choose a Godot project first.', 'Asset Usage Finder')
        return
    }
    $picker = New-Object System.Windows.Forms.OpenFileDialog
    $picker.Title = 'Choose a project asset'
    $picker.Filter = 'All files (*.*)|*.*'
    $picker.InitialDirectory = $script:ProjectRoot
    try {
        if ($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $assetBox.Text = $picker.FileName
            Invoke-Search
        }
    }
    finally { $picker.Dispose() }
})
$projectChoose.Add_Click({
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        if ($script:ProjectRoot) { $picker.SelectedPath = $script:ProjectRoot }
        if ($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:ProjectRoot = $picker.SelectedPath
            $projectBox.Text = $script:ProjectRoot
            $assetBox.Text = ''
            $grid.DataSource = $null
            $status.Text = 'Project changed. Choose an asset to inspect.'
        }
    }
    finally { $picker.Dispose() }
})
$run.Add_Click({ Invoke-Search })
$assetBox.Add_KeyDown({
    if ($_.KeyCode -eq [System.Windows.Forms.Keys]::Enter) {
        $_.SuppressKeyPress = $true
        Invoke-Search
    }
})
$grid.Add_CellDoubleClick({ Locate-Result })
$openFile.Add_Click({ Locate-Result })
$copyPath.Add_Click({
    $path = Get-SelectedResult
    if ($path) { [System.Windows.Forms.Clipboard]::SetText($path) }
})
$copyReport.Add_Click({
    if ($script:Report) { [System.Windows.Forms.Clipboard]::SetText($script:Report) }
})
$form.Add_FormClosing({
    if ($script:Busy) {
        $_.Cancel = $true
        $status.Text = 'Please allow the read-only scan to finish.'
    }
})
try { [void]$form.ShowDialog() } finally { $form.Dispose() }
#endregion
