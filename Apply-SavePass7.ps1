# Installs Save Pass 7 against c677eca after Layer Connection Pass 3. No Git operations.
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
if (!(Test-Path (Join-Path $ProjectRoot 'SYSTEMS/Saving/WorldObjectSaves.cs'))) {
    throw 'This installer requires the existing project save foundation.'
}
$Changes = [ordered]@{}
$Expected = @{}
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
            : "Save this campaign to the selected profile.";
    }
    // =========================================================
    // Make backup recovery visible and pause before the player resumes gameplay.
    public void ShowRecoveryNotice(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        if (!_open) Open();
        ShowMessage("Campaign recovered", message);
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
            ShowMessage("Campaign saved", "Your campaign was saved to this profile.\n" +
                CampaignSession.SavedPositionFor(this));
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
        continueButton.TooltipText = continueButton.Disabled
            ? "Save a campaign with this profile to enable Continue."
            : "Resume this profile's saved campaign.";

        _campaign = AddMenu(menu, "New Campaign", NewCampaign, CampaignArtwork);
        menu.AddChild(UIButtonFactory.Label(
            "ONE CAMPAIGN PER PROFILE", 14));

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
    // Route non-campaign scenes through the shared navigation helper.
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
$Changes['SYSTEMS/Saving/CampaignMenu.cs'] = @'
// Main-menu campaign actions stay separate from save storage and world loading.
using Godot;
using System;

public partial class MainMenu
{
    private ConfirmationDialog _newCampaignConfirm;
    private bool _openingCampaign;

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
            _newCampaignConfirm.GetOkButton().Text = "START NEW";
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
        if (_openingCampaign) return;
        _openingCampaign = true;
        try { CampaignSession.Launch(this, restore); }
        catch (Exception error)
        {
            _openingCampaign = false;
            ShowMessage("Could not open campaign", error.Message);
        }
    }
}
'@
$Changes['SYSTEMS/Saving/CampaignSession.cs'] = @'
// Coordinates world/player restoration and the changed-resource save section.
using Godot;
using System;
using System.Diagnostics;
using System.IO;

public partial class CampaignSession : Node
{
    private CampaignData _data;
    private bool _restoring, _finished, _layerRestored;
    private System.Collections.Generic.IEnumerator<int> _destinationPlan;
    private IDisposable _destinationPin;
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
            if (string.IsNullOrEmpty(data.Player.Layer) ||
                !float.IsFinite(data.Player.X) || !float.IsFinite(data.Player.Y) ||
                !float.IsFinite(data.SpawnX) || !float.IsFinite(data.SpawnY))
                throw new InvalidDataException("Saved player coordinates or layer are invalid.");
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
                // Begin loading at the actual save point, not the original landing site.
                WorldConfig.Find(world).GetLayerCatalog().Get(data.Player.Layer);
                // Reconstruct the seeded surface entrance before preparing underground terrain.
                Vector2 start = data.Player.Layer == WorldLayerId.Surface
                    ? new Vector2(data.Player.X, data.Player.Y)
                    : new Vector2(data.Player.SurfaceEntranceX, data.Player.SurfaceEntranceY);
                player.Position = world.GetNode<Node2D>("WorldObjects").ToLocal(start);
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
            EntityDeaths deaths = new() { Name = "EntityDeaths" };
            deaths.Initialize(data, world);
            world.AddChild(deaths);
            EntitySaves entities = new() { Name = "EntitySaves" };
            entities.Initialize(data, world);
            world.AddChild(entities);
            ResourceChanges resources = new() { Name = "ResourceChanges" };
            resources.Initialize(data);
            world.AddChild(resources);
            WorldObjectSaves objects = new() { Name = "WorldObjectSaves" };
            objects.Initialize(data, world);
            world.AddChild(objects);
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
    // Locate this world's campaign without keeping static references to old scenes.
    private static CampaignSession FindCampaign(Node context)
    {
        for (Node node = context; node != null; node = node.GetParent())
        {
            CampaignSession campaign = node.GetNodeOrNull<CampaignSession>("CampaignSession");
            if (campaign != null) return campaign;
        }
        return null;
    }

