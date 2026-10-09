# BEN TOOLBOX - Biome Population Inspector (read-only).
# Shows the species actually referenced by each surface biome, their relative
# weights, base spawn frequencies, artwork status, and local unregistered species.
# Parses saved .tres files without running Godot or modifying game resources.
# Designed for Windows PowerShell 5.1 with -STA.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

#region Project and resource parsing
# =========================================================
# Find project.godot above the installed inspector.
$script:ProjectRoot = $PSScriptRoot
while ($script:ProjectRoot -and !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'project.godot') -PathType Leaf)) {
    $parent = Split-Path -Parent $script:ProjectRoot
    if (!$parent -or $parent -eq $script:ProjectRoot) { $script:ProjectRoot = $null; break }
    $script:ProjectRoot = $parent
}
$script:Cache = @{}
$script:Biomes = @()
$script:Rows = New-Object 'System.Collections.Generic.List[object]'
$script:Busy = $false

# =========================================================
# Safely convert Godot's res:// references to this project's absolute paths.
function Convert-ResourcePath([string]$resourcePath) {
    if (!$resourcePath -or !$resourcePath.StartsWith('res://') -or !$script:ProjectRoot) { return $null }
    $relative = $resourcePath.Substring(6).Replace('/', [IO.Path]::DirectorySeparatorChar)
    $full = [IO.Path]::GetFullPath((Join-Path $script:ProjectRoot $relative))
    $root = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (!$full.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $null }
    return $full
}

