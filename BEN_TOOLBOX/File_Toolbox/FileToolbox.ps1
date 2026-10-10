# File management menu.
$tools = @(
    @{
        Name = "Folder Backup"
        Description = "Copy a folder into a dated backup with a detailed log and summary."
        Path = "Tools\Backup\Backup.ps1"
    },
    @{
        Name = "Duplicate File Finder"
        Description = "Compare folders, preview image matches, and recycle verified duplicate files from Source A."
        Path = "Tools\DuplicateFileFinder\DuplicateFileFinder.ps1"
    }
)

& "$PSScriptRoot\..\ToolboxMenu.ps1" `
    -Title "FILE TOOLBOX" -Root $PSScriptRoot -Tools $tools
