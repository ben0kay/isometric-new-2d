# BEN TOOLBOX - World Artwork Prompter
# Compatible with Windows PowerShell 5.1.
# Settings are stored beside this script as editable JSON.

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:SettingsPath = Join-Path $PSScriptRoot "WorldArtworkPrompter.settings.json"
$script:Fields = [ordered]@{}
$script:Random = New-Object System.Random
$script:Loading = $false
$script:ActiveCategory = ""

# ---------------------------------------------------------
# Convert JSON objects into editable dictionaries.
# ---------------------------------------------------------
function ConvertTo-Map {
    param($Value)

    if ($null -eq $Value) { return $null }

    if ($Value -is [System.Collections.IDictionary]) {
        $result = [ordered]@{}
        foreach ($key in $Value.Keys) {
            $result[$key] = ConvertTo-Map $Value[$key]
        }
        return $result
    }

    if ($Value -is [pscustomobject]) {
        $result = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $result[$property.Name] = ConvertTo-Map $property.Value
        }
        return $result
    }

    if (($Value -is [System.Collections.IEnumerable]) -and
        ($Value -isnot [string])) {
        $items = @(
            foreach ($item in $Value) {
                ConvertTo-Map $item
            }
        )
        return ,$items
    }

    return $Value
}

function New-Category {
    param(
        [string[]]$Subjects,
        [string[]]$Shapes,
        [string[]]$Materials,
        [string[]]$Details
    )

    return [ordered]@{
        Choices = [ordered]@{
            Subject = $Subjects
            Shape = $Shapes
            Material = $Materials
            Details = $Details
            MainColour = @(
                "Muted forest green"
                "Dark blue-green"
                "Pale brown"
                "Charcoal grey"
                "Muted purple"
                "Rust red"
                "Pale ivory"
            )
            AccentColour = @(
                "None"
                "Subtle cyan"
                "Muted pink"
                "Dull gold"
                "Deep violet"
                "Restrained glowing orange"
            )
            Scale = @(
                "Small, approximately ankle height relative to a person"
                "Medium, approximately knee height relative to a person"
                "Large, approximately waist height relative to a person"
                "Approximately person-sized"
                "Towering above a person"
                "Low and spreading across the ground"
            )
            Readability = @(
                "Clear silhouette with restrained fine detail"
                "Strong separation between major forms"
                "Detailed textures, with readable shapes at small sprite sizes"
                "Simple bold forms suitable for dense world population"
            )
        }
        Values = [ordered]@{}
        Locks = [ordered]@{}
        Notes = ""
    }
}