# =========================================================
# Read external references, inline subresources and literal saved properties.
# A small targeted parser is sufficient for the project's population .tres layout.
function Read-Tres([string]$path) {
    if (!$path -or !(Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $path = [IO.Path]::GetFullPath($path)
    if ($script:Cache.ContainsKey($path)) { return $script:Cache[$path] }

    $doc = [PSCustomObject]@{ File = $path; Externals = @{}; Sections = @{}; Header = '' }
    $script:Cache[$path] = $doc
    $section = $null
    foreach ($line in [IO.File]::ReadAllLines($path)) {
        if ($line -match '^\s*\[gd_resource\b') { $doc.Header = $line; continue }
        if ($line -match '^\s*\[ext_resource\b') {
            $id = [regex]::Match($line, '\bid="([^"]+)"')
            $resource = [regex]::Match($line, '\bpath="([^"]+)"')
            if ($id.Success -and $resource.Success) { $doc.Externals[$id.Groups[1].Value] = $resource.Groups[1].Value }
            continue
        }
        if ($line -match '^\s*\[sub_resource\b') {
            $id = [regex]::Match($line, '\bid="([^"]+)"')
            $section = @{}
            if ($id.Success) { $doc.Sections['sub:' + $id.Groups[1].Value] = $section }
            continue
        }
        if ($line -match '^\s*\[resource\]\s*$') {
            $section = @{}
            $doc.Sections['resource'] = $section
            continue
        }
        if ($line -match '^\s*\[') { $section = $null; continue }
        if ($null -ne $section -and $line -match '^\s*([A-Za-z_]\w*)\s*=\s*(.*)$') {
            $section[$Matches[1]] = $Matches[2].Trim()
        }
    }
    return $doc
}

# =========================================================
# Read a saved scalar or its actual C# default when Godot omitted the property.
function Get-Value($section, [string]$key, $default) {
    if ($null -ne $section -and $section.ContainsKey($key)) { return $section[$key] }
    return $default
}

function Get-Text($section, [string]$key, [string]$default = '') {
    return ([string](Get-Value $section $key $default)).Trim('"')
}

function Get-Number($section, [string]$key, [double]$default = 0) {
    $raw = [string](Get-Value $section $key $default)
    $parsed = 0.0
    if ([double]::TryParse($raw, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) { return $parsed }
    return $default
}

# =========================================================
# Extract the actual items inside a Godot Array[T]([ ... ]) expression;
# do not mistake the type annotation's ExtResource for an array member.
function Get-ArrayRefs([string]$expression) {
    if ([string]::IsNullOrWhiteSpace($expression)) { return }
    $array = [regex]::Match($expression, '^Array\[.*\]\(\[(.*)\]\)$')
    if ($array.Success) { $body = $array.Groups[1].Value }
    elseif ($expression -match '^\[(.*)\]$') { $body = $Matches[1] }
    else { return }
    foreach ($match in [regex]::Matches($body, '(ExtResource|SubResource)\("([^"]+)"\)')) {
        [PSCustomObject]@{ Kind = $match.Groups[1].Value; Id = $match.Groups[2].Value }
    }
}

# =========================================================
# Follow one referenced .tres resource, whether inline or external.
function Resolve-Ref($doc, [string]$reference) {
    if ($null -eq $doc -or !$reference -or
        $reference -notmatch '^(ExtResource|SubResource)\("([^"]+)"\)$') { return $null }
    $kind = $Matches[1]
    $id = $Matches[2]
    if ($kind -eq 'SubResource') {
        $key = 'sub:' + $id
        if (!$doc.Sections.ContainsKey($key)) { return $null }
        return [PSCustomObject]@{ Doc = $doc; Section = $doc.Sections[$key]; Path = $doc.File }
    }
    if (!$doc.Externals.ContainsKey($id)) { return $null }
    $path = Convert-ResourcePath $doc.Externals[$id]
    if (!$path -or !($path -match '\.tres$')) { return $null }
    $other = Read-Tres $path
    if ($null -eq $other -or !$other.Sections.ContainsKey('resource')) { return $null }
    return [PSCustomObject]@{ Doc = $other; Section = $other.Sections['resource']; Path = $path }
}

# =========================================================
# Convert an array-member token into a reference expression.
function Resolve-ArrayItem($doc, $item) {
    if ($null -eq $item) { return $null }
    return Resolve-Ref $doc ('{0}("{1}")' -f $item.Kind, $item.Id)
}

# =========================================================
# Give file references short project-relative labels for the data grid.
function Get-Relative([string]$path) {
    if (!$path) { return '(missing)' }
    $root = [IO.Path]::GetFullPath($script:ProjectRoot).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { return $path.Substring($root.Length) }
    return $path
}

# =========================================================
# Resolve the currently configured artwork source, not merely the visual .tres.
function Get-Artwork($definition) {
    if ($null -eq $definition) { return 'Missing definition' }
    $field = if ($definition.Section.ContainsKey('Visual')) { 'Visual' } else { 'VisualOverride' }
    $visual = Resolve-Ref $definition.Doc (Get-Text $definition.Section $field)
    if ($null -eq $visual) { return 'Procedural / fallback' }
    $imageFolder = Get-Text $visual.Section 'ImageFolder'
    if ($imageFolder) {
        $folder = Convert-ResourcePath $imageFolder
        if (!$folder -or !(Test-Path -LiteralPath $folder -PathType Container)) { return 'ImageFolder MISSING' }
        $pngs = @(Get-ChildItem -LiteralPath $folder -Filter '*.png' -File -ErrorAction SilentlyContinue)
        return ('{0} PNG variant(s)' -f $pngs.Count)
    }
    if ($visual.Section.ContainsKey('Image')) { return 'Single image' }
    if ($visual.Section.ContainsKey('VisualScene')) { return 'Visual scene' }
    return 'Procedural / fallback'
}
#endregion

#region Population extraction
# =========================================================
# Build a list of weighted species and their selection shares for one family.
function Add-SpeciesGroup([string]$group, $owner, [string]$property, [bool]$enabled) {
    if ($null -eq $owner) { return }
    $entries = @()
    $total = 0.0
    foreach ($ref in @(Get-ArrayRefs (Get-Text $owner.Section $property))) {
        $entry = Resolve-ArrayItem $owner.Doc $ref
        $weight = if ($entry) { Get-Number $entry.Section 'Weight' 1 } else { 0.0 }
        $definition = if ($entry) { Resolve-Ref $entry.Doc (Get-Text $entry.Section 'Definition') } else { $null }
        $entries += [PSCustomObject]@{ Entry = $entry; Weight = $weight; Definition = $definition }
        if ($definition -and $weight -gt 0) { $total += $weight }
    }

    foreach ($item in $entries) {
        $definition = $item.Definition
        $name = if ($definition) { [IO.Path]::GetFileNameWithoutExtension($definition.Path) } else { '(missing definition)' }
        $id = if ($definition) { Get-Text $definition.Section 'Id' '(ID not saved)' } else { '' }
        $status = if (!$item.Entry -or !$definition) { 'Missing reference' }
                  elseif (!$enabled) { 'Family disabled' }
                  elseif ($item.Weight -lt 0) { 'Invalid weight' }
                  elseif ($item.Weight -eq 0) { 'Weight zero' }
                  else { 'Active' }
        $percent = if ($total -gt 0 -and $item.Weight -gt 0) {
            '{0:N1}%' -f (100.0 * $item.Weight / $total)
        } else { '0%' }
        $script:Rows.Add([PSCustomObject]@{
            Group = $group; Species = $name; Id = $id; Weight = ('{0:0.##}' -f $item.Weight)
            Share = $percent; Artwork = (Get-Artwork $definition)
            Status = $status; File = $(if ($definition) { Get-Relative $definition.Path } else { '(missing)' })
            FullPath = $(if ($definition) { $definition.Path } else { '' })
        })
    }
}

# =========================================================
# Show the enemy population, whose weights live on EntityDefinition directly.
function Add-EnemyGroup($content, [bool]$enabled) {
    $ref = Get-Text $content.Section 'Enemies'
    if (!$ref) { return }
    $enemies = Resolve-Ref $content.Doc $ref
    if (!$enemies) {
        $script:Rows.Add([PSCustomObject]@{ Group = 'Enemies'; Species = '(missing population)'; Id = ''; Weight = ''; Share = '';
            Artwork = ''; Status = 'Missing reference'; File = '(missing)'; FullPath = '' })
        return
    }
    $list = @()
    $total = 0.0
    foreach ($item in @(Get-ArrayRefs (Get-Text $enemies.Section 'Definitions'))) {
        $definition = Resolve-ArrayItem $enemies.Doc $item
        $weight = if ($definition) { Get-Number $definition.Section 'SpawnWeight' 1 } else { 0.0 }
        $list += [PSCustomObject]@{ Definition = $definition; Weight = $weight }
        if ($definition -and $weight -gt 0) { $total += $weight }
    }
    foreach ($item in $list) {
        $definition = $item.Definition
        $name = if ($definition) { Get-Text $definition.Section 'DisplayName' ([IO.Path]::GetFileNameWithoutExtension($definition.Path)) } else { '(missing definition)' }
        $id = if ($definition) { Get-Text $definition.Section 'SpeciesId' } else { '' }
        $status = if (!$definition) { 'Missing reference' } elseif (!$enabled) { 'Family disabled' }
                  elseif ($item.Weight -lt 0) { 'Invalid weight' } elseif ($item.Weight -eq 0) { 'Weight zero' } else { 'Active' }
        $script:Rows.Add([PSCustomObject]@{
            Group = 'Enemies'; Species = $name; Id = $id; Weight = ('{0:0.##}' -f $item.Weight)
            Share = $(if ($total -gt 0 -and $item.Weight -gt 0) { '{0:N1}%' -f (100.0 * $item.Weight / $total) } else { '0%' })
            Artwork = (Get-Artwork $definition); Status = $status
            File = $(if ($definition) { Get-Relative $definition.Path } else { '(missing)' })
            FullPath = $(if ($definition) { $definition.Path } else { '' })
        })
    }
}

# =========================================================
# Locate locally defined plant/tree/grass resources that are not actually
# in this biome's active species lists. Shared definitions are not flagged.
function Add-Unregistered($biomePath) {
    $folder = Join-Path (Split-Path -Parent $biomePath) 'Vegetation'
    if (!(Test-Path -LiteralPath $folder -PathType Container)) { return 0 }
    $registered = @{}
    foreach ($row in $script:Rows) {
        if ($row.FullPath -and $row.Group -in @('Trees', 'Plants', 'Grass')) { $registered[$row.FullPath] = $true }
    }
    $count = 0
    foreach ($file in @(Get-ChildItem -LiteralPath $folder -Filter '*.tres' -File -Recurse -ErrorAction SilentlyContinue)) {
        $definition = Read-Tres $file.FullName
        if (!$definition) { continue }
        $classMatch = [regex]::Match($definition.Header, 'script_class="(TreeDefinition|PlantDefinition|GrassDefinition)"')
        if (!$classMatch.Success -or $registered.ContainsKey($file.FullName)) { continue }
        $family = switch ($classMatch.Groups[1].Value) {
            'TreeDefinition' { 'Trees' }
            'PlantDefinition' { 'Plants' }
            'GrassDefinition' { 'Grass' }
        }
        $section = $definition.Sections['resource']
        $script:Rows.Add([PSCustomObject]@{
            Group = 'Unregistered'; Species = $file.BaseName; Id = (Get-Text $section 'Id')
            Weight = '-'; Share = '-'; Artwork = (Get-Artwork ([PSCustomObject]@{
                Doc = $definition; Section = $section; Path = $file.FullName }))
            Status = 'Not in active content (' + $family + ')'; File = (Get-Relative $file.FullName)
            FullPath = $file.FullName
        })
        $count++
    }
    return $count
}

# =========================================================
# Get the frequency multiplier and optional clumping rule; inherited settings
# come from CONFIG/Biomes/BiomeDefaults.tres, not a guessed multiplier.
function Get-Placement($owner, [string]$family, $defaults) {
    $placement = $null
    if ($owner) { $placement = Resolve-Ref $owner.Doc (Get-Text $owner.Section ($family + 'Placement')) }
    if (!$placement -and $defaults) {
        $placement = Resolve-Ref $defaults.Doc (Get-Text $defaults.Section $family)
    }
    $frequency = if ($placement) { Get-Number $placement.Section 'FrequencyMultiplier' 1 } else { 1.0 }
    $distribution = if ($placement) { Get-Number $placement.Section 'Distribution' 0 } else { 0.0 }
    $shape = if ($distribution -eq 1) { 'Clumps' } else { 'Uniform' }
    return [PSCustomObject]@{ Frequency = $frequency; Shape = $shape }
}

# =========================================================
# Display the saved population for one selected registered surface biome.
function Show-Biome {
    if ($script:Busy -or $biomePicker.SelectedIndex -lt 0 -or $biomePicker.SelectedIndex -ge $script:Biomes.Count) { return }
    try {
        $script:Cache = @{}
        $script:Rows.Clear()
        $selected = $script:Biomes[$biomePicker.SelectedIndex]
        $biome = Read-Tres $selected.Path
        if (!$biome) { throw 'Selected biome resource is missing.' }
        $biomeRoot = [PSCustomObject]@{ Doc = $biome; Section = $biome.Sections['resource']; Path = $biome.File }
        $content = Resolve-Ref $biome (Get-Text $biomeRoot.Section 'Content')
        $usingContent = $null -ne $content
        if (!$content) { $content = $biomeRoot }
        $vegetation = Resolve-Ref $content.Doc (Get-Text $content.Section 'Vegetation')
        $veg = if ($vegetation) { $vegetation.Section } else { $null }

        $plantPatches = Get-Number $veg 'PlantPatches' 3
        $perPatch = Get-Number $veg 'PlantsPerPatch' 4
        $trees = Get-Number $veg 'TreesPerChunk' 3
        $grassPatches = Get-Number $veg 'GrassPatches' 6
        $tufts = Get-Number $veg 'GrassTuftsPerPatch' 10
        $rocks = Get-Number $content.Section 'RocksPerChunk' 8

        $defaultPath = Join-Path $script:ProjectRoot 'CONFIG\Biomes\BiomeDefaults.tres'
        $defaultDoc = Read-Tres $defaultPath
        $defaults = if ($defaultDoc) {
            [PSCustomObject]@{ Doc = $defaultDoc; Section = $defaultDoc.Sections['resource'] }
        } else { $null }

        $base = @{ Plants = $plantPatches * $perPatch; Trees = $trees; Grass = $grassPatches * $tufts; Rocks = $rocks }
        $placements = @{}
        foreach ($family in @('Plants', 'Trees', 'Grass', 'Rocks')) {
            $placementOwner = if ($family -eq 'Rocks') { $content } else { $vegetation }
            $placements[$family] = Get-Placement $placementOwner $family $defaults
            Add-SpeciesGroup $family $placementOwner $family ($base[$family] -gt 0 -and $placements[$family].Frequency -gt 0)
        }

        $enemyRef = Resolve-Ref $content.Doc (Get-Text $content.Section 'Enemies')
        $minEnemies = if ($enemyRef) { Get-Number $enemyRef.Section 'MinimumPerChunk' 0 } else { 0 }
        $maxEnemies = if ($enemyRef) { Get-Number $enemyRef.Section 'MaximumPerChunk' 2 } else { 0 }
        Add-EnemyGroup $content ($maxEnemies -gt 0)
        $unregistered = Add-Unregistered $selected.Path

        $lines = New-Object 'System.Collections.Generic.List[string]'
        $lines.Add(('BIOME: {0} [{1}]    {2}' -f $selected.Name, $selected.Id,
            $(if ($selected.Enabled) { 'Enabled' } else { 'Disabled' })))
        $lines.Add(('Population source: {0} ({1})' -f (Get-Relative $content.Path), $(if ($usingContent) { 'Content resource' } else { 'legacy fields on biome' })))
        $lines.Add('')
        foreach ($family in @('Plants', 'Trees', 'Grass', 'Rocks')) {
            $info = $placements[$family]
            $detail = switch ($family) {
                'Plants' { '{0} patches x {1} per patch' -f $plantPatches, $perPatch }
                'Grass' { '{0} patches x {1} tufts' -f $grassPatches, $tufts }
                default { ('{0:0.##} per chunk' -f $base[$family]) }
            }
            $lines.Add(('{0,-7} {1,-24} x {2:0.##} = {3:0.##} target frequency ({4})' -f
                $family, $detail, $info.Frequency, ($base[$family] * $info.Frequency), $info.Shape))
        }
        $lines.Add(('Enemies  {0} - {1} per chunk (separate entity population)' -f $minEnemies, $maxEnemies))
        $lines.Add(('Unregistered local vegetation definitions: {0}' -f $unregistered))
        if ((Get-Text $biomeRoot.Section 'Content') -and !$usingContent) {
            $lines.Add('WARNING: Content reference could not be loaded; showing legacy population fields instead.')
        }
        $lines.Add('Note: target frequency is not a guaranteed spawn count. Terrain, collisions and biome blending can reject candidates.')
        $details.Text = $lines -join [Environment]::NewLine
        Refresh-Grid
        $status.Text = ('{0} registered entries; {1} unregistered local definitions. Read-only.' -f
            @($script:Rows | Where-Object { $_.Group -ne 'Unregistered' }).Count, $unregistered)
    }
    catch {
        $status.Text = 'Unable to inspect selected biome: ' + $_.Exception.Message
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Biome Population Inspector')
    }
}
#endregion

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = 'Biome Population Inspector - Read Only'
$form.StartPosition = 'CenterScreen'
$form.Size = New-Object System.Drawing.Size(1350, 815)
$form.MinimumSize = New-Object System.Drawing.Size(970, 650)
$form.Font = New-Object System.Drawing.Font('Segoe UI', 10)

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'
$layout.RowCount = 5
$layout.ColumnCount = 1
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 47)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 54)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 203)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent', 100)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute', 34)))
$form.Controls.Add($layout)

