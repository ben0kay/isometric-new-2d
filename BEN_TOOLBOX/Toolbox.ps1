# Main menu for BEN's development tools.
# Tools run in separate PowerShell processes so their variables stay isolated.
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

#region Configuration
$script:ToolboxRoot = $PSScriptRoot
$script:PowerShellExe = Join-Path $env:SystemRoot `
    "System32\WindowsPowerShell\v1.0\powershell.exe"

# Add future tools here. Their buttons become available when their scripts exist.
$tools = @(
    @{
        Name = "Sprite Batch Tool"
        Description = "Resize PNGs, rename variants, and archive originals into RAW."
        Path = "Tools\SpriteBatchTool\SpriteBatchTool.ps1"
    }
    @{
        Name = "Resource Checker"
        Description = "Check Godot resource paths and artwork connections. Coming next."
        Path = "Tools\ResourceChecker\ResourceChecker.ps1"
    }
)
#endregion

#region Launching
# =========================================================
# Launch one tool without sharing its variables with the main menu.
function Open-Tool([string]$relativePath) {
    try {
        $toolPath = Join-Path $script:ToolboxRoot $relativePath

        if (!(Test-Path -LiteralPath $toolPath -PathType Leaf)) {
            throw "Tool script not found:`r`n$toolPath"
        }

        $launch = New-Object System.Diagnostics.ProcessStartInfo
        $launch.FileName = $script:PowerShellExe
        $launch.Arguments = '-NoProfile -ExecutionPolicy Bypass -STA -File "' +
            $toolPath + '"'
        $launch.WorkingDirectory = Split-Path -Parent $toolPath
        $launch.UseShellExecute = $false
        $launch.CreateNoWindow = $true

        [void][System.Diagnostics.Process]::Start($launch)
        $status.Text = "Opened: " + [IO.Path]::GetFileNameWithoutExtension($toolPath)
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            $_.Exception.Message, "Unable to open tool")
    }
}
#endregion

#region Window
$form = New-Object System.Windows.Forms.Form
$form.Text = "BEN TOOLBOX"
$form.ClientSize = New-Object System.Drawing.Size(760, 450)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(24, 28, 35)
$form.ForeColor = [System.Drawing.Color]::FromArgb(235, 239, 245)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = "BEN TOOLBOX"
$title.Font = New-Object System.Drawing.Font(
    "Segoe UI", 23, [System.Drawing.FontStyle]::Bold)
$title.Location = New-Object System.Drawing.Point(25, 20)
$title.Size = New-Object System.Drawing.Size(700, 45)
$form.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = "Your game development tools, in one place."
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(165, 178, 195)
$subtitle.Location = New-Object System.Drawing.Point(28, 73)
$subtitle.Size = New-Object System.Drawing.Size(700, 25)
$form.Controls.Add($subtitle)

$toolList = New-Object System.Windows.Forms.FlowLayoutPanel
$toolList.Location = New-Object System.Drawing.Point(25, 115)
$toolList.Size = New-Object System.Drawing.Size(710, 245)
$toolList.FlowDirection = "TopDown"
$toolList.WrapContents = $false
$toolList.AutoScroll = $true
$form.Controls.Add($toolList)

$status = New-Object System.Windows.Forms.Label
$status.Text = "Ready. Tools open in their own windows."
$status.ForeColor = [System.Drawing.Color]::FromArgb(165, 178, 195)
$status.Location = New-Object System.Drawing.Point(28, 405)
$status.Size = New-Object System.Drawing.Size(700, 25)
$form.Controls.Add($status)
#endregion

#region Tool Buttons
# =========================================================
# Build a menu card for each configured tool.
function Add-ToolCard($tool) {
    $available = Test-Path -LiteralPath (
        Join-Path $script:ToolboxRoot $tool.Path) -PathType Leaf

    $card = New-Object System.Windows.Forms.Panel
    $card.Size = New-Object System.Drawing.Size(680, 100)
    $card.Margin = New-Object System.Windows.Forms.Padding(0, 0, 0, 12)
    $card.BackColor = [System.Drawing.Color]::FromArgb(35, 41, 51)

    $name = New-Object System.Windows.Forms.Label
    $name.Text = $tool.Name
    $name.Font = New-Object System.Drawing.Font(
        "Segoe UI", 12, [System.Drawing.FontStyle]::Bold)
    $name.Location = New-Object System.Drawing.Point(15, 12)
    $name.Size = New-Object System.Drawing.Size(510, 25)
    $card.Controls.Add($name)

    $description = New-Object System.Windows.Forms.Label
    $description.Text = $tool.Description
    $description.ForeColor = [System.Drawing.Color]::FromArgb(165, 178, 195)
    $description.Location = New-Object System.Drawing.Point(15, 43)
    $description.Size = New-Object System.Drawing.Size(510, 45)
    $card.Controls.Add($description)

    $button = New-Object System.Windows.Forms.Button
    $button.Text = if ($available) { "Open" } else { "Not installed" }
    $button.Enabled = $available
    $button.Location = New-Object System.Drawing.Point(545, 28)
    $button.Size = New-Object System.Drawing.Size(120, 40)
    $button.FlatStyle = "Flat"
    $button.BackColor = [System.Drawing.Color]::FromArgb(65, 105, 170)
    $button.ForeColor = [System.Drawing.Color]::White
    $button.Tag = $tool.Path
    $button.Add_Click({ Open-Tool $this.Tag })
    $card.Controls.Add($button)

    $toolList.Controls.Add($card)
}

foreach ($tool in $tools) {
    Add-ToolCard $tool
}
#endregion

[void]$form.ShowDialog()
$form.Dispose()