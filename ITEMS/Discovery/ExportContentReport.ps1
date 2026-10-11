# Runs Godot's real resource loader; does not parse or modify .tres content itself.
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GodotExe,
    [string]$ProjectRoot = (Join-Path $PSScriptRoot '../..'),
    [string]$OutputDirectory = (Join-Path $env:LOCALAPPDATA 'BenTools/ContentReports/isometric-new-2d')
)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath (Join-Path $ProjectRoot 'project.godot') -PathType Leaf)) {
    throw 'ProjectRoot must contain project.godot.'
}
if (!(Test-Path -LiteralPath $GodotExe -PathType Leaf)) { throw 'Supply the full path to your Godot .NET executable (console build recommended).' }
$GodotExe = [IO.Path]::GetFullPath($GodotExe)
# Build/import through Godot first. This command only runs the report scene.
& $GodotExe '--headless' '--path' $ProjectRoot 'res://ITEMS/Discovery/ContentReportExport.tscn' '--' "--content-report-dir=$OutputDirectory"
if ($LASTEXITCODE -ne 0) { throw "Content validation/export failed (exit $LASTEXITCODE). Inspect Godot output and any report at $OutputDirectory." }
Write-Host "Content report written to $OutputDirectory"