# ---------------------------------------------------------
# Initial settings. Afterwards the JSON file is authoritative.
# ---------------------------------------------------------
function New-DefaultSettings {
    $categories = [ordered]@{}

    $categories["Plant"] = New-Category `
        @("Small leafy plant", "Low shrub", "Alien fern", "Dead dry plant") `
        @("Tall narrow silhouette", "Low spreading growth", "Layered fan-shaped leaves", "Curled fronds") `
        @("Matte leaves with fine veins", "Weathered fibrous stems", "Waxy foliage", "Ribbed fleshy leaves") `
        @("Believable adaptation to the biome", "Water-storing structures", "Protective leaf edges", "Dormant seasonal growth")

    $categories["Grass"] = New-Category `
        @("Short grass tuft", "Tall grass clump", "Dry grass clump", "Alien reed cluster") `
        @("Loose upright blades", "Dense arching blades", "Sparse irregular tufts", "Broad ribbon-like blades") `
        @("Fine matte blades", "Dry fibrous blades", "Leathery ribbon surfaces", "Subtle ribbed texture") `
        @("Readable as a small repeated terrain sprite", "Irregular natural growth", "A few bent or broken blades", "Biome-adapted growth")

    $categories["Tree"] = New-Category `
        @("Tall thin alien tree", "Broad canopy tree", "Dead tree", "Twisted small tree") `
        @("Thin trunk with a wide flat canopy", "Layered branches", "Irregular branching silhouette", "Tall column-like growth") `
        @("Rough fibrous bark", "Smooth weathered bark", "Cracked mineral-like bark", "Leathery canopy leaves") `
        @("Believable load-bearing branches", "Visible trunk beneath the canopy", "Biome-adapted foliage", "Distinct canopy masses")

    $categories["Rock"] = New-Category `
        @("Small loose rock", "Large boulder", "Rock cluster", "Flat stone slab") `
        @("Angular fractured shape", "Rounded weathered mass", "Layered broken slab", "Irregular compact silhouette") `
        @("Rough basalt", "Layered sedimentary stone", "Weathered granite", "Porous volcanic stone") `
        @("Believable geological fractures", "Subtle mineral inclusions", "Eroded edges", "Restrained surface pitting")

    $categories["Surface patch"] = New-Category `
        @("Moss patch", "Dry vegetation patch", "Mineral crust", "Organic ground growth") `
        @("Low irregular spreading shape", "Broken islands of growth", "Thin branching coverage", "Compact uneven patch") `
        @("Soft moss texture", "Dry fibrous texture", "Cracked mineral texture", "Matte organic texture") `
        @("Soft irregular coverage edges", "Gaps revealing underlying terrain", "No solid rectangular tile", "Suitable for repeated terrain decoration")

    $categories["Ore deposit"] = New-Category `
        @("Exposed metal ore deposit", "Crystal-bearing rock", "Mineral outcrop", "Ore-bearing boulder") `
        @("Compact rocky mass with exposed seams", "Angular clustered formation", "Layered mineral outcrop", "Rock with sparse protruding crystals") `
        @("Rough host rock with metallic veins", "Dull mineral seams", "Translucent mineral crystals", "Oxidised metal-rich stone") `
        @("Ore clearly distinguishable from host rock", "Believable embedded mineral growth", "Restrained metallic highlights", "Visible harvestable mineral seams")

    return [ordered]@{
        Version = 1
        SharedChoices = [ordered]@{
            Biome = @(
                "Generic alien wilderness"
                "Swampy marsh"
                "Cold alien forest"
                "Dry arid lands"
                "Basalt flats"
                "Rocky highlands"
                "Frozen tundra"
                "Underground alien cavern"
            )
            Style = @(
                "Painted game sprite with convincing three-dimensional volume"
                "Detailed stylised survival-game artwork"
                "Semi-realistic alien world artwork"
                "Hand-painted artwork with clean readable shapes"
                "Pixel art with crisp edges and a limited palette"
            )
        }
        Categories = $categories
        Global = [ordered]@{
            Category = "Plant"
            Biome = "Generic alien wilderness"
            Style = "Painted game sprite with convincing three-dimensional volume"
            Variations = 4
            Reference = $false
            Transparent = $true
            NoCastShadow = $true
            RelatedVariations = $true
        }
    }
}

$script:Settings = New-DefaultSettings

if (Test-Path -LiteralPath $script:SettingsPath) {
    try {
        $loaded = ConvertTo-Map (
            Get-Content -LiteralPath $script:SettingsPath -Raw |
                ConvertFrom-Json
        )

        if ($loaded.Version -ne 1 -or
            $null -eq $loaded.Categories -or
            $null -eq $loaded.SharedChoices -or
            $null -eq $loaded.Global -or
            $loaded.Categories.Count -eq 0) {
            throw "The settings file has an unsupported or incomplete structure."
        }

        $script:Settings = $loaded
    }
    catch {
        [void][System.Windows.Forms.MessageBox]::Show(
            "Could not load settings.`r`n`r`n$($_.Exception.Message)`r`n`r`nThe tool will close without overwriting your file.",
            "World Artwork Prompter"
        )
        return
    }
}

# ---------------------------------------------------------
# Window and helpers.
# ---------------------------------------------------------
$form = New-Object System.Windows.Forms.Form
$form.Text = "BEN TOOLBOX - World Artwork Prompter"
$form.ClientSize = New-Object System.Drawing.Size(1220, 865)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(28, 30, 34)
$form.ForeColor = [System.Drawing.Color]::WhiteSmoke
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

function Add-Label {
    param([string]$Text, [int]$X, [int]$Y, [int]$Width)

    $control = New-Object System.Windows.Forms.Label
    $control.Text = $Text
    $control.Location = New-Object System.Drawing.Point($X, $Y)
    $control.Size = New-Object System.Drawing.Size($Width, 26)
    $form.Controls.Add($control)
    return $control
}

function Add-Button {
    param(
        [string]$Text, [int]$X, [int]$Y,
        [int]$Width = 80
    )

    $control = New-Object System.Windows.Forms.Button
    $control.Text = $Text
    $control.Location = New-Object System.Drawing.Point($X, $Y)
    $control.Size = New-Object System.Drawing.Size($Width, 30)
    $control.FlatStyle = "Flat"
    $control.BackColor = [System.Drawing.Color]::FromArgb(48, 53, 62)
    $control.ForeColor = [System.Drawing.Color]::WhiteSmoke
    $form.Controls.Add($control)
    return $control
}