$top = New-Object System.Windows.Forms.FlowLayoutPanel
$top.Dock = 'Fill'
$top.WrapContents = $false
$layout.Controls.Add($top, 0, 0)
$top.Controls.Add((New-Object System.Windows.Forms.Label -Property @{ Text = 'Project:'; AutoSize = $true; Margin = (New-Object System.Windows.Forms.Padding(7,12,4,0)) }))
$projectBox = New-Object System.Windows.Forms.TextBox
$projectBox.Width = 840
$projectBox.ReadOnly = $true
$projectBox.Text = $script:ProjectRoot
$top.Controls.Add($projectBox)
$browse = New-Object System.Windows.Forms.Button
$browse.Text = 'Choose...'; $browse.Width = 95
$top.Controls.Add($browse)
$refresh = New-Object System.Windows.Forms.Button
$refresh.Text = 'Refresh'; $refresh.Width = 100
$top.Controls.Add($refresh)

$bar = New-Object System.Windows.Forms.FlowLayoutPanel
$bar.Dock = 'Fill'
$bar.WrapContents = $false
$layout.Controls.Add($bar, 0, 1)
$bar.Controls.Add((New-Object System.Windows.Forms.Label -Property @{ Text = 'Biome:'; AutoSize = $true; Margin = (New-Object System.Windows.Forms.Padding(7,12,4,0)) }))
$biomePicker = New-Object System.Windows.Forms.ComboBox
$biomePicker.DropDownStyle = 'DropDownList'; $biomePicker.Width = 340
$bar.Controls.Add($biomePicker)
$bar.Controls.Add((New-Object System.Windows.Forms.Label -Property @{ Text = 'Show:'; AutoSize = $true; Margin = (New-Object System.Windows.Forms.Padding(16,12,4,0)) }))
$filterPicker = New-Object System.Windows.Forms.ComboBox
$filterPicker.DropDownStyle = 'DropDownList'; $filterPicker.Width = 150
[void]$filterPicker.Items.AddRange([object[]]@('All', 'Trees', 'Plants', 'Grass', 'Rocks', 'Enemies', 'Unregistered'))
$filterPicker.SelectedIndex = 0
$bar.Controls.Add($filterPicker)
$openFile = New-Object System.Windows.Forms.Button
$openFile.Text = 'Locate selected species'; $openFile.Width = 184
$bar.Controls.Add($openFile)
$openContent = New-Object System.Windows.Forms.Button
$openContent.Text = 'Locate biome content'; $openContent.Width = 170
$bar.Controls.Add($openContent)

