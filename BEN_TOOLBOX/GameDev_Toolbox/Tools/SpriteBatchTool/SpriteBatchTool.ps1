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
$form.ClientSize = New-Object System.Drawing.Size(720, 530)
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

$source = Add-Input "Source folder" "" 20 425
$destination = Add-Input "Destination folder" "" 60 425
Add-FolderPicker $source 20
Add-FolderPicker $destination 60

$scale = Add-Input "Scale multiplier" "0.25" 100 120
$prefix = Add-Input "Filename prefix" "Plant" 140 220
$start = Add-Input "Starting number" "1" 180 120
$padding = Add-Input "Number digits" "2" 220 120

$letterMode = New-Object System.Windows.Forms.CheckBox
$letterMode.Text = "Letter suffixes (01a, 01b, 01c)"
$letterMode.Location = New-Object System.Drawing.Point(350, 180)
$letterMode.Size = New-Object System.Drawing.Size(340, 25)
$form.Controls.Add($letterMode)

$preview = New-Object System.Windows.Forms.ListBox
$preview.Location = New-Object System.Drawing.Point(15, 265)
$preview.Size = New-Object System.Drawing.Size(685, 180)
$preview.HorizontalScrollbar = $true
$form.Controls.Add($preview)

$previewButton = New-Object System.Windows.Forms.Button
$previewButton.Text = "Preview"
$previewButton.Location = New-Object System.Drawing.Point(15, 465)
$previewButton.Size = New-Object System.Drawing.Size(120, 35)
$form.Controls.Add($previewButton)

$processButton = New-Object System.Windows.Forms.Button
$processButton.Text = "Process"
$processButton.Location = New-Object System.Drawing.Point(150, 465)
$processButton.Size = New-Object System.Drawing.Size(120, 35)
$form.Controls.Add($processButton)

$status = New-Object System.Windows.Forms.Label
$status.Text = "PNG files only. Originals move to RAW after processing."
$status.Location = New-Object System.Drawing.Point(285, 470)
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

    $index = 0
    foreach ($file in $files) {
        $suffix = ""
        if ($letterMode.Checked) {
            $suffix = Get-LetterSuffix $index
        }

        $newName = $namePrefix + $number.ToString("D$digits") + $suffix + ".png"
        $outputPath = Join-Path $destinationPath $newName
        $archivePath = Join-Path $script:RawFolder $file.Name

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
                Description = "$($file.Name) -> $newName   " +
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

        $status.Text = "$($plan.Count) sprites ready. Originals will move to RAW."
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Check settings")
    }
})

# =========================================================
# Finish all resized copies before archiving the originals.
$processButton.Add_Click({
    $saved = 0
    $moved = 0
    $processButton.Enabled = $false
    $previewButton.Enabled = $false

    try {
        $plan = @(Get-Plan)
        [void][IO.Directory]::CreateDirectory($script:RawFolder)
        $preview.Items.Clear()

        foreach ($entry in $plan) {
            Save-Sprite $entry
            $saved++
            [void]$preview.Items.Add("Saved: " + $entry.Description)
            $status.Text = "Saved $saved of $($plan.Count)"
            $form.Refresh()
        }

        foreach ($entry in $plan) {
            [IO.File]::Move($entry.Source, $entry.Archive)
            $moved++
            [void]$preview.Items.Add(
                "Archived original: " + [IO.Path]::GetFileName($entry.Source))
            $status.Text = "Archived $moved of $($plan.Count)"
            $form.Refresh()
        }

        $status.Text = "Complete: $saved outputs saved; $moved originals archived."

        [void][System.Windows.Forms.MessageBox]::Show(
            "Finished: $saved resized PNGs saved.`r`n" +
            "$moved originals moved into RAW.",
            "Complete")
    }
    catch {
        $status.Text = "Stopped: $saved outputs saved; $moved originals archived."

        [void][System.Windows.Forms.MessageBox]::Show(
            "Stopped: $saved outputs saved; $moved originals archived.`r`n" +
            "Any originals not archived remain in Source.`r`n`r`n" +
            $_.Exception.Message,
            "Batch stopped")
    }
    finally {
        $processButton.Enabled = $true
        $previewButton.Enabled = $true
    }
})
#endregion

[void]$form.ShowDialog()
$form.Dispose()