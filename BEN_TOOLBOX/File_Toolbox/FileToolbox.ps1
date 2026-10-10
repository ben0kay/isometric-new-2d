# File management menu.
$tools = @(
    @{
        Name = "Folder Backup"
        Description = "Copy a folder into a dated backup with a detailed log and summary."
        Path = "Tools\Backup\Backup.ps1"
    },
    @{
        Name = "Duplicate File Finder"
        Description = "Compare files between two folders, with optional subfolder search and SHA-256 verification. Read-only."
        Path = "Tools\DuplicateFileFinder\DuplicateFileFinder.ps1"
    }
)

& "$PSScriptRoot\..\ToolboxMenu.ps1" `
    -Title "FILE TOOLBOX" -Root $PSScriptRoot -Tools $tools
