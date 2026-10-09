# Installs ONLY the profile-owned world/player save passes. No Git operations.
# Run from your project root with Godot closed. -Preview checks without writing.
[CmdletBinding()]
param([string]$ProjectRoot = (Get-Location).Path, [switch]$Preview)
$ErrorActionPreference = 'Stop'
$ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot)
if (!(Test-Path (Join-Path $ProjectRoot 'project.godot'))) { throw 'Choose the Godot project root.' }
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.ProcessName -like 'Godot*' }) {
    throw 'Close Godot before running this installer.'
}
$Utf8 = New-Object System.Text.UTF8Encoding($false)
function Normalize([string]$Text) { return $Text.TrimStart([char]0xFEFF).Replace("`r`n", "`n").TrimEnd("`r", "`n") }
function Digest([string]$Text) {
    $Hasher = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($Hasher.ComputeHash($Utf8.GetBytes((Normalize $Text)))).Replace('-', '').ToLowerInvariant() }
    finally { $Hasher.Dispose() }
}
$Changes = [ordered]@{}
$Expected = @{}
$Changes['UI/Menus/Main/MainMenu.cs'] = @'
// Artwork-led menu; profile identity and scene routing remain separate systems.
using Godot;
using System;

public partial class MainMenu : Control
{
    #region Configuration
    [ExportGroup("Layout")]
    [Export] public string GameTitle { get; set; } =
        "FRACTURED\nHORIZONS";

    [Export] public string Tagline { get; set; } =
        "SURVIVE  /  BUILD  /  EXPLORE  /  ENDURE";

    [Export] public float MenuWidth { get; set; } = 460f;
    [Export] public float ButtonHeight { get; set; } = 92f;
    [Export] public int ButtonSpacing { get; set; } = 14;
    [Export] public int OuterMargin { get; set; } = 40;

    [ExportGroup("Artwork")]
    [Export] public Texture2D BackgroundArtwork { get; set; }
    [Export] public Texture2D CampaignArtwork { get; set; }
    [Export] public Texture2D SandboxArtwork { get; set; }
    [Export] public Texture2D ProfileArtwork { get; set; }
    [Export] public Texture2D OptionsArtwork { get; set; }
    [Export] public Texture2D AboutArtwork { get; set; }
    [Export] public Texture2D ExitArtwork { get; set; }

    [ExportGroup("Destinations")]
    [Export(PropertyHint.File, "*.tscn")]
    public string CampaignScene { get; set; } =
        MenuNavigation.CampaignScene;

    [Export(PropertyHint.File, "*.tscn")]
    public string SandboxScene { get; set; } =
        MenuNavigation.SandboxScene;
    #endregion

    #region State
    private AcceptDialog _message;
    private HBoxContainer _layout;
    private Button _campaign;
    #endregion

    #region Lifecycle
    // =========================================================
    // F6 testing redirects to boot if no profile has been selected.
    public override void _Ready()
    {
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;

        if (ProfileStore.Selected == null)
        {
            Callable.From(() => MenuNavigation.Open(
                this, MenuNavigation.BootScene)).CallDeferred();
            return;
        }

        BuildUi();
        Resized += UpdateLayout;
        UpdateLayout();
        _campaign.GrabFocus();
    }
    #endregion

    #region Layout
    // =========================================================
    // Build the title, left artwork area, profile card and right menu.
    private void BuildUi()
    {
        if (BackgroundArtwork != null)
        {
            TextureRect image = new()
            {
                Texture = BackgroundArtwork,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode =
                    TextureRect.StretchModeEnum.KeepAspectCovered,
                MouseFilter = MouseFilterEnum.Ignore
            };

            AddChild(image);
            image.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        }

        MarginContainer margin = new();

        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride(
                "margin_" + side, OuterMargin);

        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _layout = new HBoxContainer();
        _layout.AddThemeConstantOverride("separation", 32);
        margin.AddChild(_layout);

        VBoxContainer left = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        left.AddThemeConstantOverride("separation", 18);
        _layout.AddChild(left);

        left.AddChild(UIButtonFactory.Label(GameTitle, 58));
        left.AddChild(UIButtonFactory.Label(Tagline, 17));
        left.AddChild(new Control
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        });

        PanelContainer profile = new();

        profile.AddThemeStyleboxOverride(
            "panel", UIButtonFactory.Box(
                new Color(0.025f, 0.075f, 0.10f, 0.9f),
                UIButtonFactory.Accent));

        left.AddChild(profile);

        VBoxContainer details = new();
        profile.AddChild(details);

        details.AddChild(UIButtonFactory.Label("ACTIVE PROFILE", 14));
        details.AddChild(UIButtonFactory.Label(
            ProfileStore.Selected.Name, 26));

        details.AddChild(UIButtonFactory.Label(
            "A stranger world. A new beginning.", 16));

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(MenuWidth, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        _layout.AddChild(scroll);

        VBoxContainer menu = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        menu.AddThemeConstantOverride("separation", ButtonSpacing);
        scroll.AddChild(menu);

        Label intro = UIButtonFactory.Label(
            "A 2D ISOMETRIC\nSURVIVAL EXPERIENCE", 18);

        intro.CustomMinimumSize = new Vector2(0, 88);
        menu.AddChild(intro);

        Button continueButton = AddMenu(menu, "Continue",
            () => OpenCampaign(true), CampaignArtwork);
        continueButton.Disabled = !CampaignStore.Exists(ProfileStore.Selected.Id);

        _campaign = AddMenu(menu, "New Campaign", NewCampaign, CampaignArtwork);
        menu.AddChild(UIButtonFactory.Label(
            "SAVE PASS 1–2: WORLD + PLAYER ONLY", 14));

        AddMenu(menu, "Sandbox",
            () => Launch(SandboxScene), SandboxArtwork);

        AddMenu(menu, "Change Profile",
            ChangeProfile, ProfileArtwork);

        AddMenu(menu, "Options",
            () => ShowMessage(
                "Options", "Options will be added in a later pass."),
            OptionsArtwork);

        AddMenu(menu, "About",
            () => ShowMessage(
                "About",
                "A science-fiction survival world.\n" +
                "About content is a placeholder."),
            AboutArtwork);

        AddMenu(menu, "Exit",
            () => GetTree().Quit(), ExitArtwork);

        menu.AddChild(UIButtonFactory.Label(
            "SAME SKY. DIFFERENT WORLD.", 14));

        _message = new AcceptDialog();
        AddChild(_message);
        UIButtonFactory.Apply(_message.GetOkButton());
    }

    // =========================================================
    // Keep menu buttons on the shared UI construction path.
    private Button AddMenu(
        VBoxContainer parent, string text,
        Action action, Texture2D artwork)
    {
        Button button = UIButtonFactory.CreateMenu(
            text, action, artwork, ButtonHeight);

        parent.AddChild(button);
        return button;
    }

    // =========================================================
    // Repaint the placeholder only when resizing.
    private void UpdateLayout()
    {
        if (_layout == null) return;

        Control scroll = _layout.GetChild<Control>(1);
        scroll.CustomMinimumSize = new Vector2(
            Mathf.Min(MenuWidth, Mathf.Max(280f, Size.X * 0.38f)), 0);

        QueueRedraw();
    }

