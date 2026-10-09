# Creates a separate dated folder backup using Windows Robocopy.
# Copies files, hidden files, and empty directories without deleting originals.
# Writes a detailed copy log and a completion summary.
# Junctions are skipped to avoid copying linked directory trees.
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Job = $null
$script:LastBackup = $null

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = "BEN TOOLBOX - Folder Backup"
$form.ClientSize = New-Object System.Drawing.Size(800, 390)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = "FOLDER BACKUP"
$title.Font = New-Object System.Drawing.Font(
    "Segoe UI", 18, [System.Drawing.FontStyle]::Bold)
$title.Location = New-Object System.Drawing.Point(20, 18)
$title.Size = New-Object System.Drawing.Size(740, 40)
$form.Controls.Add($title)

$hint = New-Object System.Windows.Forms.Label
$hint.Text = "Each run creates a new dated backup. Existing backups and originals are kept."
$hint.Location = New-Object System.Drawing.Point(22, 65)
$hint.Size = New-Object System.Drawing.Size(755, 40)
$form.Controls.Add($hint)

# =========================================================
# Creates a folder input with its own Browse button.
function Add-FolderInput([string]$Label, [int]$Y) {
    $caption = New-Object System.Windows.Forms.Label
    $caption.Text = $Label
    $caption.Location = New-Object System.Drawing.Point(22, $Y)
    $caption.Size = New-Object System.Drawing.Size(740, 25)
    $form.Controls.Add($caption)

    $input = New-Object System.Windows.Forms.TextBox
    $input.Location = New-Object System.Drawing.Point(22, ($Y + 28))
    $input.Size = New-Object System.Drawing.Size(635, 28)
    $form.Controls.Add($input)

    $browse = New-Object System.Windows.Forms.Button
    $browse.Text = "Browse"
    $browse.Location = New-Object System.Drawing.Point(670, ($Y + 26))
    $browse.Size = New-Object System.Drawing.Size(105, 30)
    $browse.Tag = $input
    $browse.Add_Click({
        $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
        try {
            if (Test-Path -LiteralPath $this.Tag.Text -PathType Container) {
                $dialog.SelectedPath = $this.Tag.Text
            }
            if ($dialog.ShowDialog() -eq "OK") {
                $this.Tag.Text = $dialog.SelectedPath
            }
        }
        finally { $dialog.Dispose() }
    })
    $form.Controls.Add($browse)
    return $input
}

$source = Add-FolderInput "Source folder - everything inside this folder is backed up" 110
$destination = Add-FolderInput "Destination folder - dated backups are created here" 185

$start = New-Object System.Windows.Forms.Button
$start.Text = "Create Backup"
$start.Location = New-Object System.Drawing.Point(22, 265)
$start.Size = New-Object System.Drawing.Size(170, 40)
$form.Controls.Add($start)

$open = New-Object System.Windows.Forms.Button
$open.Text = "Open Last Backup"
$open.Location = New-Object System.Drawing.Point(205, 265)
$open.Size = New-Object System.Drawing.Size(180, 40)
$open.Enabled = $false
$form.Controls.Add($open)

$progress = New-Object System.Windows.Forms.ProgressBar
$progress.Location = New-Object System.Drawing.Point(22, 318)
$progress.Size = New-Object System.Drawing.Size(753, 15)
$form.Controls.Add($progress)

$status = New-Object System.Windows.Forms.Label
$status.Text = "Ready. Select a source and destination."
$status.Location = New-Object System.Drawing.Point(22, 343)
$status.Size = New-Object System.Drawing.Size(753, 40)
$form.Controls.Add($status)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500
#endregion

#region Validation
# =========================================================
# Resolves an existing filesystem folder to a full path.
function Get-FolderPath([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        throw "Select both a source and a destination folder."
    }

    $item = Get-Item -LiteralPath $Text.Trim() -Force
    if (!$item.PSIsContainer -or $item.PSProvider.Name -ne "FileSystem") {
        throw "Select a filesystem folder: $Text"
    }

    return $item.FullName
}

