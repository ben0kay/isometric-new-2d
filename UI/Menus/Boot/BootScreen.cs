// Startup screen: select a local profile or create one before the main menu.
using Godot;
using System;

public partial class BootScreen : Control
{
    #region Configuration
    [ExportGroup("Layout")]
    [Export] public float PanelWidth { get; set; } = 560f;
    [Export] public string Heading { get; set; } = "SELECT PROFILE";
    #endregion

    #region State
    private VBoxContainer _list;
    private Label _status, _nameError;
    private Button _newProfile;
    private AcceptDialog _create;
    private LineEdit _name;
    #endregion

    #region Lifecycle
    // =========================================================
    // Profiles persist on disk; selection changes the current session.
    public override void _Ready()
    {
        GetTree().Paused = false;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        BuildUi();
        RefreshProfiles();
    }
    #endregion

    #region Layout
    // =========================================================
    // Keep startup independent from world scenes and gameplay controllers.
    private void BuildUi()
    {
        ColorRect background = new()
        {
            Color = UIButtonFactory.Ink,
            MouseFilter = MouseFilterEnum.Ignore
        };

        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        MarginContainer outer = new();

        foreach (string side in new[] { "left", "right", "top", "bottom" })
            outer.AddThemeConstantOverride("margin_" + side, 32);

        AddChild(outer);
        outer.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        CenterContainer centre = new();
        outer.AddChild(centre);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(PanelWidth, 0)
        };

        panel.AddThemeStyleboxOverride(
            "panel", UIButtonFactory.Box(
                new Color("#0d2029"), UIButtonFactory.Accent));

        centre.AddChild(panel);

        VBoxContainer content = new();
        content.AddThemeConstantOverride("separation", 16);
        panel.AddChild(content);

        content.AddChild(UIButtonFactory.Label(Heading, 28));
        content.AddChild(UIButtonFactory.Label(
            "Choose who is exploring this world."));

        ScrollContainer scroll = new()
        {
            CustomMinimumSize = new Vector2(0, 260),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        content.AddChild(scroll);

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };

        _list.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(_list);

        _newProfile = UIButtonFactory.Create(
            "NEW PROFILE", ShowCreate);

        content.AddChild(_newProfile);
        content.AddChild(UIButtonFactory.Create(
            "EXIT", () => GetTree().Quit()));

        _status = UIButtonFactory.Label("");
        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _status.CustomMinimumSize = new Vector2(PanelWidth - 40, 44);
        content.AddChild(_status);

        _create = new AcceptDialog
        {
            Title = "New Profile",
            Exclusive = true,
            DialogHideOnOk = false
        };

        AddChild(_create);

        VBoxContainer form = new();
        form.AddThemeConstantOverride("separation", 12);
        _create.AddChild(form);

        form.AddChild(UIButtonFactory.Label("Profile name"));

        _name = new LineEdit
        {
            MaxLength = 32,
            PlaceholderText = "Enter your name"
        };

        form.AddChild(_name);

        _nameError = UIButtonFactory.Label("");
        _nameError.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _nameError.CustomMinimumSize = new Vector2(400, 44);
        form.AddChild(_nameError);

        _create.GetOkButton().Text = "SAVE";
        UIButtonFactory.Apply(_create.GetOkButton());
        UIButtonFactory.Apply(_create.AddCancelButton("CANCEL"));

        _create.Confirmed += CreateProfile;
        _name.TextSubmitted += _ => CreateProfile();
    }
    #endregion

    #region Profiles
    // =========================================================
    // Show saved identities without imposing a fixed number of profile slots.
    private void RefreshProfiles()
    {
        foreach (Node child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }

        try
        {
            foreach (PlayerProfile profile in ProfileStore.Profiles)
            {
                string id = profile.Id;

                _list.AddChild(UIButtonFactory.Create(
                    profile.Name, () => Choose(id)));
            }

            if (ProfileStore.Profiles.Count == 0)
                _list.AddChild(UIButtonFactory.Label(
                    "No profiles yet. Create your first one."));

            _status.Text = ProfileStore.RecoveryMessage;
            _newProfile.GrabFocus();
        }
        catch (Exception error)
        {
            _status.Text = error.Message;
            _newProfile.Disabled = true;
        }
    }

    // =========================================================
    // Activate an identity before entering the main menu.
    private void Choose(string id)
    {
        try
        {
            ProfileStore.Select(id);
            MenuNavigation.Open(this, MenuNavigation.MainScene);
        }
        catch (Exception error) { _status.Text = error.Message; }
    }

    // =========================================================
    // Clear the form and focus typing inside the modal popup.
    private void ShowCreate()
    {
        _name.Text = "";
        _nameError.Text = "";
        _create.PopupCentered(new Vector2I(440, 210));
        _name.GrabFocus();
    }

    // =========================================================
    // Keep invalid names or disk errors visible without closing the form.
    private void CreateProfile()
    {
        try { ProfileStore.Create(_name.Text); }
        catch (Exception error)
        {
            _nameError.Text = error.Message;
            return;
        }

        _create.Hide();
        RefreshProfiles();

        try { MenuNavigation.Open(this, MenuNavigation.MainScene); }
        catch (Exception error) { _status.Text = error.Message; }
    }
    #endregion
}