    // =========================================================
    // Draw a lightweight backdrop until imported artwork is assigned.
    public override void _Draw()
    {
        if (BackgroundArtwork == null)
        {
            DrawRect(
                new Rect2(Vector2.Zero, Size),
                new Color("#06131c"));

            Vector2[] ridge =
            {
                new(0, Size.Y),
                new(0, Size.Y * 0.58f),
                new(Size.X * 0.17f, Size.Y * 0.32f),
                new(Size.X * 0.28f, Size.Y * 0.60f),
                new(Size.X * 0.42f, Size.Y * 0.43f),
                new(Size.X * 0.62f, Size.Y * 0.72f),
                new(Size.X * 0.72f, Size.Y)
            };

            DrawColoredPolygon(ridge, new Color("#102c38"));

            for (int i = 1; i < 8; i++)
                DrawLine(
                    new Vector2(0, Size.Y * i / 8f),
                    new Vector2(Size.X * 0.65f, Size.Y * i / 8f),
                    new Color(0.2f, 0.6f, 0.7f, 0.07f));
        }

        Rect2 border = new(
            new Vector2(12, 12),
            Size - new Vector2(24, 24));

        if (border.Size.X > 0 && border.Size.Y > 0)
            DrawRect(
                border,
                new Color(0.4f, 0.85f, 0.9f, 0.45f),
                false, 1f);
    }
    #endregion

    #region Actions
    // =========================================================
    // Campaign persistence will extend this launch action in the next pass.
    private void Launch(string scene)
    {
        try { MenuNavigation.Open(this, scene); }
        catch (Exception error)
        {
            ShowMessage("Could not launch", error.Message);
        }
    }

    // =========================================================
    // Release the selected identity while preserving profiles on disk.
    private void ChangeProfile()
    {
        PlayerProfile current = ProfileStore.Selected;
        ProfileStore.ClearSelection();

        try { MenuNavigation.Open(this, MenuNavigation.BootScene); }
        catch (Exception error)
        {
            ProfileStore.Select(current.Id);
            ShowMessage("Could not change profile", error.Message);
        }
    }

    // =========================================================
    // Reuse one popup for placeholders and recoverable loading errors.
    private void ShowMessage(string title, string text)
    {
        _message.Title = title;
        _message.DialogText = text;
        _message.PopupCentered(new Vector2I(480, 190));
    }
    #endregion
}
'@
$Changes['UI/Menus/Pause/PauseMenu.cs'] = @'
// Pauses gameplay while its own UI remains active; save support binds separately.
using Godot;
using System;

public partial class PauseMenu : CanvasLayer
{
    #region Configuration
    [ExportGroup("Layout")]
    [Export] public float PanelWidth { get; set; } = 440f;
    #endregion

    #region State
    private PlayerInput _controls;
    private InputModes _modes;
    private Control _screen;
    private Button _resume, _save;
    private AcceptDialog _message;
    private ConfirmationDialog _confirm;
    private Action _saveAction, _exitAction;
    private bool _open;
    private Input.MouseModeEnum _previousMouse;
    public bool IsOpen => _open;
    #endregion

    #region Setup
    // =========================================================
    // Attach once per player without editing every playable world scene.
    public static PauseMenu Attach(Player player, PlayerInput controls)
    {
        PauseMenu existing = player.GetNodeOrNull<PauseMenu>("PauseMenu");
        if (existing != null) return existing;

        PauseMenu menu = GD.Load<PackedScene>(
            "res://UI/Menus/Pause/PauseMenu.tscn")
            .Instantiate<PauseMenu>();

        menu.Name = "PauseMenu";
        menu._controls = controls;
        menu._modes = InputModes.For(player);
        player.AddChild(menu);
        return menu;
    }

    // =========================================================
    // Only this menu branch ignores the scene-tree pause state.
    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Layer = 200;
        BuildUi();
        _screen.Hide();
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Enable saving only when the real persistent saver is connected.
    public void BindSave(Action saveAction)
    {
        _saveAction = saveAction;
        if (_save == null) return;

        _save.Disabled = saveAction == null;
        _save.TooltipText = saveAction == null
            ? "Campaign saving is unavailable in this scene."
            : "Partial save: surface world, player, inventory and crafting only.";
    }
    #endregion

    #region Input and Pause
    // =========================================================
    // Let existing interfaces consume ESC before opening pause.
    public override void _UnhandledInput(InputEvent input)
    {
        if (_controls == null || !PlayerInput.IsPauseRequest(input))
            return;

        if (!_open && (!_modes.GameplayAllowed ||
            GetTree().Paused || GetViewport().GuiIsDragging()))
            return;

        GetViewport().SetInputAsHandled();

        if (_open) ResumeGame();
        else Open();
    }

    // =========================================================
    // Stop player actions, claim input, then pause the scene tree.
    private void Open()
    {
        _open = true;
        _previousMouse = Input.MouseMode;
        _modes.Push(this, PlayerInputMode.Pause);
        _controls.Suspend();

        Input.MouseMode = Input.MouseModeEnum.Visible;
        _screen.Show();
        GetTree().Paused = true;
        _resume.GrabFocus();
    }

    // =========================================================
    // Release ownership and prevent held-input retriggers.
    private void ResumeGame()
    {
        if (!_open) return;

        _message.Hide();
        _confirm.Hide();
        _exitAction = null;
        _screen.Hide();
        _modes.Release(this);
        _controls.Resume();

        Input.MouseMode = _previousMouse;
        _open = false;
        GetTree().Paused = false;
    }

    // =========================================================
    // Never leave the tree paused if the owning player is removed.
    public override void _ExitTree()
    {
        if (!_open) return;

        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);

        GetTree().Paused = false;
        Input.MouseMode = _previousMouse;
    }
    #endregion

    #region Layout
    // =========================================================
    // Build a blocking overlay with the existing shared button styles.
    private void BuildUi()
    {
        _screen = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        AddChild(_screen);
        _screen.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        ColorRect dim = new()
        {
            Color = new Color(0.015f, 0.035f, 0.05f, 0.78f),
            MouseFilter = Control.MouseFilterEnum.Stop
        };

        _screen.AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        CenterContainer centre = new();
        _screen.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(
            Control.LayoutPreset.FullRect);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0)
        };

        panel.AddThemeStyleboxOverride(
            "panel", UIButtonFactory.Box(
                UIButtonFactory.Ink, UIButtonFactory.Accent));

        centre.AddChild(panel);

        VBoxContainer contents = new();
        contents.AddThemeConstantOverride("separation", 12);
        panel.AddChild(contents);
        contents.AddChild(UIButtonFactory.Label("PAUSED", 30));

        _resume = AddButton(contents, "Resume", ResumeGame);
        _save = AddButton(contents, "Save Game", SaveGame);

        AddButton(contents, "Options", () => ShowMessage(
            "Options", "Options will be added later."));

        AddButton(contents, "Exit to Main Menu",
            () => ConfirmExit(false));

        AddButton(contents, "Exit Game",
            () => ConfirmExit(true));

        AddButton(contents, "About", () => ShowMessage(
            "About", "A science-fiction survival world.\n" +
            "About content is a placeholder."));

        _message = new AcceptDialog { Exclusive = true };
        AddChild(_message);
        UIButtonFactory.Apply(_message.GetOkButton());

        _confirm = new ConfirmationDialog
        {
            Exclusive = true,
            Title = "Leave game?"
        };

        AddChild(_confirm);
        _confirm.GetOkButton().Text = "LEAVE";
        UIButtonFactory.Apply(_confirm.GetOkButton());
        UIButtonFactory.Apply(_confirm.GetCancelButton());

        _confirm.Confirmed += CompleteExit;
        _confirm.Canceled += () => _exitAction = null;
        BindSave(_saveAction);
    }

    // =========================================================
    // Share button construction, focus handling and styling.
    private static Button AddButton(
        VBoxContainer parent, string text, Action action)
    {
        Button button = UIButtonFactory.Create(text, action);
        parent.AddChild(button);
        return button;
    }
    #endregion

    #region Actions
    // =========================================================
    // Invoke only an explicitly connected persistent saver.
    private void SaveGame()
    {
        if (_saveAction == null) return;

        try
        {
            _saveAction();
            ShowMessage("Partial campaign saved", "World and player saved to your selected profile.\n" +
                "Harvesting, world items, containers, buildings and entities are not saved yet.");
        }
        catch (Exception error)
        {
            ShowMessage("Save failed", error.Message);
        }
    }

    // =========================================================
    // Ask before leaving unsaved gameplay.
    private void ConfirmExit(bool quit)
    {
        _exitAction = quit
            ? () => GetTree().Quit()
            : () => MenuNavigation.Open(
                this, ProfileStore.Selected == null
                    ? MenuNavigation.BootScene
                    : MenuNavigation.MainScene);

        _confirm.DialogText =
            "Leave this game? Any unsaved progress will be lost.";

        _confirm.PopupCentered(new Vector2I(460, 180));
    }

    // =========================================================
    // Keep gameplay paused if returning to a menu fails.
    private void CompleteExit()
    {
        Action action = _exitAction;
        _exitAction = null;

        try { action?.Invoke(); }
        catch (Exception error)
        {
            ShowMessage("Could not leave", error.Message);
        }
    }

    // =========================================================
    // Show placeholders or errors while gameplay remains paused.
    private void ShowMessage(string title, string text)
    {
        _message.Title = title;
        _message.DialogText = text;
        _message.PopupCentered(new Vector2I(460, 180));
    }
    #endregion
}
'@
$Changes['SYSTEMS/Saving/CampaignData.cs'] = @'
// Versioned campaign data; runtime nodes and shared resources never enter JSON.
using System;
using System.Collections.Generic;

