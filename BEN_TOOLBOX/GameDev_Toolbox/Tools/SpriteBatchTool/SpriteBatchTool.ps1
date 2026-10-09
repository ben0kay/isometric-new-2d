param([string]$InputFolder = "")
# Batch-resizes PNG sprites and names the output copies.
# After all copies succeed, originals move into RAW beside this script.
# Existing files are never overwritten.
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:RawFolder = Join-Path $PSScriptRoot "RAW"

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = "Sprite Batch Tool"
$form.ClientSize = New-Object System.Drawing.Size(720, 750)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false

# =========================================================
# Add a labelled input to the window.
function Add-Input($label, $value, $y, $width = 480) {
    $caption = New-Object System.Windows.Forms.Label
    $caption.Text = $label
    $caption.Location = New-Object System.Drawing.Point(15, ($y + 4))
    $caption.Size = New-Object System.Drawing.Size(150, 22)
    $form.Controls.Add($caption)

    $inputBox = New-Object System.Windows.Forms.TextBox
    $inputBox.Text = $value
    $inputBox.Location = New-Object System.Drawing.Point(170, $y)
    $inputBox.Size = New-Object System.Drawing.Size($width, 25)
    $form.Controls.Add($inputBox)
    return $inputBox
}

# =========================================================
# Add a folder picker beside an input.
function Add-FolderPicker($inputBox, $y) {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = "Browse"
    $button.Location = New-Object System.Drawing.Point(610, ($y - 1))
    $button.Size = New-Object System.Drawing.Size(90, 27)
    $button.Tag = $inputBox

    $button.Add_Click({
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        try {
            if ($picker.ShowDialog() -eq "OK") {
                $this.Tag.Text = $picker.SelectedPath
                $preview.Items.Clear()
            }
        }
        finally { $picker.Dispose() }
    })

    $form.Controls.Add($button)
}

$source = Add-Input "Source folder" $InputFolder 20 425
$destination = Add-Input "Destination folder" "" 60 425
Add-FolderPicker $source 20
Add-FolderPicker $destination 60

$scale = Add-Input "Scale multiplier" "1" 100 120
$prefix = Add-Input "Filename prefix" "Plant" 140 220
$start = Add-Input "Starting number" "1" 180 120
$padding = Add-Input "Number digits" "2" 220 120

$letterMode = New-Object System.Windows.Forms.CheckBox
$letterMode.Text = "Letter suffixes (01a, 01b, 01c)"
$letterMode.Location = New-Object System.Drawing.Point(350, 180)
$letterMode.Size = New-Object System.Drawing.Size(340, 25)
$form.Controls.Add($letterMode)


$speciesFolders = New-Object System.Windows.Forms.CheckBox
$speciesFolders.Text = "Create a folder per species (letters = variants of one species)"
$speciesFolders.Checked = $true
$speciesFolders.Location = New-Object System.Drawing.Point(15, 260)
$speciesFolders.Size = New-Object System.Drawing.Size(680, 25)
$form.Controls.Add($speciesFolders)

$resources = New-Object System.Windows.Forms.CheckBox
$resources.Text = "Generate plant .tres + Visual.tres (requires destination inside Godot project)"
$resources.Checked = $true
$resources.Location = New-Object System.Drawing.Point(15, 290)
$resources.Size = New-Object System.Drawing.Size(680, 25)
$form.Controls.Add($resources)
$artScale = Add-Input "Artwork scale" "0.5878125" 325 120
$anchor = Add-Input "Anchor Y (0 to 1)" "0.96" 360 120
$sizeMin = Add-Input "Random size minimum" "0.85" 395 120
$sizeMax = Add-Input "Random size maximum" "1.15" 430 120
$hint = New-Object System.Windows.Forms.Label
$hint.Text = "Sources are COPIED to a unique RAW run folder beside this tool; originals stay in Source."
$hint.Location = New-Object System.Drawing.Point(315, 330)
$hint.Size = New-Object System.Drawing.Size(385, 110)
$form.Controls.Add($hint)

$preview = New-Object System.Windows.Forms.ListBox
$preview.Location = New-Object System.Drawing.Point(15, 475)
$preview.Size = New-Object System.Drawing.Size(685, 180)
$preview.HorizontalScrollbar = $true
$form.Controls.Add($preview)

$previewButton = New-Object System.Windows.Forms.Button
$previewButton.Text = "Preview"
$previewButton.Location = New-Object System.Drawing.Point(15, 675)
$previewButton.Size = New-Object System.Drawing.Size(120, 35)
$form.Controls.Add($previewButton)

$processButton = New-Object System.Windows.Forms.Button
$processButton.Text = "Process"
$processButton.Location = New-Object System.Drawing.Point(150, 675)
$processButton.Size = New-Object System.Drawing.Size(120, 35)
$form.Controls.Add($processButton)

