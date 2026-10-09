# =========================================================
# Plant Design Prompter
# Builds image prompts locally for your game artwork.
# No internet connection, API key, or API charges required.
# Paste the finished prompt into your preferred image tool.
# =========================================================

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

[System.Windows.Forms.Application]::EnableVisualStyles()

$script:Fields = [ordered]@{}
$script:Random = New-Object System.Random

# =========================================================
# Available choices. Add more entries to these lists anytime.
# =========================================================

$script:Choices = [ordered]@{
    Biome = @(
        "Volcanic lands"
        "Swampy marsh"
        "Cold alien forest"
        "Fungal wetlands"
        "Rocky highlands"
        "Frozen tundra"
        "Dark coastal lowlands"
        "Underground alien cavern"
    )

    Plant = @(
        "Small leafy plant"
        "Low shrub"
        "Tall shrub"
        "Grass clump"
        "Alien fern"
        "Flowering plant"
        "Fungus cluster"
        "Ground patch"
        "Tall thin tree"
    )

    MainColour = @(
        "Charcoal black"
        "Deep forest green"
        "Dark blue-green"
        "Muted purple"
        "Slate blue"
        "Ash grey"
        "Dark reddish brown"
        "Pale icy blue"
    )

    AccentColour = @(
        "Muted pink"
        "Glowing ember orange"
        "Subtle cyan"
        "Deep violet"
        "Muted lime green"
        "Pale silver"
        "Dark crimson"
        "None"
    )

    Shape = @(
        "Thin branching stems with wide leaves"
        "Dense rounded foliage"
        "Tall narrow silhouette"
        "Low spreading silhouette"
        "Spiky pointed leaves"
        "Curled fronds"
        "Layered fan-shaped leaves"
        "Irregular clustered growth"
    )

    Surface = @(
        "Matte leaves with fine visible veins"
        "Waxy foliage with restrained highlights"
        "Rough bark and leathery leaves"
        "Ash-dusted surfaces"
        "Cracked bark with subtle glowing fissures"
        "Soft mossy surfaces"
        "Ribbed fleshy leaves"
        "Weathered fibrous stems"
    )

    Style = @(
        "Painted game sprite with convincing three-dimensional volume"
        "Detailed stylised survival-game artwork"
        "Semi-realistic alien vegetation"
        "Hand-painted artwork with clean readable shapes"
        "Pixel art with crisp edges and a limited palette"
    )
}

# =========================================================
# Main window.
# =========================================================

$form = New-Object System.Windows.Forms.Form
$form.Text = "BEN TOOLBOX - Plant Design Prompter"
$form.ClientSize = New-Object System.Drawing.Size(1040, 720)
$form.FormBorderStyle = "FixedSingle"
$form.MaximizeBox = $false
$form.StartPosition = "CenterScreen"
$form.BackColor = [System.Drawing.Color]::FromArgb(28, 30, 34)
$form.ForeColor = [System.Drawing.Color]::WhiteSmoke
$form.Font = New-Object System.Drawing.Font("Segoe UI", 10)

$title = New-Object System.Windows.Forms.Label
$title.Text = "PLANT DESIGN PROMPTER"
$title.Location = New-Object System.Drawing.Point(22, 18)
$title.Size = New-Object System.Drawing.Size(600, 30)
$title.Font = New-Object System.Drawing.Font(
    "Segoe UI", 16, [System.Drawing.FontStyle]::Bold
)
$form.Controls.Add($title)

$intro = New-Object System.Windows.Forms.Label
$intro.Text = "Choose your settings, generate a prompt, then copy it into your image tool."
$intro.Location = New-Object System.Drawing.Point(24, 57)
$intro.Size = New-Object System.Drawing.Size(980, 26)
$form.Controls.Add($intro)

# =========================================================
# Creates an editable choice field with a randomisation lock.
# You can type your own values as well as use the dropdown.
# =========================================================