public sealed class CampaignData
{
    public int Version { get; set; } = 1;
    public string Coverage { get; set; } = "world-player-only";
    public string ProfileId { get; set; } = "";
    public string CampaignId { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public uint Seed { get; set; }
    public float SpawnX { get; set; }
    public float SpawnY { get; set; }
    public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = new();
    public Dictionary<string, string> Resources { get; set; } = new();
    public PlayerSaveData Player { get; set; } = new();
    public double WorldSeconds { get; set; }
    public Dictionary<string, System.Text.Json.JsonElement> Sections { get; set; } = new();
}

public sealed class PlayerSaveData
{
    public string Layer { get; set; } = "surface";
    public float X { get; set; }
    public float Y { get; set; }
    public int Health { get; set; }
    public float[] Stats { get; set; } = Array.Empty<float>();
    public Dictionary<string, List<ModifierSaveData>> Modifiers { get; set; } = new();
    public float[] Reserves { get; set; } = Array.Empty<float>();
    public string Backpack { get; set; } = "";
    public Dictionary<string, string> ItemResources { get; set; } = new();
    public string[] Tools { get; set; } = Array.Empty<string>();
    public int SelectedTool { get; set; }
    public List<StackSaveData> Bag { get; set; } = new();
    public List<HotbarSaveData> Hotbar { get; set; } = new();
    public int SelectedHotbar { get; set; }
    public List<CraftSaveData> Crafting { get; set; } = new();
    public double[] Survival { get; set; } = Array.Empty<double>();
    public bool SprintExhausted { get; set; }
}

public sealed class ModifierSaveData
{
    public int Stat { get; set; }
    public int Operation { get; set; }
    public float Amount { get; set; }
}
public sealed class StackSaveData
{
    public string Item { get; set; } = "";
    public int Count { get; set; }
}
public sealed class HotbarSaveData
{
    public int Area { get; set; } = -1;
    public int Index { get; set; }
    public string Item { get; set; } = "";
}
public sealed class CraftSaveData
{
    public string Recipe { get; set; } = "";
    public int Remaining { get; set; }
    public double Elapsed { get; set; }
}
'@
$Changes['SYSTEMS/Saving/PlayerSaveSections.cs'] = @'
// Save adapters stay beside the coordinator while each component owns its state.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public partial class PlayerStats
{
    // =========================================================
    // Preserve base attributes and named modifier sources without editing resources.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Stats = (float[])_base.Clone();
        data.Modifiers.Clear();
        foreach (var source in _sources)
            data.Modifiers[source.Key] = source.Value.Select(m => new ModifierSaveData
                { Stat = (int)m.Stat, Operation = (int)m.Operation, Amount = m.Amount }).ToList();
    }

    // =========================================================
    // Validate the entire attribute snapshot before changing calculated capacities.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Stats == null || data.Stats.Length != _base.Length ||
            data.Stats.Any(v => !float.IsFinite(v) || v < 0) ||
            data.Modifiers == null || data.Modifiers.Count > 128)
            throw new InvalidDataException("Invalid saved player attributes.");
        Dictionary<string, ModifierValue[]> restored = new();
        foreach (var source in data.Modifiers)
        {
            if (string.IsNullOrWhiteSpace(source.Key) || source.Value == null || source.Value.Count > 128)
                throw new InvalidDataException("Invalid modifier source.");
            restored[source.Key] = source.Value.Select(m =>
            {
                if (m == null || m.Stat < 0 || m.Stat >= _base.Length ||
                    !Enum.IsDefined(typeof(StatModifierOperation), m.Operation) ||
                    !float.IsFinite(m.Amount) ||
                    (m.Operation == (int)StatModifierOperation.Multiply && m.Amount < 0))
                    throw new InvalidDataException("Invalid saved modifier.");
                return new ModifierValue((PlayerStat)m.Stat,
                    (StatModifierOperation)m.Operation, m.Amount);
            }).ToArray();
        }
        Array.Copy(data.Stats, _base, _base.Length);
        _sources.Clear();
        foreach (var source in restored) _sources.Add(source.Key, source.Value);
        Recalculate();
    }
}

public partial class PlayerVitals
{
    // =========================================================
    // Copy current reserves independently from their calculated maximum values.
    public void CaptureSave(PlayerSaveData data) { data.Reserves = (float[])_current.Clone(); }

    // =========================================================
    // Restore after attributes so capacity signals cannot replace saved reserves.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Reserves == null || data.Reserves.Length != _current.Length ||
            data.Reserves.Any(v => !float.IsFinite(v) || v < 0) ||
            data.Health < 1 || data.Health > Health.MaxHealth)
            throw new InvalidDataException("Invalid saved vitality or reserves.");
        Health.RestoreState(data.Health);
        for (int i = 0; i < _current.Length; i++)
        {
            if (data.Reserves[i] > GetMaximum((PlayerReserve)i))
                throw new InvalidDataException("Saved reserve exceeds its capacity.");
            _current[i] = data.Reserves[i];
        }
        Changed?.Invoke();
    }
}