function Add-Check {
    param([string]$Text, [int]$X, [int]$Y, [int]$Width)

    $control = New-Object System.Windows.Forms.CheckBox
    $control.Text = $Text
    $control.Location = New-Object System.Drawing.Point($X, $Y)
    $control.Size = New-Object System.Drawing.Size($Width, 28)
    $form.Controls.Add($control)
    return $control
}

[void](Add-Label "WORLD ARTWORK PROMPTER" 20 14 600)
[void](Add-Label "Type freely. Add stores a dropdown choice; Delete removes it." 20 46 700)

[void](Add-Label "Category" 20 88 105)
$category = New-Object System.Windows.Forms.ComboBox
$category.Location = New-Object System.Drawing.Point(130, 84)
$category.Size = New-Object System.Drawing.Size(320, 30)
$category.DropDownStyle = "DropDownList"
$category.Items.AddRange([object[]]@($script:Settings.Categories.Keys))
$form.Controls.Add($category)

function Get-ChoiceOwner {
    param([string]$Key)

    if ($Key -eq "Biome" -or $Key -eq "Style") {
        return $script:Settings.SharedChoices
    }

    return $script:Settings.Categories[$script:ActiveCategory].Choices
}

function Add-Field {
    param([string]$Key, [string]$Label, [int]$Y)

    [void](Add-Label $Label 20 ($Y + 4) 110)

    $input = New-Object System.Windows.Forms.ComboBox
    $input.Location = New-Object System.Drawing.Point(130, $Y)
    $input.Size = New-Object System.Drawing.Size(320, 30)
    $input.DropDownStyle = "DropDown"
    $form.Controls.Add($input)

    $lock = Add-Check "Lock" 455 $Y 62
    $add = Add-Button "Add" 520 $Y 54
    $delete = Add-Button "Delete" 580 $Y 65

    $add.Tag = $Key
    $delete.Tag = $Key

    $script:Fields[$Key] = [pscustomobject]@{
        Input = $input
        Lock = $lock
    }

    $add.Add_Click({
        $key = [string]$this.Tag
        $value = $script:Fields[$key].Input.Text.Trim()

        if ([string]::IsNullOrWhiteSpace($value)) {
            $status.Text = "Enter a phrase before clicking Add."
            return
        }

        $owner = Get-ChoiceOwner $key
        $existing = @($owner[$key])

        if ($existing -notcontains $value) {
            $owner[$key] = @($existing) + @($value)
            [void]$script:Fields[$key].Input.Items.Add($value)
        }

        $script:Fields[$key].Input.Text = $value
        if (Save-Settings) {
            $status.Text = "Saved '$value' to $key choices."
        }
    })

    $delete.Add_Click({
        $key = [string]$this.Tag
        $value = $script:Fields[$key].Input.Text.Trim()
        $owner = Get-ChoiceOwner $key
        $existing = @($owner[$key])

        if ($existing -notcontains $value) {
            $status.Text = "That phrase is not a saved dropdown choice."
            return
        }

        $remaining = @($existing | Where-Object { $_ -ine $value })
        $owner[$key] = $remaining
        $input = $script:Fields[$key].Input
        $input.Items.Clear()

        foreach ($item in $remaining) {
            [void]$input.Items.Add([string]$item)
        }

        $input.Text = ""
        if ($remaining.Count -gt 0) {
            $input.SelectedIndex = 0
        }

        if (Save-Settings) {
            $status.Text = "Deleted '$value' from $key choices."
        }
    })
}

Add-Field "Biome" "Biome" 130
Add-Field "Subject" "Subject" 172
Add-Field "MainColour" "Main colour" 214
Add-Field "AccentColour" "Accent colour" 256
Add-Field "Shape" "Silhouette" 298
Add-Field "Material" "Material" 340
Add-Field "Details" "Details" 382
Add-Field "Scale" "World scale" 424
Add-Field "Readability" "Readability" 466
Add-Field "Style" "Art style" 508

[void](Add-Label "Variations" 20 555 110)
$count = New-Object System.Windows.Forms.NumericUpDown
$count.Location = New-Object System.Drawing.Point(130, 551)
$count.Size = New-Object System.Drawing.Size(75, 30)
$count.Minimum = 1
$count.Maximum = 12
$form.Controls.Add($count)

