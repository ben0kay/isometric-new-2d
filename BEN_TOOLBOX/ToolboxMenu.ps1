# Shared toolbox menu. Each toolbox supplies its title, root, and tool list.
param(
    [string]$Title,
    [string]$Root,
    [object[]]$Tools
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

#region Launching
# =========================================================
# Opens a tool in its own PowerShell process.
function Open-Tool([string]$RelativePath) {
    try {
        $path = Join-Path $Root $RelativePath
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Script not found: $path"
        }

        $launch = New-Object System.Diagnostics.ProcessStartInfo
        $launch.FileName = Join-Path $env:SystemRoot `
            "System32\WindowsPowerShell\v1.0\powershell.exe"
        $launch.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + $path + '"'
        $launch.WorkingDirectory = Split-Path -Parent $path
        $launch.UseShellExecute = $false
        $launch.CreateNoWindow = $true
        [void][System.Diagnostics.Process]::Start($launch)
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Unable to open tool")
    }
}
#endregion

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = $Title
$form.ClientSize = New-Object System.Drawing.Size(760, 530)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(24, 28, 35)
$form.ForeColor = [System.Drawing.Color]::WhiteSmoke
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$heading = New-Object System.Windows.Forms.Label
$heading.Text = $Title
$heading.Font = New-Object System.Drawing.Font(
    "Segoe UI", 22, [System.Drawing.FontStyle]::Bold)
$heading.Location = New-Object System.Drawing.Point(25, 20)
$heading.Size = New-Object System.Drawing.Size(710, 50)
$form.Controls.Add($heading)

$list = New-Object System.Windows.Forms.FlowLayoutPanel
$list.Location = New-Object System.Drawing.Point(25, 90)
$list.Size = New-Object System.Drawing.Size(710, 415)
$list.FlowDirection = "TopDown"
$list.WrapContents = $false
$list.AutoScroll = $true
$form.Controls.Add($list)
#endregion

#region Cards
# =========================================================
# Adds a tool card and connects its launch button.
function Add-ToolCard($Tool) {
    $card = New-Object System.Windows.Forms.Panel
    $card.Size = New-Object System.Drawing.Size(680, 110)
    $card.Margin = New-Object System.Windows.Forms.Padding(0, 0, 0, 12)
    $card.BackColor = [System.Drawing.Color]::FromArgb(35, 41, 51)

    $name = New-Object System.Windows.Forms.Label
    $name.Text = $Tool.Name
    $name.Font = New-Object System.Drawing.Font(
        "Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
    $name.Location = New-Object System.Drawing.Point(15, 12)
    $name.Size = New-Object System.Drawing.Size(505, 27)
    $card.Controls.Add($name)

    $description = New-Object System.Windows.Forms.Label
    $description.Text = $Tool.Description
    $description.Location = New-Object System.Drawing.Point(15, 45)
    $description.Size = New-Object System.Drawing.Size(505, 55)
    $description.ForeColor = [System.Drawing.Color]::Silver
    $card.Controls.Add($description)

    $button = New-Object System.Windows.Forms.Button
    $button.Enabled = Test-Path -LiteralPath (Join-Path $Root $Tool.Path) -PathType Leaf
    $button.Text = if ($button.Enabled) { "Open" } else { "Missing" }
    $button.Location = New-Object System.Drawing.Point(545, 33)
    $button.Size = New-Object System.Drawing.Size(120, 40)
    $button.FlatStyle = "Flat"
    $button.BackColor = [System.Drawing.Color]::FromArgb(65, 105, 170)
    $button.Tag = $Tool.Path
    $button.Add_Click({ Open-Tool $this.Tag })
    $card.Controls.Add($button)
    $list.Controls.Add($card)
}

foreach ($tool in $Tools) { Add-ToolCard $tool }
#endregion

[void]$form.ShowDialog()
$form.Dispose()