// Owns inventory window sizing, tab registration, shared styling, and HUD input.
// Tab scripts own their contents; the world keeps running while the window is open.
using Godot;
using System.Collections.Generic;

public partial class UIInventoryMaster : CanvasLayer
{
    #region Configuration
    [ExportGroup("Window")]
[Export] public Vector2 WindowSize { get; set; } = new(1480, 700);
    [Export] public float ScreenMargin { get; set; } = 24f;
    [Export] public float PanelOpacity { get; set; } = 0.88f;
    #endregion

    #region State
    public Player Player { get; private set; }
    public PlayerInventory Inventory { get; private set; }
    public PlayerHotbar Hotbar { get; private set; }
    public PlayerVitals Vitals { get; private set; }
    public bool IsOpen => _window?.Visible == true;
    public bool ExternalWindowOpen { get; private set; }
    public bool BlocksWorldMovement => IsOpen || ExternalWindowOpen;

    private Control _root, _hotbarRoot;
    private PanelContainer _window, _hotbarPanel;
    private VBoxContainer _contents;
    private HBoxContainer _tabs;
    private InventoryTab _inventoryTab;
    private MiningEmitter _mining;
    private Label _active;
    private bool _blockUntilRelease;
    private readonly List<HotbarSlot> _hotbarSlots = new();
    private readonly List<Control> _tabPages = new();
    private readonly List<Button> _tabButtons = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve gameplay components and create the master window.
    public override void _Ready()
    {
        Layer = 20;
        Player = GetParent<Player>();
        Inventory = Player.GetNode<PlayerInventory>("Systems/Inventory");
        Hotbar = Player.GetNode<PlayerHotbar>("Systems/Hotbar");
        Vitals = Player.GetNode<PlayerVitals>("Systems/Vitals");
        _mining = Player.GetNode<MiningEmitter>("Systems/MiningEmitter");

        BuildUi();
        Hotbar.Changed += RefreshHotbar;
        GetViewport().SizeChanged += FitWindow;
        RefreshHotbar();
        FitWindow();
    }

    // =========================================================
    // Disconnect master HUD subscriptions.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Hotbar))
            Hotbar.Changed -= RefreshHotbar;
        GetViewport().SizeChanged -= FitWindow;
    }
    #endregion

    #region Shared Styling
    // =========================================================
    // Create subtle translucent panels with a thin cyan border.
    public static StyleBoxFlat Style(Color background, Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = background, BorderColor = border,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
            ContentMarginLeft = 12, ContentMarginRight = 12,
            ContentMarginTop = 10, ContentMarginBottom = 10
        };
    }

// =========================================================
// Build a shared panel with enough initial width for wrapped text.
public VBoxContainer Section(Node parent, string title)
{
    PanelContainer panel = new()
    {
        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        SizeFlagsVertical = Control.SizeFlags.ExpandFill
    };
    panel.AddThemeStyleboxOverride("panel", Style(
        new Color(0.025f, 0.09f, 0.12f,
            Mathf.Clamp(PanelOpacity, 0f, 1f)),
        new Color("#326475")));
    parent.AddChild(panel);

    VBoxContainer contents = new()
    {
        CustomMinimumSize = new Vector2(240, 0)
    };
    contents.AddThemeConstantOverride("separation", 10);
    panel.AddChild(contents);

    Label heading = new() { Text = title };
    heading.AddThemeColorOverride("font_color", new Color("#76e5ef"));
    contents.AddChild(heading);
    return contents;
}
    #endregion

    #region Layout
    // =========================================================
    // Build tabs above the inventory page and a separate bottom hotbar.
    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _window = new PanelContainer { Visible = false };
        _root.AddChild(_window);
        _window.AddThemeStyleboxOverride("panel", Style(
            new Color(0.02f, 0.055f, 0.08f, 0.35f),
            new Color("#407686")));
        _contents = new VBoxContainer();
        _contents.AddThemeConstantOverride("separation", 12);
        _window.AddChild(_contents);

        _tabs = new HBoxContainer();
        _contents.AddChild(_tabs);

        _inventoryTab = new InventoryTab
        {
            Hud = this,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        AddTab("INVENTORY", _inventoryTab);

        Control spacer = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _tabs.AddChild(spacer);
        Button close = new()
        {
            Text = "CLOSE [DELETE]",
            FocusMode = Control.FocusModeEnum.None
        };
        _tabs.AddChild(close);
        close.Pressed += Toggle;

        _hotbarRoot = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(_hotbarRoot);
        _hotbarPanel = new PanelContainer();
        _hotbarRoot.AddChild(_hotbarPanel);
        _hotbarPanel.AddThemeStyleboxOverride("panel", Style(
            new Color("#071b25cc"), new Color("#407686")));

        VBoxContainer hotbarContents = new();
        _hotbarPanel.AddChild(hotbarContents);
        _active = new Label { Text = "QUICK ACCESS" };
        _active.AddThemeFontSizeOverride("font_size", 12);
        hotbarContents.AddChild(_active);

        HBoxContainer slots = new();
        slots.AddThemeConstantOverride("separation", 6);
        hotbarContents.AddChild(slots);
        for (int i = 0; i < Hotbar.SlotCount; i++)
        {
            HotbarSlot slot = new()
            {
                Inventory = Inventory, Hotbar = Hotbar, Index = i
            };
            slots.AddChild(slot);
            _hotbarSlots.Add(slot);
        }
        SelectTab(0);
    }

    // =========================================================
    // Register a future tab with its own independent content script.
    public void AddTab(string title, Control page)
    {
        int index = _tabPages.Count;
        Button button = new()
        {
            Text = title,
            CustomMinimumSize = new Vector2(150, 34),
            FocusMode = Control.FocusModeEnum.None
        };
        button.Pressed += () => SelectTab(index);
        _tabs.AddChild(button);
        _contents.AddChild(page);
        _tabButtons.Add(button);
        _tabPages.Add(page);
    }

    // =========================================================
    // Display one tab within the existing master window.
    public void SelectTab(int index)
    {
        for (int i = 0; i < _tabPages.Count; i++)
        {
            _tabPages[i].Visible = i == index;
            _tabButtons[i].AddThemeColorOverride("font_color",
                i == index ? new Color("#7ce9f2") : new Color("#91a8b1"));
        }
    }

