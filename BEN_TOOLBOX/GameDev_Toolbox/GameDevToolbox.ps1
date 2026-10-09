# Game development menu. Existing tools stay in this toolbox's Tools folder.
$tools = @(
    @{
        Name = "Sprite Splitter"
        Description = "Preview PNG sheets, edit cut lines, and export separate sprites."
        Path = "Tools\SpriteSplitter\SpriteSplitter.ps1"
    }
    @{
        Name = "Sprite Batch Tool"
        Description = "Resize PNGs, rename variants, and archive originals into RAW."
        Path = "Tools\SpriteBatchTool\SpriteBatchTool.ps1"
    }
    @{
        Name = "Resource Checker"
        Description = "Check Godot resource paths and artwork connections."
        Path = "Tools\ResourceChecker\ResourceChecker.ps1"
    }
    @{
        Name = "Plant Design Prompter"
        Description = "Build artwork prompts using biome, colour, shape, and style choices."
        Path = "Tools\PlantDesignPrompter\PlantDesignPrompter.ps1"
    }
        @{
        Name = "Lighting & Shadow Inspector"
        Description = "Read-only sun, lighting and shadow settings with refresh."
        Path = "Tools\LightingInspector\LightingInspector.ps1"
    }
)

& "$PSScriptRoot\..\ToolboxMenu.ps1" `
    -Title "GAME DEV TOOLBOX" -Root $PSScriptRoot -Tools $tools