$details = New-Object System.Windows.Forms.TextBox
$details.Multiline = $true
$details.ReadOnly = $true
$details.ScrollBars = 'Vertical'
$details.Dock = 'Fill'
$details.Font = New-Object System.Drawing.Font('Consolas', 10)
$layout.Controls.Add($details, 0, 2)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock = 'Fill'; $grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false; $grid.AllowUserToDeleteRows = $false
$grid.SelectionMode = 'FullRowSelect'; $grid.MultiSelect = $false
$grid.RowHeadersVisible = $false; $grid.AutoGenerateColumns = $false
$layout.Controls.Add($grid, 0, 3)
$columns = @(
    @('Group','Category',105), @('Species','Species',160), @('Id','ID',145),
    @('Weight','Weight',80), @('Share','Selection share',125), @('Artwork','Artwork',160),
    @('Status','Status',220), @('File','Resource path',580)
)
foreach ($spec in $columns) {
    $col = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $col.Name = $spec[0]; $col.DataPropertyName = $spec[0]
    $col.HeaderText = $spec[1]; $col.Width = $spec[2]
    [void]$grid.Columns.Add($col)
}
$grid.Columns['File'].AutoSizeMode = 'Fill'
$status = New-Object System.Windows.Forms.Label
$status.Dock = 'Fill'; $status.Text = 'Ready. This tool does not modify game files.'
$layout.Controls.Add($status, 0, 4)