function Add-ChoiceField {
    param(
        [string]$Key,
        [string]$Label,
        [int]$Y
    )

    $caption = New-Object System.Windows.Forms.Label
    $caption.Text = $Label
    $caption.Location = New-Object System.Drawing.Point(24, ($Y + 5))
    $caption.Size = New-Object System.Drawing.Size(112, 25)
    $form.Controls.Add($caption)

    $choice = New-Object System.Windows.Forms.ComboBox
    $choice.Location = New-Object System.Drawing.Point(140, $Y)
    $choice.Size = New-Object System.Drawing.Size(330, 30)
    $choice.DropDownStyle = "DropDown"
    $choice.Items.AddRange([object[]]$script:Choices[$Key])
    $choice.SelectedIndex = 0
    $form.Controls.Add($choice)

    $locked = New-Object System.Windows.Forms.CheckBox
    $locked.Text = "Lock"
    $locked.Location = New-Object System.Drawing.Point(480, ($Y + 3))
    $locked.Size = New-Object System.Drawing.Size(65, 26)
    $form.Controls.Add($locked)

    $script:Fields[$Key] = [pscustomobject]@{
        Input = $choice
        Lock = $locked
    }
}

Add-ChoiceField "Biome" "Biome" 100
Add-ChoiceField "Plant" "Plant type" 143
Add-ChoiceField "MainColour" "Main colour" 186
Add-ChoiceField "AccentColour" "Accent colour" 229
Add-ChoiceField "Shape" "Shape" 272
Add-ChoiceField "Surface" "Surface" 315
Add-ChoiceField "Style" "Art style" 358

# Keep your selected biome and palette when randomising.
$script:Fields["Biome"].Lock.Checked = $true
$script:Fields["MainColour"].Lock.Checked = $true
$script:Fields["AccentColour"].Lock.Checked = $true

$countLabel = New-Object System.Windows.Forms.Label
$countLabel.Text = "Variations"
$countLabel.Location = New-Object System.Drawing.Point(24, 408)
$countLabel.Size = New-Object System.Drawing.Size(110, 25)
$form.Controls.Add($countLabel)

$count = New-Object System.Windows.Forms.NumericUpDown
$count.Location = New-Object System.Drawing.Point(140, 405)
$count.Size = New-Object System.Drawing.Size(80, 30)
$count.Minimum = 1
$count.Maximum = 12
$count.Value = 4
$form.Controls.Add($count)

$reference = New-Object System.Windows.Forms.CheckBox
$reference.Text = "I will attach a reference sprite"
$reference.Location = New-Object System.Drawing.Point(24, 448)
$reference.Size = New-Object System.Drawing.Size(460, 28)
$form.Controls.Add($reference)

$notesLabel = New-Object System.Windows.Forms.Label
$notesLabel.Text = "Extra requirements"
$notesLabel.Location = New-Object System.Drawing.Point(24, 488)
$notesLabel.Size = New-Object System.Drawing.Size(400, 24)
$form.Controls.Add($notesLabel)

$notes = New-Object System.Windows.Forms.TextBox
$notes.Location = New-Object System.Drawing.Point(24, 518)
$notes.Size = New-Object System.Drawing.Size(505, 90)
$notes.Multiline = $true
$notes.ScrollBars = "Vertical"
$form.Controls.Add($notes)

$previewLabel = New-Object System.Windows.Forms.Label
$previewLabel.Text = "Ready-to-paste prompt - you can edit this too"
$previewLabel.Location = New-Object System.Drawing.Point(565, 100)
$previewLabel.Size = New-Object System.Drawing.Size(450, 25)
$form.Controls.Add($previewLabel)

$preview = New-Object System.Windows.Forms.TextBox
$preview.Location = New-Object System.Drawing.Point(565, 135)
$preview.Size = New-Object System.Drawing.Size(450, 473)
$preview.Multiline = $true
$preview.ScrollBars = "Vertical"
$preview.Font = New-Object System.Drawing.Font("Segoe UI", 11)
$form.Controls.Add($preview)

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(24, 678)
$status.Size = New-Object System.Drawing.Size(990, 25)
$status.ForeColor = [System.Drawing.Color]::Silver
$form.Controls.Add($status)

# =========================================================
# Reads a choice and validates that it has a value.
# =========================================================

function Get-ChoiceText {
    param([string]$Key)

    $value = $script:Fields[$Key].Input.Text.Trim()

    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Please enter a value for $Key."
    }

    return $value
}

# =========================================================
# Converts the settings into a natural-language image prompt.
# =========================================================

