# =========================================================
# Folder Backup
# Previews or copies a folder using optional exclusion filters.
# A separate process scans and copies so the window stays responsive.
# Creates dated backups, a summary, CSV file report, log, and settings.
# Originals are retained. Links are skipped. Permissions are not archived.
# =========================================================
param([string]$WorkerConfig)

$ErrorActionPreference = "Stop"

#region Shared Helpers
# =========================================================
# Formats byte counts for the interface and reports.
function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1TB) { return "{0:N2} TiB" -f ($Bytes / 1TB) }
    if ($Bytes -ge 1GB) { return "{0:N2} GiB" -f ($Bytes / 1GB) }
    if ($Bytes -ge 1MB) { return "{0:N2} MiB" -f ($Bytes / 1MB) }
    if ($Bytes -ge 1KB) { return "{0:N2} KiB" -f ($Bytes / 1KB) }
    return "$Bytes bytes"
}

# =========================================================
# Returns available space where Windows can determine it.
function Get-FreeBytes([string]$Path) {
    try {
        $root = [IO.Path]::GetPathRoot($Path)
        $drive = New-Object System.IO.DriveInfo($root)
        if ($drive.IsReady) { return [long]$drive.AvailableFreeSpace }
    }
    catch {}
    return $null
}
#endregion