# =========================================================
# Checks whether one folder is inside or equal to another.
function Test-Inside([string]$Child, [string]$Parent) {
    $childPath = $Child.TrimEnd('\') + '\'
    $parentPath = $Parent.TrimEnd('\') + '\'
    return $childPath.StartsWith(
        $parentPath, [System.StringComparison]::OrdinalIgnoreCase)
}
#endregion

#region Backup
# =========================================================
# Writes the run details beside the backed-up contents.
function Write-Summary([string]$Result, [string]$Detail) {
    $job = $script:Job
    $text = @"
FOLDER BACKUP
Result: $Result
Started: $($job.Started.ToString("o"))
Updated: $((Get-Date).ToString("o"))
Source: $($job.Source)
Destination: $($job.Output)
Contents: $(Join-Path $job.Output "Contents")

$Detail

Copy settings:
- Files, hidden files, and empty directories included.
- File data, attributes, and timestamps copied.
- Junctions skipped; symbolic links copied as links.
- Permissions and ownership are not archived.
- Originals and previous backups are retained.

See Backup.log for file details and Robocopy totals.
This is a folder copy, not a system image or live snapshot.
"@
    Set-Content -LiteralPath (Join-Path $job.Output "Summary.txt") `
        -Value $text -Encoding UTF8
}

# =========================================================
# Validates paths and starts Robocopy without blocking the UI.
function Start-Backup {
    $process = $null
    try {
        $sourcePath = Get-FolderPath $source.Text
        $destinationPath = Get-FolderPath $destination.Text

        if ((Test-Inside $destinationPath $sourcePath) -or
            (Test-Inside $sourcePath $destinationPath)) {
            throw "Use separate folders: source and destination cannot contain one another."
        }

        $robocopy = Join-Path $env:SystemRoot "System32\robocopy.exe"
        if (!(Test-Path -LiteralPath $robocopy -PathType Leaf)) {
            throw "Windows Robocopy was not found."
        }

        $name = [IO.Path]::GetFileName($sourcePath.TrimEnd('\'))
        $name = $name -replace '[\\/:*?"<>|]', '_'
        if (!$name) { $name = "Folder" }

        $stamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
        $suffix = [Guid]::NewGuid().ToString("N")
        $output = Join-Path $destinationPath "${name}_${stamp}_${suffix}"
        [void][IO.Directory]::CreateDirectory($output)

        $contents = Join-Path $output "Contents"
        $log = Join-Path $output "Backup.log"
        $script:Job = [pscustomobject]@{
            Source = $sourcePath
            Output = $output
            Started = Get-Date
            Process = $null
        }

        Write-Summary "IN PROGRESS" "Backup started. Completion has not yet been confirmed."

        # A trailing slash immediately before a quote can confuse native arguments.
        $nativeSource = $sourcePath.TrimEnd('\') + '\.'
        $launch = New-Object System.Diagnostics.ProcessStartInfo
        $launch.FileName = $robocopy
        $launch.Arguments = '"{0}" "{1}" /E /COPY:DAT /DCOPY:DAT /XJ /SL /R:1 /W:1 /BYTES /NP /UNILOG:"{2}"' -f `
            $nativeSource, $contents, $log
        $launch.UseShellExecute = $false
        $launch.CreateNoWindow = $true

        $process = [System.Diagnostics.Process]::Start($launch)
        $script:Job.Process = $process
        $start.Enabled = $false
        $open.Enabled = $false
        $progress.Style = "Marquee"
        $status.Text = "Backing up... " + $sourcePath
        $timer.Start()
    }
    catch {
        $message = $_.Exception.Message
        if ($process -and !$process.HasExited) {
            $start.Enabled = $false
            $timer.Start()
        }
        else {
            if ($script:Job) {
                try { Write-Summary "FAILED" $message } catch {}
            }
            if ($process) { $process.Dispose() }
            $script:Job = $null
        }
        [void][System.Windows.Forms.MessageBox]::Show($message, "Backup")
    }
}

# =========================================================
# Checks completion and records Robocopy's result.
function Check-Backup {
    if (!$script:Job -or !$script:Job.Process.HasExited) { return }

    $timer.Stop()
    $job = $script:Job
    $code = $job.Process.ExitCode
    $job.Process.Dispose()

    $result = if ($code -ge 8) { "FAILED OR INCOMPLETE" }
        elseif ($code -ge 4) { "COMPLETED WITH WARNINGS" }
        else { "COMPLETED" }

    $script:LastBackup = $job.Output
    try {
        Write-Summary $result "Robocopy exit code: $code. Review Backup.log for totals, warnings, and any files that could not be copied."
        $status.Text = "$result - exit code $code. Log and summary saved."
    }
    catch {
        $status.Text = "$result - could not finish the summary: $($_.Exception.Message)"
    }

    $script:Job = $null
    $progress.Style = "Blocks"
    $progress.Value = 0
    $start.Enabled = $true
    $open.Enabled = $true
}
#endregion

#region Events
$start.Add_Click({ Start-Backup })
$timer.Add_Tick({ Check-Backup })

$open.Add_Click({
    try {
        Start-Process -FilePath "explorer.exe" `
            -ArgumentList ('"' + $script:LastBackup + '"')
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Open Backup")
    }
})

$form.Add_FormClosing({
    if ($script:Job) {
        $_.Cancel = $true
        $status.Text = "Backup is running. Wait for completion before closing."
    }
})
#endregion

[void]$form.ShowDialog()
$timer.Dispose()
$form.Dispose()