// =========================================================
// Center the actual container size and keep it above the bottom hotbar.
private void FitWindow()
{
    Vector2 screen = GetViewport().GetVisibleRect().Size;
    float margin = Mathf.Max(8f, ScreenMargin);

    Vector2 hotbarMinimum = _hotbarPanel.GetCombinedMinimumSize();
    Vector2 hotbarSize = new(
        Mathf.Max(Hotbar.SlotCount * 82f + 18f, hotbarMinimum.X),
        Mathf.Max(100f, hotbarMinimum.Y));

    float hotbarScale = Mathf.Min(1f,
        Mathf.Max(1f, screen.X - margin * 2f) / hotbarSize.X);

    _hotbarPanel.Size = hotbarSize;
    _hotbarRoot.Scale = Vector2.One * hotbarScale;
    _hotbarRoot.Position = new Vector2(
        (screen.X - hotbarSize.X * hotbarScale) * 0.5f,
        screen.Y - hotbarSize.Y * hotbarScale - margin);

    Vector2 minimum = _window.GetCombinedMinimumSize();
    Vector2 design = new(
        Mathf.Max(WindowSize.X, minimum.X),
        Mathf.Max(WindowSize.Y, minimum.Y));

    float availableWidth = Mathf.Max(1f, screen.X - margin * 2f);
    float availableHeight = Mathf.Max(1f,
        _hotbarRoot.Position.Y - margin * 2f);

    float scale = Mathf.Min(1f, Mathf.Min(
        availableWidth / design.X,
        availableHeight / design.Y));

    _window.Size = design;
    _window.Scale = Vector2.One * scale;
    _window.Position = new Vector2(
        (screen.X - design.X * scale) * 0.5f,
        margin + (availableHeight - design.Y * scale) * 0.5f);
}

    // =========================================================
    // Update quick-access labels through inventory and selection events.
    private void RefreshHotbar()
    {
        _active.Text = $"QUICK ACCESS   |   " +
            (Hotbar.CurrentItem?.DisplayName ?? "Empty hands");
        foreach (HotbarSlot slot in _hotbarSlots) slot.Refresh();
    }
    #endregion

    #region Input
    // =========================================================
    // Toggle inventory and select shortcuts using number keys or scrolling.
    public override void _Input(InputEvent input)
    {
        if (ExternalWindowOpen) return;

        if (input is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.PhysicalKeycode == Key.Delete ||
                (IsOpen && key.PhysicalKeycode == Key.Escape))
            {
                if (!GetViewport().GuiIsDragging()) Toggle();
                GetViewport().SetInputAsHandled();
                return;
            }

            int index = key.PhysicalKeycode == Key.Key0 ? 9 :
                (int)key.PhysicalKeycode - (int)Key.Key1;
            if (!IsOpen && index >= 0 && index < Hotbar.SlotCount)
            {
                Hotbar.Select(index);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (IsOpen || input is not InputEventMouseButton mouse ||
            !mouse.Pressed) return;

        int direction = mouse.ButtonIndex == MouseButton.WheelDown ? 1 :
            mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 0;
        if (direction == 0 || PointerOverHotbar() && GetViewport().GuiIsDragging())
            return;

        Hotbar.Cycle(direction);
        GetViewport().SetInputAsHandled();
    }

// =========================================================
// Open the live-world interface and refit after containers update.
private void Toggle()
{
    _window.Visible = !_window.Visible;
    _blockUntilRelease = true;
    _mining.Stop();

    if (_window.Visible)
        Callable.From(FitWindow).CallDeferred();
}

    // =========================================================
    // Detect quick-access UI clicks before they can fire into the world.
    private bool PointerOverHotbar()
    {
        return _hotbarPanel.GetGlobalRect().HasPoint(
            _root.GetGlobalMousePosition());
    }

    // =========================================================
    // Block world actions during inventory use and UI clicks.
    public bool BlocksWorldAttack()
    {
        if (_blockUntilRelease)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left))
                _blockUntilRelease = false;
            return true;
        }
        return IsOpen || ExternalWindowOpen ||
            GetViewport().GuiIsDragging() || PointerOverHotbar();
    }

    // =========================================================
    // Preserve compatibility with your separate container window.
    public void SetExternalWindowOpen(bool open)
    {
        if (ExternalWindowOpen == open) return;
        ExternalWindowOpen = open;
        _blockUntilRelease = true;
        _mining.Stop();
    }
    #endregion
}