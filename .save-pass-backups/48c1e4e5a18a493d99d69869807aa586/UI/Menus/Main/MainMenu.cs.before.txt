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

        _campaign = AddMenu(
            menu, "Campaign",
            () => Launch(CampaignScene), CampaignArtwork);

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