#region Background Worker
# =========================================================
# Scans files, applies filters, and optionally copies the selected files.
function Invoke-BackupWorker([string]$ConfigPath) {
    $cfg = Get-Content -LiteralPath $ConfigPath -Raw | ConvertFrom-Json
    $started = Get-Date
    $report = $null
    $log = $null
    $plan = $null
    $planReader = $null
    $selected = 0L
    $selectedBytes = 0L
    $excluded = 0L
    $excludedFolders = 0L
    $links = 0L
    $scanErrors = 0L
    $copied = 0L
    $copiedBytes = 0L
    $copyErrors = 0L
    $free = $null
    $result = "FAILED"
    $detail = ""
    $planPath = Join-Path $cfg.Output "CopyPlan.jsonl"
    $utf8 = New-Object System.Text.UTF8Encoding($false)

    # =========================================================
    # Writes an escaped CSV row, including commas and line breaks.
    function Write-ReportRow($State, $Kind, $Relative, $Bytes, $Modified, $Reason) {
        $values = @($State, $Kind, $Relative, $Bytes, $Modified, $Reason)
        $escaped = foreach ($value in $values) {
            '"' + ([string]$value).Replace('"', '""') + '"'
        }
        $report.WriteLine($escaped -join ",")
    }

    # =========================================================
    # Writes a timestamped worker log entry.
    function Write-WorkerLog([string]$Text) {
        $log.WriteLine("[$((Get-Date).ToString('yyyy-MM-dd HH:mm:ss'))] $Text")
    }

    try {
        $report = New-Object System.IO.StreamWriter(
            (Join-Path $cfg.Output "FileReport.csv"), $false, $utf8)
        $log = New-Object System.IO.StreamWriter(
            (Join-Path $cfg.Output "Backup.log"), $false, $utf8)
        $log.AutoFlush = $true
        $plan = New-Object System.IO.StreamWriter($planPath, $false, $utf8)
        $report.WriteLine("Status,Kind,RelativePath,Bytes,LastModified,Reason")

        Write-WorkerLog "Starting $($cfg.Mode). Source: $($cfg.Source)"
        $base = $cfg.Source.TrimEnd('\') + '\'
        $stack = New-Object "System.Collections.Generic.Stack[string]"
        $stack.Push($cfg.Source)

        while ($stack.Count -gt 0) {
            $folderPath = $stack.Pop()
            $folderRelative = if ($folderPath -eq $cfg.Source) {
                ""
            } else {
                $folderPath.Substring($base.Length)
            }

            $plan.WriteLine((@{
                Kind = "Folder"
                Relative = $folderRelative
            } | ConvertTo-Json -Compress))

            try {
                $directory = New-Object System.IO.DirectoryInfo($folderPath)
                $entries = $directory.GetFileSystemInfos()
            }
            catch {
                $scanErrors++
                Write-ReportRow "SCAN_FAILED" "Folder" $folderRelative "" "" `
                    $_.Exception.Message
                Write-WorkerLog "Cannot scan $folderPath : $($_.Exception.Message)"
                continue
            }

            foreach ($entry in $entries) {
                $relative = $entry.FullName.Substring($base.Length)
                $isFolder = ($entry.Attributes -band [IO.FileAttributes]::Directory) -ne 0
                $isLink = ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
                $kind = if ($isFolder) { "Folder" } else { "File" }

                if ($isLink) {
                    $links++
                    Write-ReportRow "EXCLUDED" $kind $relative "" "" `
                        "Link or reparse point; target not scanned"
                    continue
                }

                if ($isFolder) {
                    if (@($cfg.Folders) -contains $entry.Name) {
                        $excludedFolders++
                        Write-ReportRow "EXCLUDED" "Folder" $relative "" "" `
                            "Excluded folder name; contents not scanned"
                    }
                    else {
                        $stack.Push($entry.FullName)
                    }
                    continue
                }

                try {
                    $bytes = [long]$entry.Length
                    $modified = $entry.LastWriteTime
                    $reasons = New-Object "System.Collections.Generic.List[string]"

                    foreach ($extension in @($cfg.Extensions)) {
                        if ($entry.Name.EndsWith(
                            $extension, [StringComparison]::OrdinalIgnoreCase)) {
                            $reasons.Add("Excluded extension: $extension")
                            break
                        }
                    }

                    if ($cfg.UseDate -and
                        $modified.Date -lt [datetime]::ParseExact(
                            $cfg.Cutoff, "yyyy-MM-dd",
                            [Globalization.CultureInfo]::InvariantCulture)) {
                        $reasons.Add("Last modified before $($cfg.Cutoff)")
                    }
                    if ($cfg.UseMin -and $bytes -lt [long]$cfg.MinBytes) {
                        $reasons.Add("Below minimum file size")
                    }
                    if ($cfg.UseMax -and $bytes -gt [long]$cfg.MaxBytes) {
                        $reasons.Add("Above maximum file size")
                    }

                    if ($reasons.Count -gt 0) {
                        $excluded++
                        Write-ReportRow "EXCLUDED" "File" $relative $bytes `
                            $modified.ToString("o") ($reasons -join "; ")
                        continue
                    }

                    $selected++
                    $selectedBytes += $bytes
                    $plan.WriteLine((@{
                        Kind = "File"
                        Relative = $relative
                        Bytes = $bytes
                        Modified = $modified.ToString("o")
                    } | ConvertTo-Json -Compress))
                }
                catch {
                    $scanErrors++
                    Write-ReportRow "SCAN_FAILED" "File" $relative "" "" `
                        $_.Exception.Message
                    Write-WorkerLog "Cannot inspect $relative : $($_.Exception.Message)"
                }
            }
        }

        $plan.Dispose()
        $plan = $null
        $free = Get-FreeBytes $cfg.Destination
        Write-WorkerLog "Selected: $selected files; $selectedBytes bytes."
        Write-WorkerLog "Excluded: $excluded files; $excludedFolders folder trees."

        if ($cfg.Mode -eq "Backup" -and
            $null -ne $free -and $selectedBytes -gt $free) {
            throw "Not enough destination space. No selected files were copied."
        }

        $contents = Join-Path $cfg.Output "Contents"
        $planReader = New-Object System.IO.StreamReader($planPath)

        while ($null -ne ($line = $planReader.ReadLine())) {
            $item = $line | ConvertFrom-Json

            if ($item.Kind -eq "Folder") {
                if ($cfg.Mode -eq "Backup") {
                    try {
                        $target = if ($item.Relative) {
                            Join-Path $contents $item.Relative
                        } else { $contents }
                        [void][IO.Directory]::CreateDirectory($target)
                    }
                    catch {
                        $copyErrors++
                        Write-ReportRow "FAILED" "Folder" $item.Relative "" "" `
                            $_.Exception.Message
                    }
                }
                continue
            }

            if ($cfg.Mode -eq "Preview") {
                Write-ReportRow "WOULD_COPY" "File" $item.Relative `
                    $item.Bytes $item.Modified "Passed all filters"
                continue
            }

            try {
                $from = Join-Path $cfg.Source $item.Relative
                $to = Join-Path $contents $item.Relative
                $current = Get-Item -LiteralPath $from -Force

                if ($current.PSIsContainer -or
                    (($current.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
                    throw "Source changed into a folder or link after scanning."
                }

                [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($to))
                [IO.File]::Copy($from, $to, $false)
                $actualBytes = (Get-Item -LiteralPath $to -Force).Length
                $copied++
                $copiedBytes += $actualBytes

                $reason = "Copied"
                if ($actualBytes -ne [long]$item.Bytes -or
                    $current.LastWriteTime.ToString("o") -ne $item.Modified) {
                    $reason = "Copied; source changed after scanning"
                }

                Write-ReportRow "COPIED" "File" $item.Relative $actualBytes `
                    $current.LastWriteTime.ToString("o") $reason
                Write-WorkerLog "COPIED $($item.Relative)"
            }
            catch {
                $copyErrors++
                Write-ReportRow "FAILED" "File" $item.Relative `
                    $item.Bytes $item.Modified $_.Exception.Message
                Write-WorkerLog "FAILED $($item.Relative): $($_.Exception.Message)"
            }
        }

        if ($cfg.Mode -eq "Preview") {
            $result = if ($scanErrors -gt 0) {
                "PREVIEW INCOMPLETE"
            } else { "PREVIEW COMPLETE" }
            $detail = "No source files were copied. Preview reports were generated."
        }
        else {
            $result = if ($scanErrors -gt 0 -or $copyErrors -gt 0) {
                "BACKUP INCOMPLETE"
            } else { "BACKUP COMPLETE" }
            $detail = "Review FileReport.csv for individual results."
        }
    }
    catch {
        $result = "FAILED OR INCOMPLETE"
        $detail = $_.Exception.Message
        if ($log) { Write-WorkerLog $detail }
    }
    finally {
        if ($planReader) { $planReader.Dispose() }
        if ($plan) { $plan.Dispose() }
        if ($report) { $report.Dispose() }
        if ($log) { $log.Dispose() }
    }

    $finished = Get-Date
    $freeText = if ($null -eq $free) { "Unknown" } else { Format-Size $free }
    $spaceCheck = if ($null -eq $free) { "Unknown" }
        elseif ($selectedBytes -gt $free) { "NOT ENOUGH SPACE" }
        else { "Enough estimated space" }

    $extensionsText = if (@($cfg.Extensions).Count) {
        @($cfg.Extensions) -join ", "
    } else { "None" }
    $foldersText = if (@($cfg.Folders).Count) {
        @($cfg.Folders) -join ", "
    } else { "None" }
    $dateText = if ($cfg.UseDate) { $cfg.Cutoff } else { "Disabled" }
    $minText = if ($cfg.UseMin) {
        "$(Format-Size $cfg.MinBytes) ($($cfg.MinBytes) bytes)"
    } else { "Disabled" }
    $maxText = if ($cfg.UseMax) {
        "$(Format-Size $cfg.MaxBytes) ($($cfg.MaxBytes) bytes)"
    } else { "Disabled" }

    $summary = @"
FOLDER BACKUP SUMMARY
Mode: $($cfg.Mode)
Status: $result
Started: $($started.ToString("o"))
Finished: $($finished.ToString("o"))
Duration: $([math]::Round(($finished - $started).TotalSeconds, 1)) seconds
Label: $($cfg.Label)

Source: $($cfg.Source)
Destination parent: $($cfg.Destination)
Report folder: $($cfg.Output)

FILTERS USED
Excluded extensions: $extensionsText
Excluded folder names: $foldersText
Exclude last modified before: $dateText
Minimum file size: $minText
Maximum file size: $maxText

SCAN RESULTS
Selected files: $selected
Estimated selected size: $(Format-Size $selectedBytes)
Estimated selected bytes: $selectedBytes
Individually excluded files: $excluded
Excluded folder trees: $excludedFolders
Skipped links/reparse points: $links
Scan errors: $scanErrors
Destination free space before copying: $freeText
Space check: $spaceCheck

COPY RESULTS
Files copied: $copied
Actual bytes copied: $copiedBytes
Actual size copied: $(Format-Size $copiedBytes)
Copy errors: $copyErrors

$detail

NOTES
- Enabled filters apply together.
- Date filtering uses local last-modified calendar date.
- File sizes use bytes; interface units are MiB (1,048,576 bytes).
- Excluded folders are not scanned, so their contents are not counted.
- Hidden files and empty directories are included unless excluded.
- Links/reparse points are skipped.
- Permissions and ownership are not archived.
- Originals and existing backups are retained.
- Estimates exclude disk allocation overhead and can change as files change.
- This is not a live snapshot; close programs changing source files.

FileReport.csv lists copied, excluded, failed, or preview-selected files.
Backup.log records worker activity.
BackupSettings.json records the parameters for this run.
"@

    Set-Content -LiteralPath (Join-Path $cfg.Output "Summary.txt") `
        -Value $summary -Encoding UTF8

    # Result.json is written last so the interface sees finished reports.
    @{
        Status = $result
        Selected = $selected
        Bytes = $selectedBytes
        Excluded = $excluded
        ExcludedFolders = $excludedFolders
        Copied = $copied
        Errors = $scanErrors + $copyErrors
        Space = $spaceCheck
        Free = $freeText
        Detail = $detail
    } | ConvertTo-Json | Set-Content `
        -LiteralPath (Join-Path $cfg.Output "Result.json") -Encoding UTF8

    Remove-Item -LiteralPath $planPath -Force -ErrorAction SilentlyContinue
}

if ($WorkerConfig) {
    Invoke-BackupWorker $WorkerConfig
    exit
}
#endregion

#region Interface Helpers
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Job = $null
$script:LastOutput = $null
$script:SettingsPath = Join-Path $env:LOCALAPPDATA "BenToolbox\BackupSettings.json"

$form = New-Object System.Windows.Forms.Form
$form.Text = "BEN TOOLBOX - Folder Backup"
$form.ClientSize = New-Object System.Drawing.Size(850, 745)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

# =========================================================
# Creates a control using shared position and size handling.
function Add-Control($Type, $Text, $X, $Y, $Width, $Height) {
    $control = New-Object ("System.Windows.Forms." + $Type)
    if ($null -ne $Text) { $control.Text = $Text }
    $control.Location = New-Object System.Drawing.Point($X, $Y)
    $control.Size = New-Object System.Drawing.Size($Width, $Height)
    $form.Controls.Add($control)
    return $control
}

# =========================================================
# Creates a folder input with a safe Browse handler.
function Add-FolderInput([string]$Caption, [int]$Y) {
    [void](Add-Control "Label" $Caption 22 $Y 790 24)
    $input = Add-Control "TextBox" "" 22 ($Y + 27) 680 28
    $browse = Add-Control "Button" "Browse" 713 ($Y + 25) 115 32
    $browse.Tag = $input

    $browse.Add_Click({
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        try {
            $dialog.Description = "Select a folder"
            $current = $this.Tag.Text.Trim()
            if ($current -and (Test-Path -LiteralPath $current -PathType Container)) {
                $dialog.SelectedPath = $current
            }
            if ($dialog.ShowDialog() -eq "OK") {
                $this.Tag.Text = $dialog.SelectedPath
            }
        }
        catch {
            [void][System.Windows.Forms.MessageBox]::Show(
                $_.Exception.Message, "Folder Picker")
        }
        finally { $dialog.Dispose() }
    })
    return $input
}

# =========================================================
# Creates an optional file-size filter.
function Add-SizeFilter([string]$Caption, [int]$Y) {
    $check = Add-Control "CheckBox" $Caption 22 $Y 365 28
    $number = Add-Control "NumericUpDown" $null 395 $Y 155 28
    $number.DecimalPlaces = 3
    $number.Maximum = 100000000
    $number.Value = 1
    $number.Enabled = $false
    [void](Add-Control "Label" "MiB" 563 ($Y + 4) 70 24)
    $check.Tag = $number
    $check.Add_CheckedChanged({ $this.Tag.Enabled = $this.Checked })
    return [pscustomobject]@{ Check = $check; Number = $number }
}
#endregion

#region Window Controls
$heading = Add-Control "Label" "FOLDER BACKUP" 22 15 800 40
$heading.Font = New-Object System.Drawing.Font(
    "Segoe UI", 18, [System.Drawing.FontStyle]::Bold)
[void](Add-Control "Label" `
    "Preview first, or create a new dated backup. Originals stay in place." `
    22 60 800 25)

$source = Add-FolderInput "Source folder" 98
$destination = Add-FolderInput "Destination folder" 168

[void](Add-Control "Label" "Optional backup label" 22 240 220 24)
$backupLabel = Add-Control "TextBox" "" 250 237 578 28

[void](Add-Control "Label" `
    "Exclude extensions - comma separated, e.g. .tmp, .zip" 22 282 800 24)
$extensions = Add-Control "TextBox" "" 22 309 806 28

[void](Add-Control "Label" `
    "Exclude folder names - comma separated, e.g. .godot, bin, obj, RAW" `
    22 350 806 24)
$folders = Add-Control "TextBox" "" 22 377 806 28

$useDate = Add-Control "CheckBox" `
    "Exclude files last modified before" 22 420 365 28
$cutoffDate = Add-Control "DateTimePicker" $null 395 420 200 28
$cutoffDate.Format = "Custom"
$cutoffDate.CustomFormat = "yyyy-MM-dd"
$cutoffDate.Enabled = $false
$useDate.Add_CheckedChanged({ $cutoffDate.Enabled = $this.Checked })

$minimum = Add-SizeFilter "Exclude files smaller than" 459
$maximum = Add-SizeFilter "Exclude files larger than" 498

$preview = Add-Control "Button" "Preview Backup" 22 548 175 40
$start = Add-Control "Button" "Create Backup" 210 548 175 40
$openFolder = Add-Control "Button" "Open Results" 398 548 140 40
$openSummary = Add-Control "Button" "Open Summary" 551 548 140 40
$openLog = Add-Control "Button" "Open Log" 704 548 124 40

$progress = Add-Control "ProgressBar" $null 22 607 806 15
$status = Add-Control "Label" "Ready. Settings are remembered automatically." `
    22 635 806 92
$openFolder.Enabled = $false
$openSummary.Enabled = $false
$openLog.Enabled = $false

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
#endregion

#region Settings And Validation
# =========================================================
# Reads an existing folder and rejects linked path components.
function Get-FolderPath([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        throw "Select both source and destination folders."
    }

    $item = Get-Item -LiteralPath $Text.Trim() -Force
    if (!$item.PSIsContainer -or $item.PSProvider.Name -ne "FileSystem") {
        throw "Select a filesystem folder."
    }

    $check = $item
    while ($null -ne $check) {
        if (($check.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Select a direct folder path rather than a linked folder: $($check.FullName)"
        }
        $check = $check.Parent
    }
    return $item.FullName
}

# =========================================================
# Checks for equal or nested folders.
function Test-Inside([string]$Child, [string]$Parent) {
    return ($Child.TrimEnd('\') + '\').StartsWith(
        ($Parent.TrimEnd('\') + '\'),
        [StringComparison]::OrdinalIgnoreCase)
}

# =========================================================
# Validates filters and captures the current settings.
function Get-Settings {
    $sourcePath = Get-FolderPath $source.Text
    $destinationPath = Get-FolderPath $destination.Text

    if ((Test-Inside $sourcePath $destinationPath) -or
        (Test-Inside $destinationPath $sourcePath)) {
        throw "Source and destination must be separate folders that do not contain one another."
    }

    $extensionList = @()
    foreach ($entry in ($extensions.Text -split '[,;\s]+')) {
        if (!$entry) { continue }
        $value = ($entry -replace '^\*\.', '').TrimStart('.')
        if ($value -notmatch '^[a-zA-Z0-9_-]+(\.[a-zA-Z0-9_-]+)*$') {
            throw "Invalid extension: $entry"
        }
        $extensionList += "." + $value.ToLowerInvariant()
    }

    $folderList = @()
    foreach ($entry in ($folders.Text -split '[,;\r\n]+')) {
        $value = $entry.Trim()
        if (!$value) { continue }
        if ($value -match '[\\/:*?"<>|]' -or $value -in @(".", "..")) {
            throw "Use folder names, not paths or wildcards: $value"
        }
        $folderList += $value
    }

    $label = $backupLabel.Text.Trim()
    if ($label -match '[\\/:*?"<>|]' -or $label.Length -gt 60) {
        throw "Keep the label under 61 characters without filename punctuation."
    }

    $minBytes = [long][decimal]::Ceiling($minimum.Number.Value * 1048576)
    $maxBytes = [long][decimal]::Floor($maximum.Number.Value * 1048576)
    if ($minimum.Check.Checked -and $maximum.Check.Checked -and
        $minBytes -gt $maxBytes) {
        throw "Minimum file size cannot exceed maximum file size."
    }

    return [ordered]@{
        Source = $sourcePath
        Destination = $destinationPath
        Label = $label
        Extensions = @($extensionList | Select-Object -Unique)
        Folders = @($folderList | Select-Object -Unique)
        UseDate = $useDate.Checked
        Cutoff = $cutoffDate.Value.ToString("yyyy-MM-dd")
        UseMin = $minimum.Check.Checked
        MinBytes = $minBytes
        MinMiB = $minimum.Number.Value
        UseMax = $maximum.Check.Checked
        MaxBytes = $maxBytes
        MaxMiB = $maximum.Number.Value
    }
}

# =========================================================
# Saves settings separately from project files.
function Save-Settings($Settings) {
    [void][IO.Directory]::CreateDirectory(
        [IO.Path]::GetDirectoryName($script:SettingsPath))
    $Settings | ConvertTo-Json -Depth 5 | Set-Content `
        -LiteralPath $script:SettingsPath -Encoding UTF8
}

# =========================================================
# Restores settings when the tool opens.
function Restore-Settings {
    if (!(Test-Path -LiteralPath $script:SettingsPath -PathType Leaf)) { return }
    try {
        $saved = Get-Content -LiteralPath $script:SettingsPath -Raw | ConvertFrom-Json
        $source.Text = $saved.Source
        $destination.Text = $saved.Destination
        $backupLabel.Text = $saved.Label
        $extensions.Text = @($saved.Extensions) -join ", "
        $folders.Text = @($saved.Folders) -join ", "
        $useDate.Checked = [bool]$saved.UseDate
        $cutoffDate.Value = [datetime]::ParseExact(
            $saved.Cutoff, "yyyy-MM-dd",
            [Globalization.CultureInfo]::InvariantCulture)
        $minimum.Check.Checked = [bool]$saved.UseMin
        $maximum.Check.Checked = [bool]$saved.UseMax
        $minimum.Number.Value = [decimal]$saved.MinMiB
        $maximum.Number.Value = [decimal]$saved.MaxMiB
    }
    catch { $status.Text = "Saved settings could not be fully restored." }
}
#endregion

#region Running And Reports
# =========================================================
# Updates controls while a worker runs.
function Set-Running([bool]$Running) {
    $preview.Enabled = !$Running
    $start.Enabled = !$Running
    $progress.Style = if ($Running) { "Marquee" } else { "Blocks" }

    $canOpen = !$Running -and $null -ne $script:LastOutput
    $openFolder.Enabled = $canOpen
    $openSummary.Enabled = $canOpen
    $openLog.Enabled = $canOpen
}

# =========================================================
# Starts a preview or backup using captured settings.
function Start-Job([string]$Mode) {
    if ($script:Job) { return }

    try {
        $settings = Get-Settings
        Save-Settings $settings

        $name = [IO.Path]::GetFileName($settings.Source.TrimEnd('\'))
        $name = $name -replace '[\\/:*?"<>|]', '_'
        if (!$name) { $name = "Folder" }

        $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
        $unique = [Guid]::NewGuid().ToString("N").Substring(0, 10)
        $labelPart = if ($settings.Label) { "_" + $settings.Label } else { "" }

        if ($Mode -eq "Preview") {
            $output = Join-Path ([IO.Path]::GetTempPath()) "BenBackupPreview_$unique"
        }
        else {
            $output = Join-Path $settings.Destination "${name}${labelPart}_${stamp}_${unique}"
        }

        if (Test-Path -LiteralPath $output) {
            throw "Output folder already exists. Try again."
        }
        [void][IO.Directory]::CreateDirectory($output)
        $settings["Mode"] = $Mode
        $settings["Output"] = $output

        $configPath = Join-Path $output "BackupSettings.json"
        $settings | ConvertTo-Json -Depth 5 | Set-Content `
            -LiteralPath $configPath -Encoding UTF8
        Set-Content -LiteralPath (Join-Path $output "Summary.txt") `
            -Value "$Mode IN PROGRESS. Completion has not been confirmed." `
            -Encoding UTF8

        $launch = New-Object System.Diagnostics.ProcessStartInfo
        $launch.FileName = Join-Path $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"
        $launch.Arguments = '-NoProfile -ExecutionPolicy Bypass -File "' +
            $PSCommandPath + '" -WorkerConfig "' + $configPath + '"'
        $launch.UseShellExecute = $false
        $launch.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($launch)
        $script:Job = [pscustomobject]@{
            Process = $process
            Output = $output
            Mode = $Mode
            Started = Get-Date
        }

        Set-Running $true
        $status.Text = "$Mode running. Scanning files and applying filters..."
        $timer.Start()
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Folder Backup")
    }
}

# =========================================================
# Collects results when the worker exits.
function Check-Job {
    if (!$script:Job) { return }
    $job = $script:Job

    if (!$job.Process.HasExited) {
        $seconds = [int]((Get-Date) - $job.Started).TotalSeconds
        $status.Text = "$($job.Mode) running... $seconds seconds.`r`nReports: $($job.Output)"
        return
    }

    $timer.Stop()
    $exitCode = $job.Process.ExitCode
    $job.Process.Dispose()
    $script:LastOutput = $job.Output
    $script:Job = $null

    try {
        $resultPath = Join-Path $job.Output "Result.json"
        if (!(Test-Path -LiteralPath $resultPath -PathType Leaf)) {
            throw "Worker stopped without a completed result (exit code $exitCode)."
        }

        $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
        $status.Text = "$($result.Status)`r`n" +
            "Selected: $($result.Selected) files / $(Format-Size $result.Bytes). " +
            "Excluded: $($result.Excluded) files and $($result.ExcludedFolders) folder trees.`r`n" +
            "Free space: $($result.Free). $($result.Space). " +
            "Copied: $($result.Copied). Errors: $($result.Errors)."
    }
    catch {
        $status.Text = $_.Exception.Message
        Add-Content -LiteralPath (Join-Path $job.Output "Summary.txt") `
            -Value "`r`nWORKER ERROR: $($_.Exception.Message)" -Encoding UTF8
    }

    Set-Running $false
}

# =========================================================
# Opens the latest results folder or a report.
function Open-Result([string]$Name) {
    if (!$script:LastOutput) { return }
    try {
        $path = if ($Name) {
            Join-Path $script:LastOutput $Name
        } else { $script:LastOutput }

        if (!(Test-Path -LiteralPath $path)) {
            throw "Report not available: $path"
        }

        if (!$Name) {
            Start-Process "explorer.exe" -ArgumentList ('"' + $path + '"')
        }
        else {
            Start-Process -FilePath "notepad.exe" `
                -ArgumentList ('"' + $path + '"')
        }
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Open Results")
    }
}
#endregion

#region Events
$preview.Add_Click({ Start-Job "Preview" })
$start.Add_Click({ Start-Job "Backup" })
$timer.Add_Tick({ Check-Job })
$openFolder.Add_Click({ Open-Result "" })
$openSummary.Add_Click({ Open-Result "Summary.txt" })
$openLog.Add_Click({ Open-Result "Backup.log" })

$form.Add_FormClosing({
    if ($script:Job) {
        $_.Cancel = $true
        $status.Text = "A job is running. Wait for completion before closing."
    }
})
#endregion

Restore-Settings
[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()