# =========================================================
# Rebuild the display after switching a category filter or biome.
function Refresh-Grid {
    $table = New-Object System.Data.DataTable
    foreach ($name in @('Group','Species','Id','Weight','Share','Artwork','Status','File','FullPath')) {
        [void]$table.Columns.Add($name, [string])
    }
    $filter = [string]$filterPicker.SelectedItem
    foreach ($item in $script:Rows) {
        if ($filter -ne 'All' -and $item.Group -ne $filter) { continue }
        $row = $table.NewRow()
        foreach ($key in @('Group','Species','Id','Weight','Share','Artwork','Status','File','FullPath')) {
            $row[$key] = [string]$item.$key
        }
        [void]$table.Rows.Add($row)
    }
    $grid.DataSource = $table
}

# =========================================================
# Load only biomes registered in the world's authoritative BiomeCatalog.
function Refresh-Biomes {
    if ($script:Busy) { return }
    $script:Busy = $true
    $refresh.Enabled = $false
    $browse.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        if (!$script:ProjectRoot -or !(Test-Path -LiteralPath (Join-Path $script:ProjectRoot 'project.godot'))) {
            throw 'Choose your Godot project root (the folder with project.godot).'
        }
        $previous = if ($biomePicker.SelectedIndex -ge 0 -and $biomePicker.SelectedIndex -lt $script:Biomes.Count) {
            $script:Biomes[$biomePicker.SelectedIndex].Path
        } else { '' }
        $script:Cache = @{}
        $catalogPath = Join-Path $script:ProjectRoot 'WORLD\Generation\Biomes\BiomeCatalog.tres'
        $catalog = Read-Tres $catalogPath
        if (!$catalog) { throw 'Missing WORLD/Generation/Biomes/BiomeCatalog.tres.' }
        $biomes = @()
        foreach ($entry in @(Get-ArrayRefs (Get-Text $catalog.Sections['resource'] 'Biomes'))) {
            $biome = Resolve-ArrayItem $catalog $entry
            if (!$biome) { continue }
            $name = Get-Text $biome.Section 'DisplayName'
            if (!$name) { $name = ([IO.Path]::GetFileNameWithoutExtension($biome.Path) -creplace '(?<=[a-z])(?=[A-Z])', ' ') }
            $id = Get-Text $biome.Section 'Id' '(default)'
            $enabled = (Get-Text $biome.Section 'Enabled' 'true') -ne 'false'
            $biomes += [PSCustomObject]@{ Path = $biome.Path; Name = $name; Id = $id; Enabled = $enabled }
        }
        if ($biomes.Count -eq 0) { throw 'BiomeCatalog has no readable biome entries.' }
        $script:Biomes = $biomes
        $biomePicker.Items.Clear()
        $select = 0
        for ($i = 0; $i -lt $biomes.Count; $i++) {
            $name = $biomes[$i].Name
            if (!$biomes[$i].Enabled) { $name += ' [DISABLED]' }
            [void]$biomePicker.Items.Add($name)
            if ($biomes[$i].Path -eq $previous) { $select = $i }
        }
        $projectBox.Text = $script:ProjectRoot
        $biomePicker.SelectedIndex = $select
    }
    catch {
        $status.Text = $_.Exception.Message
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, 'Biome Population Inspector')
    }
    finally {
        $script:Busy = $false
        $refresh.Enabled = $true
        $browse.Enabled = $true
        $form.UseWaitCursor = $false
    }
    Show-Biome
}