    // =========================================================
    // Keep respawning and generation clearance anchored to the original landing site.
    public static Vector2? OriginalSpawnFor(Node context)
    {
        if (context is not Player player) return null;
        CampaignSession campaign = FindCampaign(player);
        if (campaign?._data == null) return null;
        Node2D objects = player.GetParent<Node2D>();
        return objects.ToGlobal(new Vector2(campaign._data.SpawnX, campaign._data.SpawnY));
    }

    // =========================================================
    // Keep automatic crossings frozen until campaign restoration has completed.
    public static bool IsLoadingFor(Node context) => FindCampaign(context) is CampaignSession campaign && !campaign._finished;

    // =========================================================
    // Show exactly which coordinates were committed by the manual save action.
    public static string SavedPositionFor(Node context)
    {
        CampaignSession campaign = FindCampaign(context);
        if (campaign?._data?.Player == null) return "";
        PlayerSaveData player = campaign._data.Player;
        return $"Saved position: X {player.X:0.##}, Y {player.Y:0.##} ({player.Layer}).";
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
        try { WorldObjectSaves.Find(this).RestoreBuildings(_world); }
        catch (Exception error)
        {
            _world.GetNode("WorldObjects").ProcessMode = ProcessModeEnum.Disabled;
            FailLoad(error);
        }
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
                if (!PrepareSavedDestination(position))
                {
                    if (_startup.Elapsed.TotalSeconds > 120)
                        throw new IOException("Saved layer or destination could not be prepared.");
                    return;
                }
                RestorePlayer();
                _player.GlobalPosition = position;
                _player.Velocity = Vector2.Zero;
                _clock.RestoreSave(_data.WorldSeconds);
            }
            else _clock.RestoreSave(0);
            WorldObjectSaves.Find(this).RestoreDrops(this);
            Camera2D camera = _player.GetNode<Camera2D>("Camera2D");
            camera.ResetSmoothing();
            camera.ForceUpdateScroll();
            if (_restoring)
                GD.Print($"[CampaignLoad] Profile {_data.ProfileId}: restored " +
                    $"{_player.GlobalPosition}, saved ({_data.Player.X}, {_data.Player.Y}).");
            PauseMenu.Attach(_player, _player.Controls).BindSave(Save);
            objects.ProcessMode = _objectsMode;
            _finished = true; SetProcess(false);
            if (_restoring && !string.IsNullOrEmpty(CampaignStore.RecoveryMessage))
            {
                GD.Print(CampaignStore.RecoveryMessage);
                PauseMenu.Attach(_player, _player.Controls).ShowRecoveryNotice(CampaignStore.RecoveryMessage);
            }
        }
        catch (Exception error) { FailLoad(error); }
    }

    // =========================================================
    // Reconstruct local seeded routes at any saved depth, retaining the surface return entrance.
    private bool PrepareSavedDestination(Vector2 position)
    {
        string layer = _data.Player.Layer;
        if (layer == WorldLayerId.Surface)
        {
            _player.GlobalPosition = position;
            if (!_chunks.PrepareDestination(position)) return false;
            if (!_chunks.IsNavigationPointAvailable(position, 12f))
                throw new InvalidDataException("Saved position is no longer on available surface terrain.");
            return true;
        }
        WorldLayerController controller = WorldLayerController.Find(this)
            ?? throw new InvalidDataException("This campaign has no underground layer runtime.");
        if (!_layerRestored)
        {
            Vector2 entrance = new(_data.Player.SurfaceEntranceX, _data.Player.SurfaceEntranceY);
            _player.GlobalPosition = entrance;
            if (!_chunks.PrepareDestination(entrance)) return false;
            WorldLayerConnection route = null;
            foreach (WorldLayerConnection connection in controller.Worlds.Connections.All)
                if (connection.Id == _data.Player.SurfaceEntranceId && connection.UpperLayer == WorldLayerId.Surface)
                    route = connection;
            if (route == null || route.LowerLayer != controller.Worlds.SurfaceEntranceLayerId)
                throw new InvalidDataException("The saved surface entrance could not be reconstructed.");
            // Plan the saved area's endpoints before terrain or arrival-ramp sampling.
            CaveWorld destination = controller.Worlds.GetUnderground(layer);
            InfiniteWorldGeneration generation = InfiniteWorldGeneration.Find(this);
            if (_destinationPlan == null)
            {
                Vector2 tile = destination.WorldToTile(position);
                Rect2 area = new(tile - Vector2.One * 16f, Vector2.One * 32f);
                _destinationPin = generation.PinArea(area, layer);
                _destinationPlan = generation.PrepareArea(area, layer).GetEnumerator();
            }
            Stopwatch budget = Stopwatch.StartNew();
            while (_destinationPlan.MoveNext())
                if (budget.Elapsed.TotalMilliseconds >= 2) return false;
            _destinationPlan.Dispose(); _destinationPlan = null;
            controller.RestoreCampaignLayer(layer, position, route);
            _layerRestored = true;
        }
        _player.GlobalPosition = position;
        CaveWorld cave = controller.Worlds.GetUnderground(layer);
        if (!cave.Streaming.AreaReady(position)) return false;
        if (!controller.Worlds.IsAvailable(layer, position, 14f))
            throw new InvalidDataException("Saved position is no longer on available underground terrain.");
        _destinationPin?.Dispose(); _destinationPin = null;
        return true;
    }

    // =========================================================
    // Release temporary destination metadata on cancellation or load failure.
    public override void _ExitTree()
    {
        _destinationPlan?.Dispose(); _destinationPlan = null;
        _destinationPin?.Dispose(); _destinationPin = null;
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
    // Capture supported sections while paused and grounded in the exact active layer.
    private void Save()
    {
        if (!_finished || !GetTree().Paused || ProfileStore.Selected?.Id != _data.ProfileId)
            throw new InvalidOperationException("No paused campaign belongs to the selected profile.");
        string layer = WorldLayerMember.For(_player);
        WorldLayerController controller = WorldLayerController.Find(this);
        WorldLayerConnection entrance = controller?.LastSurfaceConnection;
        if (layer != WorldLayerId.Surface && (entrance == null || entrance.UpperLayer != WorldLayerId.Surface ||
            entrance.LowerLayer != controller.Worlds.SurfaceEntranceLayerId ||
            controller.Worlds.IsAvailable(layer, _player.GlobalPosition, 14f) != true))
            throw new InvalidOperationException("Save on ready underground ground with a known natural surface return entrance.");
        if (layer != WorldLayerId.Surface)
        {
            CaveWorld cave = controller.Worlds.GetUnderground(layer);
            Vector2 tile = cave.WorldToTile(_player.GlobalPosition);
            foreach (WorldLayerConnection connection in controller.Worlds.Connections.ForLayer(layer))
                if (connection.Id.StartsWith("TEST_", StringComparison.Ordinal) && connection.SampleArea.HasPoint(tile))
                    throw new InvalidOperationException("Move away from the temporary debug corridor before saving; use natural corridors for persistence tests.");
        }
        if (_player.IsAirborne || !_player.GetNode<Health>("Systems/Health").IsAlive)
            throw new InvalidOperationException("Save while alive and standing on the ground.");
        PlayerSaveData saved = new()
        {
            X = _player.GlobalPosition.X, Y = _player.GlobalPosition.Y,
            Layer = layer,
            SurfaceEntranceId = layer == WorldLayerId.Surface ? "" : entrance.Id,
            SurfaceEntranceX = layer == WorldLayerId.Surface ? 0 : entrance.UpperPosition.X,
            SurfaceEntranceY = layer == WorldLayerId.Surface ? 0 : entrance.UpperPosition.Y,
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
        ResourceChanges.Find(this).Capture(_data);
        WorldObjectSaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureResourceDefinitions(_data);
        EntityDeaths.Find(this).Capture(_data);
        EntitySaves.Find(this).Capture(_data);
        CampaignRecipe.CaptureObjectDefinitions(_data);
        CampaignRecipe.CaptureEntityDefinitions(_data);
        _data.Version = 6;
        _data.Coverage = "world-player-resources-objects-entities-layer";
        CampaignStore.Write(_data);
        GD.Print($"[CampaignSave] Profile {_data.ProfileId}: " +
            $"({saved.X}, {saved.Y}), original spawn ({_data.SpawnX}, {_data.SpawnY}).");
    }

    // =========================================================
    // Keep failed restoration frozen and show an exit route without saving over it.
    private void FailLoad(Exception error)
    {
        _destinationPlan?.Dispose(); _destinationPlan = null;
        _destinationPin?.Dispose(); _destinationPin = null;
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
$Changes['NOTES/OngoingWork/LayerGenerationConnections.md'] = @'
# Layer Generation and Connections

## Status

The three planned connection stages are implemented. Restore integration also completes save Pass 6.2. Local gameplay verification remains part of the save checklist in [SAVEMECHANICWORK.md](SAVEMECHANICWORK.md).

| Stage | Implemented behaviour |
|---|---|
| 1. Queries and tuning | Exact-layer biome queries without activating layers or building chunks; global connection-frequency multipliers. |
| 2. Permanent corridors | Explicit downward layer targets; seeded cave corridors discoverable from either endpoint; local preparation, bounded caches and spatial lookup. |
| 3. Restore integration | Save at any defined natural underground depth; prepare nearby routes before activation; restore exact position and retain the original surface return entrance. |

## Layer Rules

- Every layer has a stable ID, depth, biome catalog and generation settings. There is no maximum depth or assumption that Deep Caverns is the final layer; additional depths such as Hell require definitions and explicit incoming target rules.
- Surface entrances use the existing seeded surface planner. Underground definitions expose `DownwardLayerId`, `ConnectionChance`, `ConnectionSpacingTiles` and `ConnectionLengthTiles`. The target must exist and have a greater depth index.
- The upper layer owns each downward connection. Seed, stable layer-pair IDs and absolute candidate cells produce the same record whether discovered from above or below. Permanent corridor IDs are stable `L_...` identities.
- The source chamber joins the corridor mouth; its lower end joins the destination's chamber network. Source elevation uses base biome sampling. Conflicts between permanent pairs sharing a layer resolve deterministically from candidate data and ID priority.
- Connections are probabilistic and deliberately sparse. Configured spacing is a minimum; conservative geometry clearance may increase effective spacing. A higher chance does not guarantee a corridor in every chunk.
- Original spawn, seed, generation settings/resources and generator behaviour jointly determine compatibility. Seed alone cannot preserve a world after generation rules change.

## Global Frequency Tuning

Select **CONFIG** in `WORLD/Scenes/world_infinite.tscn` and expand **Layer Connection Frequency**. Dictionary keys name the upper/source layer.

| Key | Default multiplier | Current meaning |
|---|---:|---|
| `surface` | 1 | Surface to the catalog's surface-entrance underground layer. |
| `underground_1` | 0.5 | Upper Caverns to Deep Caverns. |
| `underground_2` | 0.25 | Reserved for a downward target when one is configured. |

`0` disables normal downward candidates; `1` uses base chance; `2` doubles it, capped at 100%. Allowed multipliers are 0–8. Missing entries for defined layers default to 1; unknown IDs/invalid values are rejected. Debug probability bypasses are unaffected.

Campaign recipes capture these settings and restore them on Continue. Restart and use a new test campaign to assess Inspector tuning changes. For quicker corridor testing, temporarily use `underground_1 = 8`, then restore 0.5.

## Queries and Streaming

`WorldGenerationQueries.BiomeAt(layer, logicalGlobalPosition)` reuses the exact layer's sampler, including inactive layers. Queries do not load chunks, activate layers or spawn objects. Feature/lair planning and surface flowers reacting to deeper features are future work.

Chunk preparation builds local metadata before terrain samples it. Lookup is indexed by touching layer pairs and corridor footprints. Each pair targets 512 cached decisions, including empty decisions; protected loaded working sets may exceed the target. Evicted unpinned records unregister their spatial entries and markers and can regenerate later. Chunk leases and the remembered surface entrance protect required metadata.

Continue first reconstructs the saved surface return entrance, then plans around the saved position with a temporary lease and a 2 ms per-frame planning budget. The saved layer activates only after planning; real chunk streaming takes over protection once terrain is ready. Intervening route segments regenerate as approached; no whole-journey scan or physical loading of every depth is needed.

## Persistence Boundaries

Base biomes, chambers and unchanged natural corridors regenerate. Exact player layer/position, original return entrance identity, and supported gameplay changes belong to the save system. Changed/player-created connections do not yet have gameplay mutation mechanics or a persistence section.

`DeepCavernsTest` is optional temporary geometry. Disable its **Enabled** property when testing natural corridors. Saving inside a `TEST_` corridor footprint is rejected. Pass 2 changed the generation recipe; older recipes may fail existing compatibility checks. Pass 3 did not introduce another schema/resource change.

## Verification

Automated checks cover biome-query equality/no activation, frequency validation, fresh-process upper-first/lower-first corridor equality, duplicate prevention, landing/controller descent and return, lower-room floor continuity, metadata eviction/pins/spatial cleanup, and a real deep save followed by fresh-process exact-depth restoration and return through Upper Caverns to surface. Guarded installers check preview, application, rerun and zero-write conflict rejection.

Headless tests use fixed seeds, increased test frequency, artwork substitutes and controlled transitions. Local walking, visuals, camera/collision seams and long-distance travel still need gameplay checks. The planned connection phase is complete; future feature planning and mutation mechanics are separate work.
'@
$Changes['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = @'
# Campaign Save Persistence

## Current Status

Manual profile-owned campaign saving is implemented for the currently supported gameplay state. Passes 1–5 and 6.1–6.2 are implemented. Pass 7 finishes save/recovery menu messaging and provides combined verification. Local gameplay sign-off remains required; this does not claim every possible future mechanic is persistent.

| Pass | Scope | Status |
|---|---|---|
| 1–2 | Profile ownership, atomic files/backup, world recipe/time, exact player state and surface position | Implemented; surface position confirmed locally. |
| 3 | Changed generated resources and chunk regeneration | Implemented. |
| 4 | Drops, storage/loot, wrecks, structures and health | Implemented; optional lifetime data stored, expiry not yet programmed. |
| 5.1–5.3 | Stable entity identities, deaths/rewards, living/retired entities, exact layer ownership and logical groups | Implemented. |
| 6.1 | Natural surface-connected cave restoration | Implemented; underground position/items confirmed locally. |
| 6.2 | Permanent connections and exact deeper-layer restoration | Implemented through the three connection stages; see [LayerGenerationConnections.md](LayerGenerationConnections.md). |
| 6.3 | Changed liquid/basin contents or levels | Deferred by agreement. `LiquidBody.SetFill` exists, but current gameplay does not mutate generated fill levels. |
| 7 | Combined verification, profile campaign selection/overwrite behaviour and menu/recovery finish | Implemented finishing changes; automated results below; local checklist remains. |

## Save Contents and Ownership

Each profile has one current campaign slot. **Continue** loads that selected profile's slot. **New Campaign** asks before starting fresh when a slot exists; the old save remains until the new campaign is successfully saved. Separate profiles retain independent campaign files.

A successful paused **Save Game** captures:

- Campaign identity, seed, original spawn, generation settings/resource definitions and world time/eclipse state.
- Exact player global ground position/layer, underground surface-return entrance identity/position, health, stats/modifiers, reserves, inventory/equipment, hotbar, crafting and survival state.
- Changed rocks, trees, plants, ores, ground deposits and consumed/cleared grass, retaining stable identities through chunk retirement.
- Physical drops, optional remaining lifetime, container and loot contents including empty caches, wrecks, placed structures and saved health.
- Entity deaths with reward identities, supported surviving/authored/population/retired actor state, exact layer transfers and logical group membership/formation/roaming/dissolution.

Unchanged seeded terrain, biomes, chambers and natural connections regenerate. Runtime targets, paths, reservations and engine references rebuild. A save requires the selected campaign's living, grounded player on available terrain. Underground saves require a known natural surface return entrance; temporary `TEST_` corridor footprints are rejected.

## Loading, Files and Compatibility

Campaign schema is **version 6** with four named gameplay sections: resource changes, world objects, entity deaths and entity state. Versions 1–5 remain readable under their original supported coverage and upgrade on successful Save. Missing gameplay state from older saves cannot be reconstructed retroactively. Earlier game versions cannot read version 6.

Continue validates the recipe and profile identity, freezes actions/crossings during initialization, reconstructs necessary entrance/nearby route metadata, activates the saved layer, waits for terrain, and restores player/time/items before gameplay resumes. Original spawn remains the generation/respawn anchor. Unknown layers, missing routes, unavailable terrain or incompatible resources fail visibly without silently moving the player to surface.

Files are written through a flushed temporary file and atomic replacement. A readable previous primary becomes the backup. Invalid replacement data cannot overwrite the primary. A readable backup can recover a damaged/missing primary without rewriting it during load. Backup recovery now opens a visible **Campaign recovered** notice with gameplay paused; dismiss it and Resume when ready. Failed loading preserves existing files and offers Main Menu.

The permanent connection planner changed generation resources in connection Pass 2. Saves from before that recipe change may be refused by compatibility checks. Pass 3/Pass 7 do not bypass those checks or change existing save files merely by installing.

## Pass 7 Menu Finish

- Removed obsolete world/player-only and partial-save messages. Save success reports the campaign and exact committed position/layer.
- Main Menu identifies the single campaign slot per profile; Continue explains its selected-profile behaviour and is disabled only when no primary/backup exists.
- Existing new-campaign replacement confirmation remains explicit. Repeated campaign-launch clicks are suppressed during the deferred scene transition; immediate validation failures allow retry.
- Successful backup recovery is shown in-game rather than only in the console.
- Options/autosave remain outside this pass. Multiple named save slots are not implemented.

## Verification and Local Sign-off

Previous automated stage checks cover resource mutation/retirement/regeneration; items, storage/empty loot, wrecks/structures/health; surviving/retired entities, groups, death rewards and duplicate prevention; profile separation, version upgrades, malformed-save primary preservation and backup recovery; exact surface/Upper/deep restoration and natural return routes. Headless fixtures substitute artwork and may control spawning/transitions.

Pass 7 automated checks passed:

- Full production C# compilation, including current audio/notification sources.
- One combined paused campaign capture and fresh-process restoration: player inventory/equipment, restored world time, partial generated-resource work/depleted grass, drop identity/count/layer/optional lifetime, storage, empty loot, and structure health.
- Authored/living/retired/transferred entities, health/home state, logical group reservations/dissolution, repeated remembered-actor scans without duplication, and resaving dormant state.
- Malformed replacement data preserving the primary, readable-backup recovery, independent profile slots, real Save UI success/position text, Continue availability, and new-campaign confirmation cancellation preserving the slot.
- A separate disposable damaged-primary run: visible paused recovery notice, Resume, combined restoration, and transferred-cave actor restoration.
- Installer preview/application/exact payload/rerun and zero-write conflict protection.

Headless checks use fixture actors/containers, controlled population/visibility and artwork substitutes; the structure fixture exercises real creation/capture/restore without an inventory placement transaction. Surface/deep position and natural return-route tests are from the preceding integration pass. These checks do not replace local walking, rendered UI or long-session gameplay tests.

1. Profile A: change inventory/vitals, partially harvest and deplete resources, drop/pick up items, use storage/loot and place/damage a structure. Damage/kill nearby entities. Save, leave chunks, revisit and verify changes.
2. Close completely, select A and Continue. Check exact position, inventory, crafting, world time and changed objects/entities; watch for duplicate items/rewards or regenerated depleted resources.
3. Save and restart on the surface, in Upper Caverns and in natural Deep Caverns. Verify items/layer/position and walk back through natural connections. Disable DeepCavernsTest for these tests. Repeat away from the original surface entrance and near a ramp.
4. Profile B: verify its Continue/save is independent of A. Start a new campaign, cancel the replacement prompt, then start and leave without saving; A/B's previous saved campaign must still exist. Save a new campaign only when deliberately replacing that profile's slot.
5. Check Save success/failure, Exit's unsaved-progress confirmation, Main Menu/Continue, and backup-recovery notice. Do not damage real saves for testing; use a disposable profile/copy.

## Deferred Work

- Liquid/basin mutation persistence (6.3): revisit when gameplay can drain, fill or otherwise change levels/contents. Generated initial fill currently reconstructs from the recipe.
- Autosave/options scheduling; actual dropped-item expiry/countdown.
- Future player-created/modified connections, new gameplay systems, and feature/lair/boss mechanics: add their non-reconstructible state when implemented.
- Complete the local checklist and investigate any reported failures before describing persistence as fully gameplay-verified.
'@
$Expected['UI/Menus/Pause/PauseMenu.cs'] = '3dd5b6c388b3c5fb579478642464c303260bc33bebd605e565146623b0d6f19a'
$Expected['UI/Menus/Main/MainMenu.cs'] = '923fd48ee4452edf85303c3889367a49d590a36d9663682a66ecf11d87b52ed1'
$Expected['SYSTEMS/Saving/CampaignMenu.cs'] = 'f47546d89cc6c006ddc25575cea5deebf7c600f7389afc952b31de9284dea59a'
$Expected['SYSTEMS/Saving/CampaignSession.cs'] = '163196fc02fcee105d463ffd8d54f85cc97156d4db539f17f71505c8502ac9f8'
$Expected['NOTES/OngoingWork/LayerGenerationConnections.md'] = '7b9ab5ad043008aafee7c4d19a390de7f6f50810f3737ee5823e0fec77191128'
$Expected['NOTES/OngoingWork/SAVEMECHANICWORK.md'] = 'd5ab02d8a48081065a9434ea50b5d5a86c8ffc66310e6432823c5e2848e3b5d5'
$Expected['WORLD/Layers/WorldLayerController.cs'] = '2d83993436318d15d209c63732a4f45b334929e180fd8910219463d07e89ab88'
$Expected['WORLD/Streaming/InfiniteWorldGeneration.cs'] = 'f6fef2b2e8bfff16b0ce989df891383dcb3cc500ba3d39b3d80050ad5568b671'
$Expected['WORLD/Layers/Connections/LayerConnectionPlanner.cs'] = '21ab76c5ae24d4bed4c3eac82743e8f7341e1f73d03f005af7212cb3577d8f4a'
$Expected['WORLD/Generation/Caves/CaveWorld.cs'] = '2a3c567acb9228d9238edd99b50946e8963711e8ea18d40d0144ccc880f51b43'
$Expected['SYSTEMS/Saving/WorldObjectSaves.cs'] = '1db1da422ac930f1a07e9d001ebaf7cc2b51e97715b507fe68f97a5bab16401f'
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
if (!$Pending.Count) { Write-Host 'Save Pass 7 is already installed. No files changed.'; return }
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
Write-Host 'Reopen Godot and build C#. Save menu finishing and current-state notes are installed.'
Write-Host 'Use the local gameplay checklist in SAVEMECHANICWORK.md.'
Write-Host 'Liquid changes, autosaves and dropped-item expiry remain deferred. Existing save files were not modified.'
