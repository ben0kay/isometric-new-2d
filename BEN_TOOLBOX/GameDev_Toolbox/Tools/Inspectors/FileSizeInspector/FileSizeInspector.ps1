# Read-only file size inspector. Windows PowerShell 5.1, STA.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Folder = $PSScriptRoot
while ($script:Folder -and !(Test-Path -LiteralPath (Join-Path $script:Folder "project.godot"))) {
    $script:Folder = Split-Path -Parent $script:Folder
}
if (!$script:Folder) { $script:Folder = $PSScriptRoot }
$script:Busy = $false

$form = New-Object System.Windows.Forms.Form
$form.Text = "File Size Inspector - Read Only"
$form.Size = New-Object System.Drawing.Size(1180, 760)
$form.MinimumSize = New-Object System.Drawing.Size(850, 500)
$form.StartPosition = "CenterScreen"
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = "Fill"
$layout.ColumnCount = 1
$layout.RowCount = 4
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle("Absolute", 76)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle("Absolute", 45)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle("Percent", 100)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle("Absolute", 50)))
$form.Controls.Add($layout)

$bar = New-Object System.Windows.Forms.FlowLayoutPanel
$bar.Dock = "Fill"
$layout.Controls.Add($bar, 0, 0)
function Add-Button([string]$text) {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $text
    $button.AutoSize = $true
    $bar.Controls.Add($button)
    return $button
}
$choose = Add-Button "Choose folder"
$refresh = Add-Button "Scan / Refresh"
$open = Add-Button "Open containing folder"
$types = New-Object System.Windows.Forms.ComboBox
$types.DropDownStyle = "DropDown"
$types.Width = 200
$types.Items.AddRange([object[]]@("All images", "All audio", "All files", "png", "jpg", "webp", "mp3", "wav", "ogg"))
$types.SelectedIndex = 0
$bar.Controls.Add($types)
$recursive = New-Object System.Windows.Forms.CheckBox
$recursive.Text = "Include subfolders"
$recursive.Checked = $true
$recursive.AutoSize = $true
$bar.Controls.Add($recursive)
$skip = New-Object System.Windows.Forms.CheckBox
$skip.Text = "Skip generated folders"
$skip.Checked = $true
$skip.AutoSize = $true
$bar.Controls.Add($skip)

$note = New-Object System.Windows.Forms.Label
$note.Dock = "Fill"
$note.Text = "Custom filter: png, wav or *.png; separate multiple types with commas. RGBA estimate = width x height x 4; excludes mipmaps and GPU compression."
$layout.Controls.Add($note, 0, 1)

$grid = New-Object System.Windows.Forms.DataGridView
$grid.Dock = "Fill"
$grid.ReadOnly = $true
$grid.AllowUserToAddRows = $false
$grid.AllowUserToDeleteRows = $false
$grid.AutoGenerateColumns = $false
$grid.RowHeadersVisible = $false
$grid.SelectionMode = "FullRowSelect"
$grid.MultiSelect = $false
$layout.Controls.Add($grid, 0, 2)
foreach ($spec in @(
    @("Name", "Filename", 220),
    @("Type", "Type", 70),
    @("Bytes", "Disk size (MB)", 120),
    @("Dimensions", "Dimensions", 125),
    @("RGBA", "RGBA estimate (MB)", 155),
    @("Folder", "Folder", 380)
)) {
    $column = New-Object System.Windows.Forms.DataGridViewTextBoxColumn
    $column.Name = $spec[0]
    $column.DataPropertyName = $spec[0]
    $column.HeaderText = $spec[1]
    $column.Width = $spec[2]
    $column.SortMode = "Automatic"
    if ($spec[0] -eq "Bytes" -or $spec[0] -eq "RGBA") {
        $column.DefaultCellStyle.Format = "N3"
    }
    [void]$grid.Columns.Add($column)
}
$status = New-Object System.Windows.Forms.Label
$status.Dock = "Fill"
$layout.Controls.Add($status, 0, 3)

