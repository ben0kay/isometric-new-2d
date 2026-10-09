# File management menu. Add future file tools to this list.
$tools = @(
    @{
        Name = "Folder Backup"
        Description = "Copy a folder into a dated backup with a detailed log and summary."
        Path = "Tools\Backup\Backup.ps1"
    }
)

& "$PSScriptRoot\..\ToolboxMenu.ps1" `
    -Title "FILE TOOLBOX" -Root $PSScriptRoot -Tools $tools