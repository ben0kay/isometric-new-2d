// Opens the shared debug menu with F1 without pausing world simulation.
// Overlay controls reuse existing services and preserve cached enemy geometry.
using Godot;

public partial class DebugMenu : CanvasLayer
{
    #region State
    private InputModes _modes;
    private WorldCollisionDebug _collision;
    private WorldEnemyRangesDebug _ranges;
    private Control _root;
    private CheckBox _collisionToggle, _rangesToggle;
    private bool _open;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve sibling debug services and build the menu once.
    public override void _Ready()
    {
        Layer = 120;
        _modes = InputModes.For(this);
        _collision = GetParent<WorldCollisionDebug>();
        _ranges = _collision.GetNode<WorldEnemyRangesDebug>("EnemyRanges");

        BuildInterface();
        SetProcess(false);
        SetProcessInput(true);
    }

    // =========================================================
    // Release only this menu's input claim when the scene closes.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);
    }

    // =========================================================
    // Toggle with F1 and close with Escape while this menu owns input.
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        if (key.PhysicalKeycode == Key.F1)
        {
            if (_open)
            {
                if (_modes.OwnsInput(this)) CloseMenu();
            }
            else if (_modes.GameplayAllowed &&
                !GetViewport().GuiIsDragging())
                OpenMenu();
            else
                return;

            GetViewport().SetInputAsHandled();
        }
        else if (_open && _modes.OwnsInput(this) &&
            key.PhysicalKeycode == Key.Escape)
        {
            CloseMenu();
            GetViewport().SetInputAsHandled();
        }
    }
    #endregion

    #region Interface
    // =========================================================
    // Build a centred panel with a mouse-blocking backdrop and reusable toggles.
    private void BuildInterface()
    {
        _root = new Control
        {
            Name = "Interface",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        ColorRect backdrop = new()
        {
            Color = new Color(0f, 0f, 0f, 0.45f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        CenterContainer centre = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(centre);
        centre.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        PanelContainer panel = new()
        {
            CustomMinimumSize = new Vector2(380, 0)
        };
        centre.AddChild(panel);

        StyleBoxFlat style = new()
        {
            BgColor = new Color("#14202a"),
            BorderColor = new Color("#486577"),
            BorderWidthLeft = 1,
            BorderWidthRight = 1,
            BorderWidthTop = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 24,
            ContentMarginRight = 24,
            ContentMarginTop = 22,
            ContentMarginBottom = 22
        };
        panel.AddThemeStyleboxOverride("panel", style);

        VBoxContainer column = new();
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        AddLabel(column, "DEBUG MENU", 22, new Color("#dcebf3"));
        AddLabel(column, "World overlays", 14, new Color("#91aaba"));
        column.AddChild(new HSeparator());

        _collisionToggle = new CheckBox
        {
            Text = "Collision footprints"
        };
        column.AddChild(_collisionToggle);
        _collisionToggle.Toggled += OnCollisionToggled;

        _rangesToggle = new CheckBox
        {
            Text = "Enemy ranges"
        };
        column.AddChild(_rangesToggle);
        _rangesToggle.Toggled += OnRangesToggled;

        VBoxContainer legend = new();
        legend.AddThemeConstantOverride("separation", 4);
        column.AddChild(legend);

        AddLabel(legend, "Green — Detection", 14, new Color("#65e58b"));
        AddLabel(legend, "Red — Attack", 14, new Color("#ff7272"));
        AddLabel(legend, "Amber — Forget", 14, new Color("#e8bd68"));

        column.AddChild(new HSeparator());
        AddLabel(column, "Simulation continues while this menu is open.",
            13, new Color("#91aaba"));

        Button close = new()
        {
            Text = "Close  ·  F1 / Esc",
            CustomMinimumSize = new Vector2(0, 38)
        };
        column.AddChild(close);
        close.Pressed += CloseMenu;
    }

    // =========================================================
    // Add consistently styled, mouse-transparent explanatory text.
    private static void AddLabel(
        Node parent, string text, int size, Color colour)
    {
        Label label = new()
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        parent.AddChild(label);
    }
    #endregion

    #region Menu Ownership
    // =========================================================
    // Synchronize current overlay settings before claiming gameplay input.
    private void OpenMenu()
    {
        _collisionToggle.SetPressedNoSignal(_collision.Enabled);
        _rangesToggle.SetPressedNoSignal(_ranges.Enabled);

        _open = true;
        _root.Visible = true;
        _modes.Push(this, PlayerInputMode.DebugMenu);
        _collisionToggle.GrabFocus();
    }

    // =========================================================
    // Close without changing overlay settings or discarding their caches.
    private void CloseMenu()
    {
        if (!_open) return;

        Control focused = GetViewport().GuiGetFocusOwner();
        if (focused != null && _root.IsAncestorOf(focused))
            focused.ReleaseFocus();

        _open = false;
        _root.Visible = false;
        _modes.Release(this);
    }
    #endregion

    #region Overlay Controls
    // =========================================================
    // Toggle collision rendering through its shared public control.
    private void OnCollisionToggled(bool enabled)
    {
        _collision.SetEnabled(enabled);
    }

    // =========================================================
    // Toggle enemy ranges while retaining their cached drawings.
    private void OnRangesToggled(bool enabled)
    {
        _ranges.SetEnabled(enabled);
    }
    #endregion
}