public partial class PlayerInventory
{
    // =========================================================
    // Copy physical slots; hotbar shortcuts are saved separately.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Backpack = Equipment.Backpack?.Id ?? "";
        data.ItemResources.Clear();
        void Remember(ItemDefinition item)
        {
            if (item == null || string.IsNullOrEmpty(item.ResourcePath)) return;
            if (!item.ResourcePath.StartsWith("res://", StringComparison.Ordinal) ||
                item.ResourcePath.Contains("::"))
                throw new InvalidDataException("Save requires external item resources or catalog IDs.");
            data.ItemResources[item.Id] = item.ResourcePath;
        }
        Remember(Equipment.Backpack);
        foreach (ItemDefinition tool in Equipment.CopyTools()) Remember(tool);
        data.Tools = Equipment.CopyTools().Select(t => t?.Id ?? "").ToArray();
        data.SelectedTool = Equipment.SelectedSlot;
        data.Bag.Clear();
        for (int i = 0; i < _storage.SlotCount; i++)
        {
            InventoryStack stack = _storage.Get(i);
            Remember(stack.Item);
            data.Bag.Add(new StackSaveData { Item = stack.Item?.Id ?? "", Count = stack.Count });
        }
    }

    // =========================================================
    // Stage exact contents before committing, avoiding collection or starter grants.
    public void RestoreSave(PlayerSaveData data, ItemCatalog catalog)
    {
        if (data.ItemResources == null || data.ItemResources.Count > 128)
            throw new InvalidDataException("Invalid saved item references.");
        ItemDefinition Resolve(string id)
        {
            ItemDefinition item = catalog.Get(id);
            if (item != null) return item;
            if (!data.ItemResources.TryGetValue(id, out string path) ||
                !path.StartsWith("res://", StringComparison.Ordinal) || path.Contains("::") ||
                !path.EndsWith(".tres", StringComparison.OrdinalIgnoreCase))
                return null;
            item = ResourceLoader.Load<ItemDefinition>(path);
            if (item?.Id != id) throw new InvalidDataException("Saved item identity does not match its resource.");
            return item;
        }
        BackpackDefinition pack = string.IsNullOrEmpty(data.Backpack) ? null
            : Resolve(data.Backpack) as BackpackDefinition
                ?? throw new InvalidDataException("Saved backpack is unavailable.");
        if (data.Bag == null || data.Bag.Count != PackSlots(pack) ||
            data.Tools == null || data.Tools.Length != Equipment.ToolSlotCount ||
            data.SelectedTool < 0 || data.SelectedTool >= Equipment.ToolSlotCount)
            throw new InvalidDataException("Saved equipment layout is invalid.");
        InventoryStorage staged = new(data.Bag.Count);
        for (int i = 0; i < data.Bag.Count; i++)
        {
            StackSaveData entry = data.Bag[i] ?? throw new InvalidDataException("Missing saved slot.");
            if (string.IsNullOrEmpty(entry.Item) && entry.Count == 0) continue;
            ItemDefinition item = Resolve(entry.Item)
                ?? throw new InvalidDataException($"Saved item '{entry.Item}' is unavailable.");
            if (entry.Count < 1 || entry.Count > Math.Max(1, item.MaxStack))
                throw new InvalidDataException("Invalid saved stack quantity.");
            staged.Set(i, new InventoryStack(item, entry.Count));
        }
        ItemDefinition[] tools = new ItemDefinition[data.Tools.Length];
        for (int i = 0; i < tools.Length; i++)
        {
            if (string.IsNullOrEmpty(data.Tools[i])) continue;
            tools[i] = Resolve(data.Tools[i]);
            if (tools[i]?.Attack == null)
                throw new InvalidDataException("Saved tool is unavailable or incompatible.");
        }
        _storage = staged;
        Equipment.ApplyContents(tools, pack);
        Equipment.Select(data.SelectedTool);
        Recalculate();
    }
}

public partial class PlayerHotbar
{
    // =========================================================
    // Preserve slot references and expected items without creating additional stacks.
    public void CaptureSave(PlayerSaveData data)
    {
        data.SelectedHotbar = SelectedSlot;
        data.Hotbar.Clear();
        for (int i = 0; i < SlotCount; i++)
        {
            InventoryAddress? address = GetAddress(i);
            data.Hotbar.Add(address.HasValue ? new HotbarSaveData
            {
                Area = (int)address.Value.Area, Index = address.Value.Index,
                Item = GetStack(i).Item.Id
            } : new HotbarSaveData());
        }
    }

    // =========================================================
    // Replace starter bindings only after physical inventory restoration.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Hotbar == null || data.Hotbar.Count != SlotCount ||
            data.SelectedHotbar < 0 || data.SelectedHotbar >= SlotCount)
            throw new InvalidDataException("Invalid saved hotbar.");
        InventoryAddress?[] bindings = new InventoryAddress?[SlotCount];
        ItemDefinition[] expected = new ItemDefinition[SlotCount];
        for (int i = 0; i < SlotCount; i++)
        {
            HotbarSaveData entry = data.Hotbar[i]
                ?? throw new InvalidDataException("Missing saved hotbar slot.");
            if (entry.Area == -1) continue;
            if (!Enum.IsDefined(typeof(InventoryArea), entry.Area))
                throw new InvalidDataException("Invalid hotbar address.");
            InventoryAddress address = new((InventoryArea)entry.Area, entry.Index);
            InventoryStack stack = _inventory.GetStack(address);
            if (!_inventory.HasAddress(address) || stack.IsEmpty || stack.Item.Id != entry.Item)
                throw new InvalidDataException("Saved hotbar references a missing item.");
            bindings[i] = address;
            expected[i] = stack.Item;
        }
        _bindings = bindings; _expected = expected;
        SelectedSlot = data.SelectedHotbar;
        Publish();
    }
}

public partial class PlayerCrafting
{
    // =========================================================
    // Preserve elapsed work without taking ingredients for unfinished jobs.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Crafting = _jobs.Select((job, index) => new CraftSaveData
        {
            Recipe = job.Recipe.Id, Remaining = job.Remaining,
            Elapsed = Math.Min(job.Recipe.DurationSeconds,
                job.Elapsed + (index == 0 ? _tick : 0))
        }).ToList();
    }

    // =========================================================
    // Rebuild the queue directly; ordinary queueing checks are for new requests.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Crafting == null || data.Crafting.Count > MaximumQueueEntries)
            throw new InvalidDataException("Invalid saved crafting queue.");
        List<CraftJob> staged = new();
        foreach (CraftSaveData entry in data.Crafting)
        {
            if (entry == null) throw new InvalidDataException("Missing crafting job.");
            CraftingRecipe recipe = Catalog.Recipes.FirstOrDefault(r => r.Id == entry.Recipe)
                ?? throw new InvalidDataException($"Saved recipe '{entry.Recipe}' is unavailable.");
            if (entry.Remaining < 1 || entry.Remaining > MaximumBatchSize ||
                !double.IsFinite(entry.Elapsed) || entry.Elapsed < 0 || entry.Elapsed > recipe.DurationSeconds)
                throw new InvalidDataException("Invalid saved crafting progress.");
            staged.Add(new CraftJob(recipe, entry.Remaining) { Elapsed = entry.Elapsed });
        }
        _jobs.Clear(); _jobs.AddRange(staged); _tick = 0;
        Status = "Crafting queue restored.";
    }
}

public partial class WorldClock
{
    // =========================================================
    // Restore the eclipse clock after its configuration has been initialized.
    public void RestoreSave(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new InvalidDataException("Invalid saved world time.");
        ElapsedSeconds = seconds;
        UpdateCycle();
    }
}

public partial class PlayerSurvival
{
    // =========================================================
    // Keep pending survival costs and recovery delays across a reload.
    public void CaptureSave(PlayerSaveData data)
    {
        data.Survival = new[] { _elapsed, _workRemaining, _foodPending,
            _waterPending, _fatiguePending, _staminaRecoveryDelay };
        data.SprintExhausted = _sprintExhausted;
    }

