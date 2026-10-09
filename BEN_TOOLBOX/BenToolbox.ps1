# Master menu for Ben's separate toolboxes.
$tools = @(
    @{
        Name = "Game Dev Toolbox"
        Description = "Sprites, resource checks, artwork prompts, and future game tools."
        Path = "GameDev_Toolbox\GameDevToolbox.ps1"
    }
    @{
        Name = "File Toolbox"
        Description = "Dated folder backups, logs, and future file management tools."
        Path = "File_Toolbox\FileToolbox.ps1"
    }
)

& "$PSScriptRoot\ToolboxMenu.ps1" `
    -Title "BEN TOOLBOX" -Root $PSScriptRoot -Tools $tools