function New-PlantPrompt {
    $biome = Get-ChoiceText "Biome"
    $plant = Get-ChoiceText "Plant"
    $main = Get-ChoiceText "MainColour"
    $accent = Get-ChoiceText "AccentColour"
    $shape = Get-ChoiceText "Shape"
    $surface = Get-ChoiceText "Surface"
    $style = Get-ChoiceText "Style"
    $amount = [int]$count.Value

    $parts = New-Object "System.Collections.Generic.List[string]"

    if ($amount -eq 1) {
        $parts.Add(
            "Create one $plant sprite for the $biome biome of an alien sci-fi survival game."
        )
    }
    else {
        $parts.Add(
            "Create $amount related variations of a $plant for the $biome biome of an alien sci-fi survival game. Give each variation a distinct silhouette while keeping them recognisably part of the same species."
        )
    }

    $parts.Add(
        "Use $main as the main colour."
    )

    if ($accent -ne "None") {
        $parts.Add(
            "Add restrained $accent accents without overwhelming the main colour."
        )
    }
    else {
        $parts.Add(
            "Use subtle tonal variation within the main colour palette."
        )
    }

    $parts.Add(
        "The plant's structure should feature $shape. Give it $surface. Adapt its growth and appearance to the biome, with believable alien biology."
    )

    $parts.Add(
        "Art direction: $style. Use an elevated three-quarter view suitable for an isometric 2D game, with consistent perspective, scale, and soft neutral lighting."
    )

    if ($reference.Checked) {
        $parts.Add(
            "Use my attached reference sprite as the primary guide for rendering style, viewing angle, level of detail, and lighting. Preserve that visual style while designing this new plant."
        )
    }

    if ($plant -eq "Ground patch") {
        $parts.Add(
            "Keep the growth low and spreading, suitable for placement directly over game terrain. Show the vegetation without a soil tile or a raised platform."
        )
    }

    if ($amount -gt 1) {
        $parts.Add(
            "Arrange all $amount sprites in one wide image, with generous empty gaps between them so they can be cropped into separate files. Keep every sprite fully visible, with no overlap."
        )
    }
    else {
        $parts.Add(
            "Centre the complete plant with generous empty space around it. Keep every leaf, branch, and stem inside the image."
        )
    }

    $parts.Add(
        "Use a transparent background with no scenery, ground plane, pot, cast shadow, labels, text, border, or watermark. Keep enough shading within the plant to show its form."
    )

    if (![string]::IsNullOrWhiteSpace($notes.Text)) {
        $parts.Add(
            "Additional requirements: $($notes.Text.Trim())"
        )
    }

    return ($parts -join "`r`n`r`n")
}

# =========================================================
# Generates the preview and reports validation errors.
# =========================================================

function Update-Prompt {
    try {
        $preview.Text = New-PlantPrompt
        $status.Text = "Prompt generated locally. Copy it when ready."
    }
    catch {
        $status.Text = $_.Exception.Message
    }
}

# =========================================================
# Randomises unlocked fields only.
# =========================================================

function Randomise-Choices {
    foreach ($key in $script:Fields.Keys) {
        $field = $script:Fields[$key]

        if (!$field.Lock.Checked) {
            $field.Input.SelectedIndex = $script:Random.Next(
                $field.Input.Items.Count
            )
        }
    }

    Update-Prompt
}

# =========================================================
# Creates a consistently styled action button.
# =========================================================

function Add-ActionButton {
    param(
        [string]$Text,
        [int]$X,
        [int]$Width
    )

    $button = New-Object System.Windows.Forms.Button
    $button.Text = $Text
    $button.Location = New-Object System.Drawing.Point($X, 626)
    $button.Size = New-Object System.Drawing.Size($Width, 38)
    $button.FlatStyle = "Flat"
    $button.BackColor = [System.Drawing.Color]::FromArgb(48, 53, 62)
    $button.ForeColor = [System.Drawing.Color]::WhiteSmoke
    $form.Controls.Add($button)

    return $button
}

$generateButton = Add-ActionButton "Generate Prompt" 24 170
$randomButton = Add-ActionButton "Randomise + Generate" 207 220
$copyButton = Add-ActionButton "Copy Prompt" 565 170

$generateButton.Add_Click({
    Update-Prompt
})

$randomButton.Add_Click({
    Randomise-Choices
})

$copyButton.Add_Click({
    try {
        if ([string]::IsNullOrWhiteSpace($preview.Text)) {
            throw "Generate a prompt first."
        }

        [System.Windows.Forms.Clipboard]::SetText($preview.Text)
        $status.Text = "Copied! Paste the prompt into your image tool."
    }
    catch {
        $status.Text = "Could not copy: $($_.Exception.Message)"
    }
})

# =========================================================
# Displays an initial prompt and starts the tool.
# =========================================================

Update-Prompt
[void]$form.ShowDialog()
$form.Dispose()