function Get-Extensions {
    $filter = $types.Text.Trim().ToLowerInvariant()
    switch ($filter) {
        "all files" { return @() }
        "all images" { return @(".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".svg", ".dds", ".tga", ".exr", ".hdr", ".avif") }
        "all audio" { return @(".mp3", ".wav", ".ogg", ".flac", ".m4a", ".aac", ".opus", ".wma", ".aiff") }
        default {
            if (!$filter) { throw "Choose a file type or enter an extension." }
            $result = @()
            foreach ($part in ($filter -split '[,;\s]+')) {
                if (!$part) { continue }
                $extension = $part.TrimStart('*', '.')
                if ($extension -notmatch '^[a-z0-9]+$') {
                    throw "Use extensions such as png, mp3 or wav."
                }
                $result += "." + $extension
            }
            return $result
        }
    }
}

function Scan-Files {
    if ($script:Busy) { return }
    $script:Busy = $true
    $refresh.Enabled = $false
    $choose.Enabled = $false
    $types.Enabled = $false
    $recursive.Enabled = $false
    $skip.Enabled = $false
    $form.UseWaitCursor = $true
    try {
        $extensions = @(Get-Extensions)
        $table = New-Object System.Data.DataTable
        [void]$table.Columns.Add("Name", [string])
        [void]$table.Columns.Add("Type", [string])
        [void]$table.Columns.Add("Bytes", [double])
        [void]$table.Columns.Add("Dimensions", [string])
        [void]$table.Columns.Add("RGBA", [double])
        [void]$table.Columns.Add("Folder", [string])
        [void]$table.Columns.Add("FullPath", [string])
        $pending = New-Object 'System.Collections.Generic.Stack[string]'
        $pending.Push($script:Folder)
        $total = [long]0
        $errors = 0
        $visited = 0
        $excluded = @(".git", ".godot", ".vs", "bin", "obj", ".save-pass-backups")
        while ($pending.Count -gt 0) {
            $directory = $pending.Pop()
            try {
                $items = @(Get-ChildItem -LiteralPath $directory -Force -ErrorAction Stop)
            } catch {
                $errors++
                continue
            }
            foreach ($item in $items) {
                if ($form.IsDisposed) { return }
                $visited++
                if (($visited % 100) -eq 0) {
                    $status.Text = "Scanning... $($table.Rows.Count) matching files | $script:Folder"
                    [System.Windows.Forms.Application]::DoEvents()
                }
                if ($item.PSIsContainer) {
                    # Avoid following links/junctions into cycles or other drives.
                    if ($recursive.Checked -and
                        !($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -and
                        (!$skip.Checked -or $excluded -notcontains $item.Name)) {
                        $pending.Push($item.FullName)
                    }
                    continue
                }
                $extension = $item.Extension.ToLowerInvariant()
                if ($extensions.Count -gt 0 -and $extensions -notcontains $extension) { continue }
                $row = $table.NewRow()
                $row["Name"] = $item.Name
                $row["Type"] = $extension
                $row["Bytes"] = [double]$item.Length / 1MB
                $row["Folder"] = $item.DirectoryName
                $row["FullPath"] = $item.FullName
                $row["Dimensions"] = ""
                if ($extension -in @(".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff")) {
                    $image = $null
                    try {
                        $image = [Drawing.Image]::FromFile($item.FullName)
                        $row["Dimensions"] = "$($image.Width) x $($image.Height)"
                        $row["RGBA"] = ([double]$image.Width * $image.Height * 4) / 1MB
                    } catch {
                        $row["Dimensions"] = "Unreadable"
                    } finally {
                        if ($null -ne $image) { $image.Dispose() }
                    }
                }
                [void]$table.Rows.Add($row)
                $total += $item.Length
            }
        }
        $table.DefaultView.Sort = "Bytes DESC, Name ASC"
        $grid.DataSource = $table
        $status.Text = "{0} files | Total: {1:N2} MB | Skipped unreadable folders: {2} | {3}" -f $table.Rows.Count, ($total / 1MB), $errors, $script:Folder
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "File Size Inspector")
    } finally {
        $script:Busy = $false
        if (!$form.IsDisposed) {
            $refresh.Enabled = $true
            $choose.Enabled = $true
            $types.Enabled = $true
            $recursive.Enabled = $true
            $skip.Enabled = $true
            $form.UseWaitCursor = $false
        }
    }
}
$refresh.Add_Click({ Scan-Files })
$choose.Add_Click({
    $picker = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $picker.SelectedPath = $script:Folder
        if ($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $script:Folder = $picker.SelectedPath
            Scan-Files
        }
    } finally { $picker.Dispose() }
})
$open.Add_Click({
    if (!$grid.CurrentRow) { return }
    try {
        $path = [string]$grid.CurrentRow.DataBoundItem["FullPath"]
        if (Test-Path -LiteralPath $path) {
            Start-Process -FilePath "explorer.exe" -ArgumentList ('/select,"{0}"' -f $path)
        }
    } catch {
        [void][System.Windows.Forms.MessageBox]::Show($_.Exception.Message, "Open folder")
    }
})
$form.Add_Shown({ Scan-Files })
$form.Add_FormClosing({
    if ($script:Busy) {
        $_.Cancel = $true
        $status.Text = "Please wait for the current scan to finish."
    }
})
try { [void]$form.ShowDialog() } finally { $form.Dispose() }
