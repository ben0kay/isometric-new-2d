# Project inspector submenu. Add future inspectors to this list.
$tools = @(
    @{
        Name = "Lighting & Shadow Inspector"
        Description = "Read-only sun, lighting and shadow settings with refresh."
        Path = "Tools\Inspectors\LightingInspector\LightingInspector.ps1"
    }
    @{
        Name = "File Size Inspector"
        Description = "Find the largest images, audio or other files in your project."
        Path = "Tools\Inspectors\FileSizeInspector\FileSizeInspector.ps1"
    }
)

$gameDevRoot = Split-Path -Parent $PSScriptRoot
$toolboxRoot = Split-Path -Parent $gameDevRoot

& (Join-Path $toolboxRoot "ToolboxMenu.ps1") `
    -Title "PROJECT INSPECTORS" -Root $gameDevRoot -Tools $tools