    // =========================================================
    // Restore accumulated costs without resuming a held sprint input.
    public void RestoreSave(PlayerSaveData data)
    {
        if (data.Survival == null || data.Survival.Length != 6 ||
            data.Survival.Any(v => !double.IsFinite(v) || v < 0))
            throw new InvalidDataException("Invalid saved survival timing.");
        _elapsed = data.Survival[0]; _workRemaining = data.Survival[1];
        _foodPending = data.Survival[2]; _waterPending = data.Survival[3];
        _fatiguePending = data.Survival[4]; _staminaRecoveryDelay = data.Survival[5];
        _sprintExhausted = data.SprintExhausted; IsSprinting = false;
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignRecipe.cs'] = @'
// Records exported world settings and detects incompatible generation resources.
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

public static class CampaignRecipe
{
    private static readonly string[] Owners =
        { "CONFIG", "Systems/WorldGenerator", "Systems/ChunkController" };

    // =========================================================
    // Capture exported settings before Ready callbacks derive terrain and cave seeds.
    public static void Capture(Node world, CampaignData data)
    {
        data.Settings.Clear(); data.Resources.Clear();
        foreach (string owner in Owners)
        {
            Node node = world.GetNode(owner);
            Dictionary<string, string> values = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) == 0 ||
                    (usage & PropertyUsageFlags.Storage) == 0) continue;
                string name = property["name"].AsString();
                Variant value = node.Get(name);
                if (value.VariantType == Variant.Type.Object)
                {
                    Resource resource = value.AsGodotObject() as Resource;
                    if (resource == null) { values[name] = "null"; continue; }
                    if (string.IsNullOrEmpty(resource.ResourcePath) || resource.ResourcePath.Contains("::"))
                        throw new InvalidDataException($"Save needs a separate resource file for {owner}/{name}.");
                    values[name] = "@resource:" + resource.ResourcePath;
                    Stamp(resource.ResourcePath, data.Resources);
                }
                else values[name] = GD.VarToStr(value);
            }
            data.Settings[owner] = values;
        }
        // A null generator catalog uses the surface definition inside LayerCatalog.
        Stamp("res://WORLD/Layers/WorldLayers.tres", data.Resources);
    }

    // =========================================================
    // Hash referenced settings and their dependencies without storing engine objects.
    private static void Stamp(string path, Dictionary<string, string> stamps)
    {
        if (stamps.ContainsKey(path) || path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)) return;
        if (!Godot.FileAccess.FileExists(path))
            throw new InvalidDataException($"Missing world resource: {path}");
        stamps[path] = Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(path)));
        foreach (string dependency in ResourceLoader.GetDependencies(path))
        {
            string resolved = dependency.Split("::").Last();
            if (resolved.StartsWith("res://", StringComparison.Ordinal)) Stamp(resolved, stamps);
        }
    }

    // =========================================================
    // Refuse changed resource recipes rather than silently rebuilding a different map.
    public static void Validate(CampaignData data)
    {
        if (data.Settings.Count != Owners.Length || data.Resources.Count == 0 || data.Resources.Count > 4096)
            throw new InvalidDataException("Missing or invalid generation recipe.");
        foreach (var stamp in data.Resources)
        {
            if (!stamp.Key.StartsWith("res://", StringComparison.Ordinal) ||
                !Godot.FileAccess.FileExists(stamp.Key) ||
                Convert.ToHexString(SHA256.HashData(Godot.FileAccess.GetFileAsBytes(stamp.Key))) != stamp.Value)
                throw new InvalidDataException("World generation resources changed since this save. " +
                    "Start a new campaign or restore the original resources. " + stamp.Key);
        }
    }

    // =========================================================
    // Apply settings to a detached scene so initialization sees the saved recipe.
    public static void Apply(Node world, CampaignData data)
    {
        Validate(data);
        foreach (string owner in Owners)
        {
            if (!data.Settings.TryGetValue(owner, out var values) || values == null || values.Count > 256)
                throw new InvalidDataException("Missing saved world settings.");
            Node node = world.GetNode(owner);
            HashSet<string> exported = new();
            foreach (var property in node.GetPropertyList())
            {
                var usage = (PropertyUsageFlags)property["usage"].AsInt64();
                if ((usage & PropertyUsageFlags.ScriptVariable) != 0 &&
                    (usage & PropertyUsageFlags.Storage) != 0)
                    exported.Add(property["name"].AsString());
            }
            foreach (var setting in values)
            {
                if (!exported.Contains(setting.Key) || setting.Value == null)
                    throw new InvalidDataException("Unsupported saved setting.");
                if (setting.Value.StartsWith("@resource:", StringComparison.Ordinal))
                {
                    string path = setting.Value.Substring(10);
                    if (!data.Resources.ContainsKey(path))
                        throw new InvalidDataException("Unvalidated resource reference.");
                    Resource resource = ResourceLoader.Load(path)
                        ?? throw new InvalidDataException("Saved resource is unavailable.");
                    node.Set(setting.Key, resource);
                }
                else
                {
                    Variant value = GD.StrToVar(setting.Value);
                    if (value.VariantType == Variant.Type.Object || value.VariantType == Variant.Type.Callable ||
                        value.VariantType == Variant.Type.Signal)
                        throw new InvalidDataException("Invalid saved world setting type.");
                    node.Set(setting.Key, value);
                }
            }
        }
        world.GetNode<ChunkController>("Systems/ChunkController").WorldSeed = data.Seed;
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignStore.cs'] = @'
// Keeps each profile's current campaign separate and replaces saves atomically.
using Godot;
using System;
using System.IO;
using System.Text.Json;

public static class CampaignStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static string RecoveryMessage { get; private set; } = "";

    // =========================================================
    // Resolve only validated stable IDs beneath Godot's user directory.
    public static string PathFor(string profile)
    {
        if (!Guid.TryParseExact(profile, "N", out _))
            throw new InvalidDataException("Invalid profile identity.");
        return ProjectSettings.GlobalizePath($"user://Profiles/{profile}/campaign.json");
    }

    // =========================================================
    // Keep a damaged save visible to Continue so it can report its actual error.
    public static bool Exists(string profile)
    {
        string path = PathFor(profile);
        return File.Exists(path) || File.Exists(path + ".bak");
    }

    // =========================================================
    // Validate ownership and the supported format before exposing a save.
    private static CampaignData Read(string path, string profile)
    {
        if (new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Campaign file exceeds this pass's size limit.");
        CampaignData data = JsonSerializer.Deserialize<CampaignData>(File.ReadAllText(path));
        if (data == null || data.Version != 1 || data.Coverage != "world-player-only" ||
            data.ProfileId != profile || !Guid.TryParseExact(data.CampaignId, "N", out _) ||
            data.Player == null || data.Settings == null || data.Resources == null ||
            data.Sections == null || data.Sections.Count != 0)
            throw new InvalidDataException("Unsupported or invalid campaign save.");
        return data;
    }

    // =========================================================
    // Recover a readable previous save without overwriting damaged files.
    public static CampaignData Load(string profile)
    {
        RecoveryMessage = "";
        string path = PathFor(profile);
        try { return Read(path, profile); }
        catch (Exception original)
        {
            try
            {
                CampaignData data = Read(path + ".bak", profile);
                RecoveryMessage = "Loaded the previous campaign backup.";
                return data;
            }
            catch
            {
                throw new IOException("Could not read the campaign or its backup. " +
                    "Existing files were preserved. " + original.Message, original);
            }
        }
    }

    // =========================================================
    // Flush all bytes before replacing the current save; preserve a valid backup.
    public static void Write(CampaignData data)
    {
        string path = PathFor(data.ProfileId), temporary = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(data, Json);
        using (FileStream stream = new(temporary, FileMode.Create,
            System.IO.FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(true);
        }
        Read(temporary, data.ProfileId);
        if (File.Exists(path))
        {
            bool valid = false;
            try { Read(path, data.ProfileId); valid = true; } catch { }
            File.Replace(temporary, path, valid ? path + ".bak" : null);
        }
        else File.Move(temporary, path);
        RecoveryMessage = "";
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignMenu.cs'] = @'
// Main-menu campaign actions stay separate from save storage and world loading.
using Godot;
using System;

public partial class MainMenu
{
    private ConfirmationDialog _newCampaignConfirm;

    // =========================================================
    // Confirm replacing this profile's single current-campaign slot on its next save.
    private void NewCampaign()
    {
        if (!CampaignStore.Exists(ProfileStore.Selected.Id)) { OpenCampaign(false); return; }
        if (_newCampaignConfirm == null)
        {
            _newCampaignConfirm = new ConfirmationDialog { Title = "New Campaign" };
            AddChild(_newCampaignConfirm);
            _newCampaignConfirm.Confirmed += () => OpenCampaign(false);
            UIButtonFactory.Apply(_newCampaignConfirm.GetOkButton());
            UIButtonFactory.Apply(_newCampaignConfirm.GetCancelButton());
        }
        _newCampaignConfirm.DialogText = "Start a fresh campaign? Your next Save Game " +
            "will replace this profile's current campaign slot. The existing save stays until then.";
        _newCampaignConfirm.PopupCentered(new Vector2I(540, 220));
    }

    // =========================================================
    // Report validation errors before replacing the menu with a gameplay scene.
    private void OpenCampaign(bool restore)
    {
        try { CampaignSession.Launch(this, restore); }
        catch (Exception error) { ShowMessage("Could not open campaign", error.Message); }
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignSession.cs'] = @'
// Coordinates the partial world/player save pass; later sections extend this owner.
using Godot;
using System;
using System.Diagnostics;
using System.IO;

public partial class CampaignSession : Node
{
    private CampaignData _data;
    private bool _restoring, _finished;
    private Node _world;
    private Player _player;
    private ChunkController _chunks;
    private WorldClock _clock;
    private ProcessModeEnum _objectsMode;
    private readonly Stopwatch _startup = new();

    // =========================================================
    // Instantiate a detached world and apply its recipe before Ready runs.
    public static void Launch(Node menu, bool continueCampaign)
    {
        PlayerProfile profile = ProfileStore.Selected
            ?? throw new InvalidOperationException("Select a profile first.");
        CampaignData data = continueCampaign ? CampaignStore.Load(profile.Id) : new CampaignData
        {
            ProfileId = profile.Id, CampaignId = Guid.NewGuid().ToString("N")
        };
        if (continueCampaign)
        {
            CampaignRecipe.Validate(data);
            if (data.Player.Layer != WorldLayerId.Surface ||
                !float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
                !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY))
                throw new InvalidDataException("This pass restores surface campaigns only.");
        }
        PackedScene scene = GD.Load<PackedScene>(MenuNavigation.CampaignScene)
            ?? throw new IOException("Campaign scene is unavailable.");
        Node world = scene.Instantiate();
        try
        {
            Player player = world.GetNode<Player>("WorldObjects/Player");
            ChunkController chunks = world.GetNode<ChunkController>("Systems/ChunkController");
            if (continueCampaign)
            {
                CampaignRecipe.Apply(world, data);
                player.Position = new Vector2(data.SpawnX, data.SpawnY);
            }
            else
            {
                // A campaign uses a fresh seed rather than the scene's fixed test seed.
                using RandomNumberGenerator random = new();
                random.Randomize();
                chunks.WorldSeed = random.Randi();
                data.Seed = chunks.WorldSeed;
                data.SpawnX = player.Position.X; data.SpawnY = player.Position.Y;
                CampaignRecipe.Capture(world, data);
            }
            world.AddChild(new CampaignSession
                { Name = "CampaignSession", _data = data, _restoring = continueCampaign });
        }
        catch { world.Free(); throw; }
        SceneTree tree = menu.GetTree();
        // Retire the old scene before activating the detached campaign.
        Callable.From(() =>
        {
            Node previous = tree.CurrentScene;
            if (previous != null) { tree.Root.RemoveChild(previous); previous.QueueFree(); }
            tree.Paused = false;
            tree.Root.AddChild(world);
            tree.CurrentScene = world;
        }).CallDeferred();
    }

    // =========================================================
    // Let terrain startup run while player actions and item collection remain frozen.
    public override void _Ready()
    {
        _world = GetParent();
        _player = _world.GetNode<Player>("WorldObjects/Player");
        _chunks = _world.GetNode<ChunkController>("Systems/ChunkController");
        _objectsMode = ProcessModeEnum.Inherit;
        ProcessPriority = 1000;
        _startup.Start();
    }

    // =========================================================
    // Wait for terrain, player artwork and the deferred world clock, then restore once.
    public override void _Process(double delta)
    {
        if (_finished) return;
        Node objects = _world.GetNode("WorldObjects");
        objects.ProcessMode = ProcessModeEnum.Disabled;
        try
        {
            _clock ??= WorldEclipse.Find(this)?.GetNodeOrNull<WorldClock>("WorldClock");
            if (!_chunks.WorldReady || _player.Controls == null || !_player.IsPhysicsProcessing() || _clock == null)
            {
                if (_startup.Elapsed.TotalSeconds > 120)
                    throw new IOException("Campaign initialization did not finish; check Godot's errors.");
                return;
            }
            if (_restoring)
            {
                Vector2 position = new(_data.Player.X, _data.Player.Y);
                _player.GlobalPosition = position;
                if (!_chunks.PrepareDestination(position))
                {
                    if (_startup.Elapsed.TotalSeconds > 120)
                        throw new IOException("Saved destination could not be prepared.");
                    return;
                }
                if (!_chunks.IsNavigationPointAvailable(position, 12f))
                    throw new InvalidDataException("Saved position is no longer on available surface terrain.");
                RestorePlayer();
                _player.GlobalPosition = position;
                _player.Velocity = Vector2.Zero;
                _clock.RestoreSave(_data.WorldSeconds);
            }
            else _clock.RestoreSave(0);
            _player.GetNode<Camera2D>("Camera2D").ResetSmoothing();
            PauseMenu.Attach(_player, _player.Controls).BindSave(Save);
            objects.ProcessMode = _objectsMode;
            _finished = true; SetProcess(false);
            if (_restoring && !string.IsNullOrEmpty(CampaignStore.RecoveryMessage))
                GD.Print(CampaignStore.RecoveryMessage);
        }
        catch (Exception error) { FailLoad(error); }
    }

    // =========================================================
    // Restore capacities first, physical items second, shortcuts and vitals last.
    private void RestorePlayer()
    {
        PlayerSaveData saved = _data.Player;
        _player.GetNode<PlayerStats>("Systems/Stats").RestoreSave(saved);
        ItemCatalog items = ResourceWorld.Find(this).Catalog;
        _player.GetNode<PlayerInventory>("Systems/Inventory").RestoreSave(saved, items);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").RestoreSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").RestoreSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").RestoreSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").RestoreSave(saved);
    }

    // =========================================================
    // Capture supported sections only while paused, on solid surface ground.
    private void Save()
    {
        if (!_finished || !GetTree().Paused || ProfileStore.Selected?.Id != _data.ProfileId)
            throw new InvalidOperationException("No paused campaign belongs to the selected profile.");
        if (WorldLayerMember.For(_player) != WorldLayerId.Surface)
            throw new InvalidOperationException("Underground saving comes with the layer-restoration pass.");
        if (_player.IsAirborne || !_player.GetNode<Health>("Systems/Health").IsAlive)
            throw new InvalidOperationException("Save while alive and standing on the ground.");
        PlayerSaveData saved = new()
        {
            X = _player.GlobalPosition.X, Y = _player.GlobalPosition.Y,
            Layer = WorldLayerId.Surface,
            Health = _player.GetNode<Health>("Systems/Health").Current
        };
        _player.GetNode<PlayerStats>("Systems/Stats").CaptureSave(saved);
        _player.GetNode<PlayerVitals>("Systems/Vitals").CaptureSave(saved);
        _player.GetNode<PlayerInventory>("Systems/Inventory").CaptureSave(saved);
        _player.GetNode<PlayerHotbar>("Systems/Hotbar").CaptureSave(saved);
        _player.GetNode<PlayerCrafting>("Systems/Crafting").CaptureSave(saved);
        _player.GetNode<PlayerSurvival>("Systems/Survival").CaptureSave(saved);
        _data.Player = saved;
        _data.WorldSeconds = _clock.ElapsedSeconds;
        _data.SavedUtc = DateTime.UtcNow;
        CampaignStore.Write(_data);
    }

    // =========================================================
    // Keep failed restoration frozen and show an exit route without saving over it.
    private void FailLoad(Exception error)
    {
        _finished = true; SetProcess(false);
        GetTree().Paused = true;
        AcceptDialog dialog = new()
        {
            Title = "Campaign load failed", DialogText = error.Message +
                "\nYour existing save was preserved.",
            ProcessMode = ProcessModeEnum.Always
        };
        AddChild(dialog);
        dialog.GetOkButton().Text = "MAIN MENU";
        dialog.Confirmed += () => MenuNavigation.Open(this, MenuNavigation.MainScene);
        dialog.PopupCentered(new Vector2I(640, 240));
        GD.PushError(error.Message);
    }
}
'@
$Changes['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = @'
# Planned Work — Menus, Persistence and Local Lighting

## Purpose

Two upcoming work areas:

1. Main menu, in-game pause menu and persistent saves.
2. Local light sources that illuminate the world.

These are planned features. This document does not mark them as implemented.

Keep both systems modular and reuse the existing world layers, input modes,
inventory systems and shared visual settings where appropriate.

---

## 1 — Main Menu, Pause Menu and Persistent Saves

### Goal

Provide a proper entry point to the game and allow the player to save,
quit and continue without losing world changes.

### Main Menu

Initial options:

- Continue — available when a valid save exists.
- New Campaign — create a new world and player.
- Load Campaign — choose an existing save.
- Change Profile - can switch between profiles
- Sandbox - my testing
- Options. - placeholder
- About - just about this game - placeholder
- Exit.

Keep menu presentation separate from world creation and save/load logic.
Do not embed these systems in world_infinite or the player script.

### In-Game Pause Menu

Press ESC during gameplay to open the pause menu.

Initial options:

- Resume.
- Save Game.
- Options.
- Return to Main Menu.
- Exit Game.

Use the shared input-mode system to prevent movement, shooting, hotbar
scrolling and world interactions while the menu is open.

For this single-player first pass, opening the pause menu pauses world
simulation. Menu controls must continue processing.

ESC should first close the active interface where appropriate, such as
the inventory or debug map, rather than opening several menus together.

### Persistent Saves

Saving must preserve the playable world, not only the player's position
and the world seed.

Save data should include:

- Save format version.
- World identity and generation seed.
- Generation settings needed to reproduce the world.
- Player position and exact layer ID.
- Player vitals and progression.
- Inventory, equipment and hotbar assignments.
- Changes to generated world objects.
- Harvested or depleted resources.
- Container contents and claimed loot.
- Placed objects and structures.
- Relevant changes to liquids and basins.
- Persistent entities where required.
- Persistent connections between layers where required.

Generated terrain can be recreated from its seed and settings.
Store changes to that generated world rather than saving every unchanged
tile or loaded node.

Unloading a chunk must not discard its persistent changes.
Returning to a chunk or reloading a save must not restore harvested
resources or regenerate already-claimed loot.

Persistent object identities must include the world layer so objects at
the same coordinates on different depths remain separate.

### Reliability

- Store saves in Godot's user data location.
- Write to a temporary file before replacing the previous save.
- Keep a recoverable previous save.
- Detect unsupported or damaged saves and show a useful message.
- Include a save format version for future migrations.
- Confirm before overwriting or abandoning unsaved progress.

Do not treat debug test placement as permanent world content by default.

### Suggested Passes

1. Menu scenes and ESC/input-mode integration.
2. Save format, world identity and player persistence.
3. Persistent chunk changes, containers and world objects.
4. Remaining systems, recovery handling and end-to-end verification.

Do not label persistence complete until all currently relevant gameplay
changes survive quitting and loading.

### Completion Checks

- New Game starts a fresh world.
- Continue restores the correct save and layer.
- ESC pauses and resumes cleanly.
- Gameplay input does not leak through menus.
- Inventory and hotbar assignments survive loading.
- Harvested resources remain harvested.
- Claimed loot stays claimed.
- Container contents and placed objects survive loading.
- Chunk unloading does not lose changes.
- Surface and underground progress remain independent.
- A failed save does not destroy the previous valid save.

---

## 2 — Local Light Sources

### Goal

Allow local sources to illuminate nearby terrain and objects.

Examples:

- Lamps and placed lights.
- Glowing enemies or wildlife.
- Bioluminescent plants.
- Powered equipment.
- Temporary effects and projectiles.

An emissive-looking sprite or glow alone is not enough: the source should
also affect nearby world surfaces.

### Design Direction

Use one reusable light definition and runtime system.

A source should be able to define:

- Colour.
- Intensity.
- Radius.
- Enabled state.
- Optional flicker or pulse.
- Whether it moves.
- Whether it requires shadows.

Keep species-specific light settings with their species and object-specific
settings with their objects.

Shared lighting behaviour belongs with the existing visual lighting
systems. Global quality limits and multipliers can live in CONFIG.

### Compatibility

Review the existing terrain and sprite shaders before choosing the
implementation.

Local lighting must work with:

- Procedural ground surfaces.
- Imported and baked sprites.
- Terrain elevation and projected artwork.
- Existing sun lighting and shadows.
- Surface and underground layers.

Lights must respect exact layer identities. A surface light must not
illuminate a cave directly below it merely because their coordinates
overlap.

Decide explicitly how local lighting interacts with fading layers during
entrance transitions.

### Performance

Start with a small number of lights and measure the cost in a dense biome.

- Exclude distant lights from active lighting work.
- Stop unloaded lights from updating.
- Avoid scanning every world object for every light each frame.
- Avoid separate processing helpers on every vegetation instance.
- Update static light data only when it changes.
- Limit simultaneous visible lights through shared quality settings.
- Make local shadow casting optional and reserve it for selected sources.

Do not assume a light is cheap because its visual effect looks simple.

### Suggested Passes

1. Review shader compatibility and implement one test light.
2. Verify terrain, sprites, elevation and layer isolation.
3. Add reusable definitions and attach lights to objects and entities.
4. Add quality controls and measure performance with multiple sources.
5. Consider flicker, pulses and selected local shadows afterward.

### Completion Checks

- A test lamp visibly illuminates nearby ground and sprites.
- Light fades smoothly with distance.
- Moving sources remain aligned with their artwork.
- Lights affect only the correct world layer.
- Lights disappear correctly when their objects unload or are removed.
- Existing sunlight and shadows remain correct.
- Multiple lights have a measured, acceptable performance cost.
- Disabling local lighting restores the baseline appearance.

---

## Working Rules

- Review the latest GitHub push before preparing each pass.
- Keep scripts focused and folders clearly organised.
- Reuse existing shared systems where appropriate.
- Provide complete replacement files or functions.
- Remove temporary test nodes and obsolete implementations after testing.
- Update this document with actual implementation and verification status.

## Save Passes 1–2 — Applied, Verification Pending

- One current campaign slot per stable profile ID; manual pause-menu saving only.
- Versioned JSON, temporary-file replacement, previous-save backup and recovery.
- Fresh campaign seeds, exported world settings and resource recipe compatibility checks.
- Surface player position, original spawn, stats/modifiers, vitals, inventory, equipment, hotbar, crafting and world clock.
- Resource definition edits are detected and incompatible loads are refused; resource migrations remain future work.
- Save while alive, grounded and on the surface. Underground restore is a later pass.
- Not yet saved: harvested resources, grass changes, loose drops, containers, loot, buildings, entities, basin changes or connections.
- Debug herd and deep-cave test systems are unchanged and outside this pass.
- Build and local gameplay verification must be completed before marking tested.

Checks: two separate profiles; pause/save/quit/Continue; same surface position, original respawn location, seed/layout, reserves, inventory, selected hotbar, crafting progress and eclipse time; second save backup; rejected incompatible recipe; no save creation from Sandbox/F6.
'@
$Expected['CONFIG/WorldConfig.cs'] = '067311cc7b5c385fe32911a9183bd6a8a3502c777fe6c8417e4ff53bb1c2705c'
$Expected['PLAYER/Player.cs'] = 'b37fe3509b4f48dea86b03ab8a1eafa77ca5265bb037dbd3373ddc1977dc4294'
$Expected['WORLD/Time/WorldClock.cs'] = '101b77dfee2ccc4b04ffb0722067b7dc75f93fb091c153dcb99a3325b03b1b0b'
$Expected['WORLD/Generation/WorldGenerator.cs'] = '5311448009de3d9500f7d90e62df372b6988972a047461b0b96d58548364135f'
$Expected['WORLD/Chunks/ChunkController.cs'] = 'bf84571202b0b02ac212807ce3e3a6e97f025ac6bc2ef8bbb367c38a51a40194'
$Expected['SYSTEMS/Crafting/PlayerCrafting.cs'] = '2348d2c33cca14fcb6ea31d9e602cb046f9ce64b6df6f143345121de3d61d5b7'
$Expected['SYSTEMS/Crafting/CraftingRecipe.cs'] = 'bd82fe5423cd56117285875337393a53fc6eaa2065be7406adb777af6cb11dd8'
$Expected['SYSTEMS/Profiles/ProfileStore.cs'] = 'ddd4c9a66f03b1b25e4634fbed78bc265973043ab18b645ff64a9b48db2abb7d'
$Expected['UI/Menus/Pause/PauseMenu.cs'] = '17ed2e5135688d5cb02a397481955114aaac8de1e223b5281df9f091018cfa41'
$Expected['UI/Menus/Main/MainMenu.cs'] = '898ec11055909c67c633e6939d93f4de2327d5603503a97c74dea7e7dc55ae98'
$Expected['PLAYER/Stats/PlayerVitals.cs'] = '0a9a3fb990be10f808ba310d06607e1b9bf2035afbdcd8b6c29a27e2fc694aa3'
$Expected['PLAYER/Stats/PlayerStats.cs'] = '78776b42efcdaabdf9a73cd189a2238738619c8da412b58d0a0773838e1e1141'
$Expected['PLAYER/Stats/StatModifier.cs'] = '6027fdb61887240a6da6897b9e0ebf56796a419a4fac1759855cf426967b0390'
$Expected['PLAYER/Equipment/PlayerEquipment.cs'] = '961a14f2df83de443dc1a49e4012ae7edec45489a7d7aa11b56c97733866afd6'
$Expected['PLAYER/Inventory/InventoryStorage.cs'] = '6ebf49a79ee87db39cfa8da675a297e0566351b51b0320c8e207fcb1de9f0ed9'
$Expected['PLAYER/Inventory/PlayerInventory.cs'] = 'adbc0f9515a275abb3930fc297557aa84183a27a1bbf021098ac7cfa8ccf2b30'
$Expected['PLAYER/Inventory/PlayerHotbar.cs'] = '1534ec6f01151af1fb6b7a51bb3071cdbb234057f331d1320107728bb08b7cdf'
$Expected['PLAYER/Survival/PlayerSurvival.cs'] = 'ad1b7a2e405668a772f0afbfb91192f75607f140a0b5d39c2c0f15bc2f48c72c'
$Expected['NOTES/OngoingWork/PersistentSave_LocalLighting.md'] = 'e29b300a1966155b4c847a75213cb8662b49631982fab818109ec2efef5b0100'

# Validate all dependencies and targets before touching any project file.
$Conflicts = New-Object System.Collections.Generic.List[string]
$Pending = New-Object System.Collections.Generic.List[string]
foreach ($Relative in $Expected.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (!(Test-Path $Path)) { $Conflicts.Add("Missing: $Relative"); continue }
    $Hash = Digest ([IO.File]::ReadAllText($Path))
    $AlreadyUpdated = $Changes.Contains($Relative) -and $Hash -eq (Digest $Changes[$Relative])
    if ($Hash -ne $Expected[$Relative] -and !$AlreadyUpdated) { $Conflicts.Add("Changed since reviewed push: $Relative") }
}
foreach ($Relative in $Changes.Keys) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        if ((Digest ([IO.File]::ReadAllText($Path))) -eq (Digest $Changes[$Relative])) { continue }
        if (!$Expected.ContainsKey($Relative)) { $Conflicts.Add("Existing new-file destination: $Relative"); continue }
    }
    $Pending.Add($Relative)
}
if ($Conflicts.Count) { throw ($Conflicts -join "`n") }
if (!$Pending.Count) { Write-Host 'These passes are already installed. No files changed.'; return }
Write-Host ('Files to install/update: ' + $Pending.Count)
$Pending | ForEach-Object { Write-Host ('  ' + $_) }
if ($Preview) { Write-Host 'Preview complete. No files changed.'; return }
$Backup = Join-Path $ProjectRoot ('.save-pass-backups/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($Backup) | Out-Null
$Original = @{}
# Backups use .txt so the C# compiler cannot compile duplicate scripts.
foreach ($Relative in $Pending) {
    $Path = Join-Path $ProjectRoot $Relative
    if (Test-Path $Path) {
        $Original[$Relative] = [IO.File]::ReadAllBytes($Path)
        $Copy = Join-Path $Backup ($Relative + '.before.txt')
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Copy)) | Out-Null
        [IO.File]::WriteAllBytes($Copy, $Original[$Relative])
    }
}
$Written = New-Object System.Collections.Generic.List[string]
try {
    foreach ($Relative in $Pending) {
        $Path = Join-Path $ProjectRoot $Relative
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path)) | Out-Null
        $Written.Add($Relative)
        [IO.File]::WriteAllText($Path, $Changes[$Relative] + "`n", $Utf8)
    }
}
catch {
    foreach ($Relative in $Written) {
        $Path = Join-Path $ProjectRoot $Relative
        if ($Original.ContainsKey($Relative)) { [IO.File]::WriteAllBytes($Path, $Original[$Relative]) }
        elseif (Test-Path $Path) { Remove-Item -LiteralPath $Path -Force }
    }
    throw
}
Write-Host ('Installed. Original files backed up in: ' + $Backup)
Write-Host 'Reopen Godot and build C#. Start through Boot > profile > New Campaign.'
Write-Host 'PARTIAL SAVE: surface world/player/inventory/crafting/time only.'
Write-Host 'Objects, harvesting, containers, entities and underground saving are later passes.'