$status = New-Object System.Windows.Forms.Label
$status.Text = "PNG files only. Source files preserved; RAW copies created during processing."
$status.Location = New-Object System.Drawing.Point(285, 680)
$status.Size = New-Object System.Drawing.Size(415, 40)
$form.Controls.Add($status)
#endregion

#region Batch Planning
# =========================================================
# Generate a..z, then aa, ab, ac, and so on.
function Get-LetterSuffix([int]$index) {
    $suffix = ""
    $value = $index + 1

    while ($value -gt 0) {
        $value--
        $suffix = ([char](97 + ($value % 26))).ToString() + $suffix
        $value = [int][Math]::Floor($value / 26)
    }
    return $suffix
}

# =========================================================
# Validate folders, names and archive conflicts before writing files.
function Get-Plan {
    if (!(Test-Path -LiteralPath $source.Text -PathType Container)) {
        throw "Choose an existing source folder."
    }
    if (!(Test-Path -LiteralPath $destination.Text -PathType Container)) {
        throw "Choose an existing destination folder."
    }

    $sourcePath = (Resolve-Path -LiteralPath $source.Text).ProviderPath
    $destinationPath = (Resolve-Path -LiteralPath $destination.Text).ProviderPath
    $rawPath = [IO.Path]::GetFullPath($script:RawFolder).TrimEnd('\')

    if ($sourcePath.TrimEnd('\') -ieq $destinationPath.TrimEnd('\')) {
        throw "Source and destination must be different folders."
    }
    if ($sourcePath.TrimEnd('\') -ieq $rawPath) {
        throw "Choose a source folder other than RAW."
    }
    if ($destinationPath.TrimEnd('\') -ieq $rawPath) {
        throw "Choose a destination folder other than RAW."
    }
    if ((Test-Path -LiteralPath $script:RawFolder) -and
        !(Test-Path -LiteralPath $script:RawFolder -PathType Container)) {
        throw "RAW already exists but is not a folder."
    }

    $factor = 0.0
    $number = 0
    $digits = 0
    $culture = [System.Globalization.CultureInfo]::InvariantCulture

    if (![double]::TryParse($scale.Text,
        [System.Globalization.NumberStyles]::Float, $culture, [ref]$factor) -or
        [double]::IsNaN($factor) -or $factor -le 0 -or $factor -gt 1) {
        throw "Scale must be greater than 0 and at most 1. Example: 0.25."
    }
    if (![int]::TryParse($start.Text, [ref]$number) -or $number -lt 0) {
        throw "Starting number must be a non-negative whole number."
    }
    if (![int]::TryParse($padding.Text, [ref]$digits) -or
        $digits -lt 1 -or $digits -gt 8) {
        throw "Number digits must be between 1 and 8."
    }

    $namePrefix = $prefix.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($namePrefix) -or
        $namePrefix.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "Enter a valid filename prefix."
    }

    $files = @(Get-ChildItem -LiteralPath $sourcePath -File |
        Where-Object { $_.Extension -ieq ".png" } |
        Sort-Object Name)

    if ($files.Count -eq 0) {
        throw "No PNG files found in the source folder."
    }
    if (!$letterMode.Checked -and
        [long]$number + $files.Count - 1 -gt [int]::MaxValue) {
        throw "Starting number is too large."
    }


    if ($resources.Checked -and !$speciesFolders.Checked) {
        throw "Enable species folders to generate plant resources."
    }
    $script:ResourcePlan = @()
    $script:ArchiveRun = Join-Path $script:RawFolder ([Guid]::NewGuid().ToString("N"))
    $projectRoot = $destinationPath
    if ($resources.Checked) {
        while ($projectRoot -and !(Test-Path -LiteralPath (Join-Path $projectRoot "project.godot"))) {
            $projectRoot = Split-Path -Parent $projectRoot
        }
        if (!$projectRoot) { throw "Plant resources require a destination within a Godot project." }
        foreach ($required in @("WORLD/Contents/Vegetation/Plants/PlantDefinition.cs",
            "VISUALS/Definitions/VisualDefinition.cs",
            "VISUALS/Vegetation/VegetationVisualSettings.cs")) {
            if (!(Test-Path -LiteralPath (Join-Path $projectRoot $required))) {
                throw "Required project script missing: $required"
            }
        }
        $culture = [Globalization.CultureInfo]::InvariantCulture
        $script:ArtValue = Read-Number $artScale.Text 0.001 100
        $script:AnchorValue = Read-Number $anchor.Text 0 1
        $script:MinValue = Read-Number $sizeMin.Text 0.1 10
        $script:MaxValue = Read-Number $sizeMax.Text 0.1 10
        if ([double]$script:MaxValue -lt [double]$script:MinValue) { throw "Maximum size must be at least minimum size." }
    }
    $seen = @{}
    $index = 0
    foreach ($file in $files) {
        $suffix = ""
        if ($letterMode.Checked) {
            $suffix = Get-LetterSuffix $index
        }

        $newName = $namePrefix + $number.ToString("D$digits") + $suffix + ".png"

        $species = $namePrefix + $number.ToString("D$digits")
        if ($species -notmatch '^[A-Za-z][A-Za-z0-9_]*$') {
            throw "Use a prefix starting with a letter, containing letters, numbers or underscores."
        }
        $folder = $destinationPath
        if ($speciesFolders.Checked) {
            $folder = Join-Path $destinationPath $species
            if (Test-Path -LiteralPath $folder) {
                throw "Species folder already exists: $folder. Use a new name/number; existing species are not modified."
            }
        }
        $outputPath = Join-Path $folder $newName
        $archivePath = Join-Path $script:ArchiveRun $file.Name
        if ($resources.Checked -and !$seen.ContainsKey($species)) {
            $seen[$species] = $true
            $relative = $folder.Substring($projectRoot.Length).TrimStart('\', '/').Replace('\', '/')
            $script:ResourcePlan += [pscustomobject]@{
                Species = $species
                Folder = $folder
                ResFolder = "res://$relative"
            }
        }

        if (Test-Path -LiteralPath $outputPath) {
            throw "Output already exists: $newName. Change the prefix or starting number."
        }
        if (Test-Path -LiteralPath $archivePath) {
            throw "RAW already contains '$($file.Name)'. Move or rename that archived file first."
        }

        $image = [System.Drawing.Image]::FromFile($file.FullName)
        try {
            $newWidth = [Math]::Max(1, [int][Math]::Round($image.Width * $factor))
            $newHeight = [Math]::Max(1, [int][Math]::Round($image.Height * $factor))

            [PSCustomObject]@{
                Source = $file.FullName
                Output = $outputPath
                Archive = $archivePath
                Width = $newWidth
                Height = $newHeight
                Description = "$($file.Name) -> $outputPath   " +
                    "$($image.Width)x$($image.Height) -> ${newWidth}x${newHeight}"
            }
        }
        finally { $image.Dispose() }

        $index++
        if (!$letterMode.Checked) { $number++ }
    }
}
#endregion

#region Image Processing
# =========================================================
# Resize with transparency and save without overwriting existing files.
function Save-Sprite($entry) {
    $original = [Drawing.Image]::FromFile($entry.Source)
    try { $unchanged = ($original.Width -eq $entry.Width -and $original.Height -eq $entry.Height) }
    finally { $original.Dispose() }
    if ($unchanged) {
        [IO.File]::Copy($entry.Source, $entry.Output, $false)
        return
    }
    $image = $null
    $bitmap = $null
    $graphics = $null
    $attributes = $null
    $stream = $null
    $created = $false
    $complete = $false

    try {
        $image = [System.Drawing.Image]::FromFile($entry.Source)
        $bitmap = [System.Drawing.Bitmap]::new(
            [int]$entry.Width, [int]$entry.Height,
            [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.CompositingMode =
            [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
        $graphics.CompositingQuality =
            [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.InterpolationMode =
            [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode =
            [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        $attributes = [System.Drawing.Imaging.ImageAttributes]::new()
        $attributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)

        $rectangle = [System.Drawing.Rectangle]::new(
            0, 0, [int]$entry.Width, [int]$entry.Height)

        $graphics.DrawImage($image, $rectangle, 0, 0,
            $image.Width, $image.Height,
            [System.Drawing.GraphicsUnit]::Pixel, $attributes)

        $stream = [IO.File]::Open($entry.Output,
            [IO.FileMode]::CreateNew,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        $created = $true

        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $complete = $true
    }
    finally {
        if ($stream) { $stream.Dispose() }
        if ($attributes) { $attributes.Dispose() }
        if ($graphics) { $graphics.Dispose() }
        if ($bitmap) { $bitmap.Dispose() }
        if ($image) { $image.Dispose() }

        if ($created -and !$complete) {
            Remove-Item -LiteralPath $entry.Output -ErrorAction SilentlyContinue
        }
    }
}
#endregion


function Read-Number([string]$text, [double]$minimum, [double]$maximum) {
    $value = 0.0
    if (![double]::TryParse($text, [Globalization.NumberStyles]::Float,
        [Globalization.CultureInfo]::InvariantCulture, [ref]$value) -or
        [double]::IsNaN($value) -or $value -lt $minimum -or $value -gt $maximum) {
        throw "Enter a number between $minimum and $maximum (use a decimal point)."
    }
    return $value.ToString("0.########", [Globalization.CultureInfo]::InvariantCulture)
}
function Write-NewText([string]$path, [string]$text, $written) {
    $stream = [IO.File]::Open($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    $written.Add($path)
    try {
        $encoding = New-Object System.Text.UTF8Encoding($false)
        $bytes = $encoding.GetBytes($text)
        $stream.Write($bytes, 0, $bytes.Length)
    } finally { $stream.Dispose() }
}
function Write-PlantResources($entry, $written) {
    $species = $entry.Species
    $folder = $entry.ResFolder
    $id = ([regex]::Replace($species, '([a-z0-9])([A-Z])', '$1_$2')).ToLowerInvariant()
    $visual = @"
[gd_resource type="Resource" script_class="VisualDefinition" format=3]

[ext_resource type="Script" path="res://VISUALS/Definitions/VisualDefinition.cs" id="1_visual"]
[ext_resource type="Script" path="res://VISUALS/Vegetation/VegetationVisualSettings.cs" id="2_effects"]

[sub_resource type="Resource" id="Effects"]
script = ExtResource("2_effects")

[resource]
script = ExtResource("1_visual")
ImageFolder = "$folder"
ArtworkScale = Vector2($script:ArtValue, $script:ArtValue)
ImageAnchor = Vector2(0.5, $script:AnchorValue)
Vegetation = SubResource("Effects")
"@
    $plant = @"
[gd_resource type="Resource" script_class="PlantDefinition" format=3]

[ext_resource type="Script" path="res://WORLD/Contents/Vegetation/Plants/PlantDefinition.cs" id="1_plant"]
[ext_resource type="Resource" path="$folder/${species}Visual.tres" id="2_visual"]

[resource]
script = ExtResource("1_plant")
Id = "$id"
Visual = ExtResource("2_visual")
ContactShadowScale = Vector2(1, 0.75)
Spacing = Vector2(112, 64)
SizeRange = Vector2($script:MinValue, $script:MaxValue)
MirrorChance = 0.5
HarvestItemId = "plant_fiber"
"@
    Write-NewText (Join-Path $entry.Folder "${species}Visual.tres") $visual $written
    Write-NewText (Join-Path $entry.Folder "$species.tres") $plant $written
}
#region Buttons
# =========================================================
# Preview names and dimensions without changing any files.
$previewButton.Add_Click({
    try {
        $plan = @(Get-Plan)
        $preview.Items.Clear()

        foreach ($entry in $plan) {
            [void]$preview.Items.Add($entry.Description)
        }

        $status.Text = "$($plan.Count) sprites ready. Sources preserved; RAW copies will be created."
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Check settings")
    }
})

# =========================================================
# Finish all resized copies before archiving the originals.

$processButton.Add_Click({
    $processButton.Enabled = $false
    $previewButton.Enabled = $false
    $written = New-Object 'System.Collections.Generic.List[string]'
    $createdFolders = New-Object 'System.Collections.Generic.List[string]'
    try {
        $plan = @(Get-Plan)
        [void][IO.Directory]::CreateDirectory($script:ArchiveRun)
        foreach ($entry in $plan) {
            [IO.File]::Copy($entry.Source, $entry.Archive, $false)
        }
        foreach ($entry in $plan) {
            $folder = Split-Path -Parent $entry.Output
            if (!(Test-Path -LiteralPath $folder)) {
                [void][IO.Directory]::CreateDirectory($folder)
                $createdFolders.Add($folder)
            }
            Save-Sprite $entry
            $written.Add($entry.Output)
        }
        foreach ($entry in $script:ResourcePlan) {
            Write-PlantResources $entry $written
        }
        $preview.Items.Clear()
        foreach ($entry in $plan) { [void]$preview.Items.Add($entry.Description) }
        $status.Text = "Complete: $($plan.Count) sprites; $($script:ResourcePlan.Count) species. Source files preserved."
        [void][System.Windows.Forms.MessageBox]::Show(
            $status.Text + "`r`nOriginal copies: " + $script:ArchiveRun +
            "`r`nResources generated only; biome spawn lists were not changed.", "Complete")
    } catch {
        foreach ($path in $written) {
            Remove-Item -LiteralPath $path -ErrorAction SilentlyContinue
        }
        foreach ($folder in $createdFolders) {
            if (@(Get-ChildItem -LiteralPath $folder -Force -ErrorAction SilentlyContinue).Count -eq 0) {
                Remove-Item -LiteralPath $folder -ErrorAction SilentlyContinue
            }
        }
        $status.Text = "Export stopped. Source files remain intact."
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Batch stopped")
    } finally {
        $processButton.Enabled = $true
        $previewButton.Enabled = $true
    }
})

#endregion

[void]$form.ShowDialog()
$form.Dispose()
