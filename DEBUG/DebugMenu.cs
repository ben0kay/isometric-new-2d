// Builds the F1 menu from registered debug providers.
// Options fill four columns vertically and can be sorted alphabetically.
using Godot;
using System;
using System.Collections.Generic;

public partial class DebugMenu : CanvasLayer
{
    #region Configuration
    [Export(PropertyHint.Range, "1,20,1")]
    public int OptionsPerColumn { get; set; } = 6;

    [Export] public bool SortByName { get; set; } = true;
    [Export] public float ScreenMargin { get; set; } = 32f;
    #endregion

    #region State
    private const int ColumnCount = 4;

    private InputModes _modes;
    private Control _root;
    private HBoxContainer _columns;
    private CheckBox _sortToggle;
    private Label _count;
    private bool _open;

    private readonly List<(Node Owner, DebugOption Option)> _options = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Build the reusable menu shell without knowing any overlay types.
    public override void _Ready()
    {
        Layer = 120;
        _modes = InputModes.For(this);
        BuildInterface();
        SetProcess(false);
        SetProcessInput(true);
    }

    // =========================================================
    // Release this menu's input claim when its world closes.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_modes))
            _modes.Release(this);
    }

    // =========================================================
    // Open with F1 and close with F1 or Escape while owning input.
    public override void _Input(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo)
            return;

        if (key.PhysicalKeycode == Key.F1)
        {
            if (_open)
            {
                if (!_modes.OwnsInput(this)) return;
                CloseMenu();
            }
            else
            {
                if (!_modes.GameplayAllowed ||
                    GetViewport().GuiIsDragging())
                    return;

                OpenMenu();
            }

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
    // Build a large screen-inset panel with four scrollable option columns.
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
            Color = new Color(0f, 0f, 0f, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(backdrop);
        backdrop.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        PanelContainer panel = new();
        _root.AddChild(panel);
        panel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        float margin = Mathf.Max(0f, ScreenMargin);
        panel.OffsetLeft = panel.OffsetTop = margin;
        panel.OffsetRight = panel.OffsetBottom = -margin;

        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#14202a"),
            BorderColor = new Color("#486577"),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12, CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12, CornerRadiusBottomRight = 12,
            ContentMarginLeft = 24, ContentMarginRight = 24,
            ContentMarginTop = 22, ContentMarginBottom = 22
        });

        VBoxContainer layout = new();
        layout.AddThemeConstantOverride("separation", 16);
        panel.AddChild(layout);

        HBoxContainer toolbar = new();
        toolbar.AddThemeConstantOverride("separation", 20);
        layout.AddChild(toolbar);

        Label title = MakeLabel("DEBUG MENU", 24, new Color("#dcebf3"));
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        toolbar.AddChild(title);

        _sortToggle = new CheckBox { Text = "Sort by name" };
        _sortToggle.SetPressedNoSignal(SortByName);
        toolbar.AddChild(_sortToggle);
        _sortToggle.Toggled += OnSortChanged;

        Button refresh = new() { Text = "Refresh" };
        toolbar.AddChild(refresh);
        refresh.Pressed += RebuildOptions;

        layout.AddChild(new HSeparator());

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        layout.AddChild(scroll);

        _columns = new HBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        _columns.AddThemeConstantOverride("separation", 24);
        scroll.AddChild(_columns);

        layout.AddChild(new HSeparator());

        HBoxContainer footer = new();
        footer.AddThemeConstantOverride("separation", 20);
        layout.AddChild(footer);

        _count = MakeLabel("", 14, new Color("#91aaba"));
        _count.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        footer.AddChild(_count);

        Button close = new()
        {
            Text = "Close  ·  F1 / Esc",
            CustomMinimumSize = new Vector2(180, 38)
        };
        footer.AddChild(close);
        close.Pressed += CloseMenu;
    }

    // =========================================================
    // Create consistently styled explanatory text that wraps inside its column.
    private static Label MakeLabel(string text, int size, Color colour)
    {
        Label label = new()
        {
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", colour);
        return label;
    }

    // =========================================================
    // Rediscover options only when opening, refreshing or changing sorting.
    private void RebuildOptions()
    {
        _options.Clear();

        Node world = GetParent().GetParent();
        foreach (Node node in GetTree().GetNodesInGroup(DebugOption.Group))
        {
            if (!GodotObject.IsInstanceValid(node) ||
                node.IsQueuedForDeletion() ||
                !world.IsAncestorOf(node) ||
                node is not IDebugOptionProvider provider)
                continue;

            foreach (DebugOption option in provider.GetDebugOptions())
            {
                if (option == null || string.IsNullOrWhiteSpace(option.Name) ||
                    option.Read == null || option.Write == null)
                    continue;

                _options.Add((node, option));
            }
        }

        _options.Sort((first, second) =>
        {
            if (!SortByName)
            {
                int order = first.Option.Order.CompareTo(second.Option.Order);
                if (order != 0) return order;
            }

            return StringComparer.OrdinalIgnoreCase.Compare(
                first.Option.Name, second.Option.Name);
        });

        foreach (Node child in _columns.GetChildren())
        {
            _columns.RemoveChild(child);
            child.QueueFree();
        }

        int capacity = Math.Max(
            Math.Max(1, OptionsPerColumn),
            (_options.Count + ColumnCount - 1) / ColumnCount);

        for (int columnIndex = 0; columnIndex < ColumnCount; columnIndex++)
        {
            VBoxContainer column = new()
            {
                CustomMinimumSize = new Vector2(200, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            column.AddThemeConstantOverride("separation", 16);
            _columns.AddChild(column);

            int start = columnIndex * capacity;
            int end = Math.Min(start + capacity, _options.Count);
            for (int index = start; index < end; index++)
                AddOption(column, _options[index].Owner, _options[index].Option);
        }

        _count.Text = $"{_options.Count} options · Simulation continues";
    }

    // =========================================================
    // Generate a checkbox and its provider-defined legend without custom handlers.
    private static void AddOption(
        VBoxContainer column, Node owner, DebugOption option)
    {
        VBoxContainer entry = new();
        entry.AddThemeConstantOverride("separation", 5);
        column.AddChild(entry);

        CheckBox toggle = new() { Text = option.Name };
        toggle.SetPressedNoSignal(option.Read());
        entry.AddChild(toggle);

        toggle.Toggled += enabled =>
        {
            if (!GodotObject.IsInstanceValid(owner) ||
                owner.IsQueuedForDeletion())
            {
                toggle.Disabled = true;
                return;
            }

            option.Write(enabled);
            toggle.SetPressedNoSignal(option.Read());
        };

        foreach (DebugLegend legend in option.Legends)
            entry.AddChild(MakeLabel(legend.Text, 13, legend.Colour));

        entry.AddChild(new HSeparator());
    }

    // =========================================================
    // Reorder options without changing any overlay's enabled state.
    private void OnSortChanged(bool enabled)
    {
        SortByName = enabled;
        RebuildOptions();
    }
    #endregion

    #region Menu Ownership
    // =========================================================
    // Discover current providers and claim gameplay input without pausing.
    private void OpenMenu()
    {
        RebuildOptions();
        _sortToggle.SetPressedNoSignal(SortByName);
        _open = true;
        _root.Visible = true;
        _modes.Push(this, PlayerInputMode.DebugMenu);
        _sortToggle.GrabFocus();
    }

    // =========================================================
    // Close while retaining the selected overlay settings.
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
}