$related = Add-Check "Variations belong to the same species / material family" 220 552 430
$reference = Add-Check "I will attach a reference image" 20 590 300
$transparent = Add-Check "Transparent background" 335 590 300
$noShadow = Add-Check "No cast shadow or ground plane" 20 623 320

[void](Add-Label "Extra requirements - remembered separately for each category" 20 663 630)
$notes = New-Object System.Windows.Forms.TextBox
$notes.Location = New-Object System.Drawing.Point(20, 691)
$notes.Size = New-Object System.Drawing.Size(625, 92)
$notes.Multiline = $true
$notes.ScrollBars = "Vertical"
$form.Controls.Add($notes)

[void](Add-Label "Generated prompt - editable before copying or exporting" 675 88 530)
$preview = New-Object System.Windows.Forms.TextBox
$preview.Location = New-Object System.Drawing.Point(675, 122)
$preview.Size = New-Object System.Drawing.Size(525, 661)
$preview.Multiline = $true
$preview.ScrollBars = "Vertical"
$form.Controls.Add($preview)

$generate = Add-Button "Generate" 20 798 105
$randomise = Add-Button "Randomise + Generate" 135 798 185
$save = Add-Button "Save settings" 330 798 120
$openSettings = Add-Button "Open JSON" 460 798 115
$copy = Add-Button "Copy prompt" 675 798 120
$export = Add-Button "Export prompt" 805 798 135

$status = Add-Label "" 20 836 1180
$status.ForeColor = [System.Drawing.Color]::Silver

# ---------------------------------------------------------
# Capture, save and restore.
# ---------------------------------------------------------
function Capture-State {
    if ($script:Loading -or
        [string]::IsNullOrWhiteSpace($script:ActiveCategory)) {
        return
    }

    $entry = $script:Settings.Categories[$script:ActiveCategory]

    foreach ($key in $script:Fields.Keys) {
        $entry.Values[$key] = $script:Fields[$key].Input.Text
        $entry.Locks[$key] = [bool]$script:Fields[$key].Lock.Checked
    }

    $entry.Notes = $notes.Text

    $global = $script:Settings.Global
    $global.Category = $script:ActiveCategory
    $global.Biome = $script:Fields["Biome"].Input.Text
    $global.Style = $script:Fields["Style"].Input.Text
    $global.Variations = [int]$count.Value
    $global.Reference = [bool]$reference.Checked
    $global.Transparent = [bool]$transparent.Checked
    $global.NoCastShadow = [bool]$noShadow.Checked
    $global.RelatedVariations = [bool]$related.Checked
}

function Save-Settings {
    try {
        Capture-State
        $json = $script:Settings | ConvertTo-Json -Depth 20
        $temporary = "$script:SettingsPath.tmp"
        $utf8 = New-Object System.Text.UTF8Encoding($false)

        [System.IO.File]::WriteAllText($temporary, $json, $utf8)

        if (Test-Path -LiteralPath $script:SettingsPath) {
            [System.IO.File]::Replace(
                $temporary, $script:SettingsPath, $null
            )
        }
        else {
            [System.IO.File]::Move(
                $temporary, $script:SettingsPath
            )
        }

        return $true
    }
    catch {
        $status.Text = "Settings could not be saved: $($_.Exception.Message)"
        return $false
    }
}

function Load-Category {
    param([string]$Name)

    $script:Loading = $true
    try {
        $script:ActiveCategory = $Name
        $entry = $script:Settings.Categories[$Name]

        foreach ($key in $script:Fields.Keys) {
            $field = $script:Fields[$key]
            $owner = Get-ChoiceOwner $key
            $field.Input.Items.Clear()

            foreach ($value in @($owner[$key])) {
                if (![string]::IsNullOrWhiteSpace([string]$value)) {
                    [void]$field.Input.Items.Add([string]$value)
                }
            }

            $field.Input.Text = ""

            if ($key -eq "Biome" -or $key -eq "Style") {
                $field.Input.Text = [string]$script:Settings.Global[$key]
            }
            elseif ($entry.Values.Contains($key)) {
                $field.Input.Text = [string]$entry.Values[$key]
            }
            elseif ($field.Input.Items.Count -gt 0) {
                $field.Input.SelectedIndex = 0
            }

            if ($entry.Locks.Contains($key)) {
                $field.Lock.Checked = [bool]$entry.Locks[$key]
            }
            else {
                $field.Lock.Checked = (
                    $key -eq "Biome" -or
                    $key -eq "Style" -or
                    $key -eq "MainColour" -or
                    $key -eq "AccentColour"
                )
            }
        }

        $notes.Text = [string]$entry.Notes
    }
    finally {
        $script:Loading = $false
    }
}