# =========================================================
# Locate files without opening or changing their contents.
function Locate-File([string]$path) {
    if (!$path -or !(Test-Path -LiteralPath $path -PathType Leaf)) { return }
    Start-Process -FilePath 'explorer.exe' -ArgumentList ('/select,"{0}"' -f $path)
}

$refresh.Add_Click({ Refresh-Biomes })
$biomePicker.Add_SelectedIndexChanged({ Show-Biome })
$filterPicker.Add_SelectedIndexChanged({ Refresh-Grid })
$browse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        if ($script:ProjectRoot) { $dialog.SelectedPath = $script:ProjectRoot }
        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:ProjectRoot = $dialog.SelectedPath
            $script:Biomes = @(); $biomePicker.Items.Clear()
            Refresh-Biomes
        }
    } finally { $dialog.Dispose() }
})
$openFile.Add_Click({
    if ($grid.CurrentRow -and $grid.CurrentRow.DataBoundItem) {
        Locate-File ([string]$grid.CurrentRow.DataBoundItem['FullPath'])
    }
})
$grid.Add_CellDoubleClick({
    if ($grid.CurrentRow -and $grid.CurrentRow.DataBoundItem) {
        Locate-File ([string]$grid.CurrentRow.DataBoundItem['FullPath'])
    }
})
$openContent.Add_Click({
    if ($biomePicker.SelectedIndex -lt 0 -or $script:Busy) { return }
    $doc = Read-Tres $script:Biomes[$biomePicker.SelectedIndex].Path
    $content = Resolve-Ref $doc (Get-Text $doc.Sections['resource'] 'Content')
    Locate-File $(if ($content) { $content.Path } else { $doc.File })
})
$form.Add_Shown({ Refresh-Biomes })
try { [void]$form.ShowDialog() } finally { $form.Dispose() }
#endregion