function Get-FieldText {
    param([string]$Key)

    $value = $script:Fields[$Key].Input.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Please fill in $Key."
    }
    return $value
}

# ---------------------------------------------------------
# Prompt construction.
# ---------------------------------------------------------
function New-ArtworkPrompt {
    $biome = Get-FieldText "Biome"
    $subject = Get-FieldText "Subject"
    $main = Get-FieldText "MainColour"
    $accent = Get-FieldText "AccentColour"
    $shape = Get-FieldText "Shape"
    $material = Get-FieldText "Material"
    $details = Get-FieldText "Details"
    $scale = Get-FieldText "Scale"
    $readability = Get-FieldText "Readability"
    $style = Get-FieldText "Style"
    $amount = [int]$count.Value

    $parts = New-Object "System.Collections.Generic.List[string]"

    if ($amount -eq 1) {
        $parts.Add(
            "Create one world sprite depicting: $subject. It is for the $biome biome of an alien sci-fi survival game."
        )
    }
    else {
        $parts.Add(
            "Create $amount distinct world sprite variations depicting: $subject. They are for the $biome biome of an alien sci-fi survival game."
        )

        if ($related.Checked) {
            $parts.Add(
                "Keep the variations recognisably part of the same species or material family, while giving each a distinct silhouette."
            )
        }
        else {
            $parts.Add(
                "Explore different designs while maintaining a consistent visual style, palette and world setting."
            )
        }
    }

    $parts.Add("Main colour palette: $main.")

    if ($accent -ieq "None") {
        $parts.Add("Use subtle tonal variation within the main palette.")
    }
    else {
        $parts.Add("Accent palette: $accent. Keep accents restrained.")
    }

    $parts.Add(
        "Silhouette and structure: $shape. Surface and material: $material. Additional design detail: $details."
    )
    $parts.Add(
        "Intended scale in the game world: $scale. Use this to guide proportions; do not include a person, ruler or scale diagram."
    )
    $parts.Add(
        "Sprite readability: $readability. Avoid tiny isolated details that disappear when the artwork is reduced."
    )
    $parts.Add(
        "Art direction: $style. Use an elevated three-quarter view suitable for an isometric 2D game. Keep perspective and visual scale consistent between variations. Use soft neutral lighting and enough internal shading to show form."
    )

    switch ($script:ActiveCategory) {
        "Plant" {
            $parts.Add(
                "Use believable alien biology adapted to the biome. Keep the plant's base easy to identify for placement on terrain."
            )
        }
        "Grass" {
            $parts.Add(
                "Design a self-contained grass tuft or clump with a clear rooted base. It should work when repeated densely across terrain."
            )
        }
        "Tree" {
            $parts.Add(
                "Keep the entire trunk, canopy and branch tips visible. Make the trunk's ground contact clear and use believable structural support."
            )
        }
        "Rock" {
            $parts.Add(
                "Use believable geological structure and weight. Keep a clear base for placement on terrain."
            )
        }
        "Surface patch" {
            $parts.Add(
                "Keep the asset low and spreading, viewed along the terrain plane. Use irregular edges. Do not add a raised platform, soil tile or rectangular backing."
            )
        }
        "Ore deposit" {
            $parts.Add(
                "Show an in-world mineral deposit with readable ore seams or crystals embedded in host material. It should be recognisable as a harvestable world resource."
            )
        }
    }

    if ($reference.Checked) {
        $parts.Add(
            "Use my attached reference image as the primary guide for rendering style, viewing angle, lighting and level of detail."
        )
    }

    if ($amount -gt 1) {
        $parts.Add(
            "Arrange all $amount sprites in one wide image with generous empty gaps between them. Keep every sprite fully visible, with no overlap or touching edges, so each can be cropped separately."
        )
    }
    else {
        $parts.Add(
            "Centre the complete asset with generous empty space around it. Keep every part inside the image."
        )
    }

    if ($transparent.Checked) {
        $parts.Add("Use a transparent background.")
    }
    else {
        $parts.Add(
            "Use a plain neutral background with strong separation from the artwork, suitable for later background removal."
        )
    }

    if ($noShadow.Checked) {
        $parts.Add(
            "Do not include a cast shadow, contact shadow or ground plane. Preserve shading inside the asset to show its volume."
        )
    }

    $parts.Add(
        "No scenery, pot, labels, text, border or watermark. Produce world artwork suitable for placement directly in the game."
    )

    if (![string]::IsNullOrWhiteSpace($notes.Text)) {
        $parts.Add("Additional requirements: $($notes.Text.Trim())")
    }

    return ($parts -join "`r`n`r`n")
}

function Update-Prompt {
    try {
        $preview.Text = New-ArtworkPrompt
        if (Save-Settings) {
            $status.Text = "Prompt generated. Settings saved."
        }
    }
    catch {
        $status.Text = $_.Exception.Message
    }
}

# ---------------------------------------------------------
# Actions.
# ---------------------------------------------------------
$category.Add_SelectedIndexChanged({
    if ($script:Loading) { return }

    Capture-State
    Load-Category ([string]$category.SelectedItem)
    Update-Prompt
})

$generate.Add_Click({ Update-Prompt })

$randomise.Add_Click({
    foreach ($key in $script:Fields.Keys) {
        $field = $script:Fields[$key]

        if (!$field.Lock.Checked -and $field.Input.Items.Count -gt 0) {
            $field.Input.SelectedIndex = $script:Random.Next(
                $field.Input.Items.Count
            )
        }
    }
    Update-Prompt
})

$save.Add_Click({
    if (Save-Settings) {
        $status.Text = "Settings saved."
    }
})

$openSettings.Add_Click({
    if (Save-Settings) {
        try {
            Start-Process -FilePath "notepad.exe" `
                -ArgumentList ('"{0}"' -f $script:SettingsPath)
            $status.Text = "Close the prompter before editing JSON; reopen it to load your changes."
        }
        catch {
            $status.Text = "Could not open settings: $($_.Exception.Message)"
        }
    }
})

$copy.Add_Click({
    try {
        if ([string]::IsNullOrWhiteSpace($preview.Text)) {
            throw "Generate a prompt first."
        }
        [System.Windows.Forms.Clipboard]::SetText($preview.Text)
        $status.Text = "Prompt copied."
    }
    catch {
        $status.Text = "Could not copy: $($_.Exception.Message)"
    }
})

$export.Add_Click({
    $dialog = New-Object System.Windows.Forms.SaveFileDialog
    try {
        if ([string]::IsNullOrWhiteSpace($preview.Text)) {
            throw "Generate a prompt first."
        }

        $dialog.Filter = "Text file (*.txt)|*.txt|Markdown file (*.md)|*.md"
        $dialog.FileName = "ArtworkPrompt-$($script:ActiveCategory.Replace(' ', ''))"
        $dialog.DefaultExt = "txt"

        if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
            $utf8 = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText(
                $dialog.FileName, $preview.Text, $utf8
            )
            $status.Text = "Prompt exported."
        }
    }
    catch {
        $status.Text = "Could not export: $($_.Exception.Message)"
    }
    finally {
        $dialog.Dispose()
    }
})

$form.Add_FormClosing({
    if (!(Save-Settings)) {
        $answer = [System.Windows.Forms.MessageBox]::Show(
            "Your settings could not be saved.`r`n`r`nClose anyway?",
            "World Artwork Prompter",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning
        )

        if ($answer -ne [System.Windows.Forms.DialogResult]::Yes) {
            $_.Cancel = $true
        }
    }
})

# ---------------------------------------------------------
# Restore global controls and open.
# ---------------------------------------------------------
$global = $script:Settings.Global
$count.Value = [decimal][Math]::Max(
    1, [Math]::Min(12, [int]$global.Variations)
)
$reference.Checked = [bool]$global.Reference
$transparent.Checked = [bool]$global.Transparent
$noShadow.Checked = [bool]$global.NoCastShadow
$related.Checked = [bool]$global.RelatedVariations

$initial = [string]$global.Category
if (!$script:Settings.Categories.Contains($initial)) {
    $initial = [string]@($script:Settings.Categories.Keys)[0]
}

$script:Loading = $true
$category.SelectedItem = $initial
$script:Loading = $false

Load-Category $initial
Update-Prompt

[void]$form.ShowDialog()
$form.Dispose()