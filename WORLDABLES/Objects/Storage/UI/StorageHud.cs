// Presents nearby containers through shared storage and player inventory APIs.
// Proximity is checked at ten hertz; slot controls refresh through inventory events.
using Godot;
using System.Collections.Generic;

public partial class StorageHud : CanvasLayer
{
    #region State
    private Player _player;
    private PlayerInventory _inventory;
    private InventoryHud _backpack;
    private WorldStorage _nearby, _opened;
    private Control _root;
    private PanelContainer _window;
    private Label _prompt, _title, _totals, _notice;
    private VBoxContainer _bagList, _containerList;
    private readonly List<Button> _bagButtons = new();
    private readonly List<Button> _containerButtons = new();
    private double _scanTimer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve this player's components and build reusable interface controls.
    public override void _Ready()
    {
        Layer = 21;
        _player = GetParent<Player>();
        _inventory = _player.GetNode<PlayerInventory>("Systems/Inventory");
        _backpack = _player.GetNode<InventoryHud>("InventoryHud");
        BuildUi();
        _inventory.Changed += Refresh;
        GetViewport().SizeChanged += FitWindow;
        FitWindow();
    }

    // =========================================================
    // Search periodically and close when the container becomes inaccessible.
    public override void _Process(double delta)
    {
        _scanTimer -= delta;
        if (_scanTimer > 0.0) return;
        _scanTimer = 0.1;

        if (_opened != null)
        {
            if (!GodotObject.IsInstanceValid(_opened) ||
                !_opened.CanInteract(_player)) Close();
            return;
        }

        _nearby = null;
        float best = float.PositiveInfinity;
        foreach (Node node in GetTree().GetNodesInGroup("world_storage"))
        {
            if (node is not WorldStorage storage ||
                !storage.CanInteract(_player)) continue;
            float distance = storage.Host.GlobalPosition.DistanceSquaredTo(
                _player.GlobalPosition);
            if (distance >= best) continue;
            best = distance;
            _nearby = storage;
        }

        _prompt.Text = _nearby != null && !_backpack.IsOpen
            ? $"[E] {_nearby.Definition.DisplayName}" : "";
    }

    // =========================================================
    // Release subscriptions and inventory input protection when removed.
    public override void _ExitTree()
    {
        Close();
        if (GodotObject.IsInstanceValid(_inventory))
            _inventory.Changed -= Refresh;
        GetViewport().SizeChanged -= FitWindow;
    }
    #endregion

    #region Layout
    // =========================================================
    // Build a centered transfer window and a separate interaction prompt.
    private void BuildUi()
    {
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        AddChild(_root);
        _root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

        _prompt = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _root.AddChild(_prompt);
        _prompt.SetAnchorsPreset(Control.LayoutPreset.TopWide);
        _prompt.OffsetTop = 180;
        _prompt.OffsetBottom = 210;

        _window = new PanelContainer { Visible = false };
        _root.AddChild(_window);
        _window.SetAnchorsPreset(Control.LayoutPreset.Center);
        _window.OffsetLeft = -350;
        _window.OffsetRight = 350;
        _window.OffsetTop = -230;
        _window.OffsetBottom = 230;
        _window.PivotOffset = new Vector2(350, 230);
        _window.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("#12202b"),
            BorderColor = new Color("#577981"),
            BorderWidthLeft = 2, BorderWidthRight = 2,
            BorderWidthTop = 2, BorderWidthBottom = 2
        });

        MarginContainer margin = new();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
            margin.AddThemeConstantOverride("margin_" + side, 14);
        _window.AddChild(margin);

        VBoxContainer contents = new();
        contents.AddThemeConstantOverride("separation", 8);
        margin.AddChild(contents);

        HBoxContainer heading = new();
        contents.AddChild(heading);
        _title = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        heading.AddChild(_title);
        Button close = new()
        {
            Text = "Close [E / Esc]",
            FocusMode = Control.FocusModeEnum.None
        };
        heading.AddChild(close);
        close.Pressed += Close;

        _totals = new Label();
        contents.AddChild(_totals);
        HBoxContainer columns = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        columns.AddThemeConstantOverride("separation", 16);
        contents.AddChild(columns);
        _bagList = AddColumn(columns, "BACKPACK → DEPOSIT");
        _containerList = AddColumn(columns, "CONTAINER → TAKE");

        _notice = new Label
        {
            Text = "Click an occupied slot to transfer its complete stack.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        contents.AddChild(_notice);
    }

    // =========================================================
    // Create one scrolling stack column.
    private static VBoxContainer AddColumn(Node parent, string title)
    {
        VBoxContainer column = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        parent.AddChild(column);
        column.AddChild(new Label { Text = title });
        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        column.AddChild(scroll);
        VBoxContainer list = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(list);
        return list;
    }

    // =========================================================
    // Scale layout only when viewport dimensions change.
    private void FitWindow()
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Clamp(Mathf.Min(
            (size.X - 24f) / 700f, (size.Y - 40f) / 460f), 0.2f, 1f);
        _window.Scale = Vector2.One * scale;
    }

    // =========================================================
    // Rebuild controls only when the required slot count changes.
    private void EnsureButtons(List<Button> buttons, VBoxContainer list,
        int count, bool deposit)
    {
        if (buttons.Count == count) return;
        foreach (Button button in buttons)
        {
            list.RemoveChild(button);
            button.QueueFree();
        }
        buttons.Clear();

        for (int i = 0; i < count; i++)
        {
            int index = i;
            Button button = new()
            {
                FocusMode = Control.FocusModeEnum.None,
                CustomMinimumSize = new Vector2(0, 36),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                Alignment = HorizontalAlignment.Left
            };
            button.Pressed += () => Transfer(index, deposit);
            list.AddChild(button);
            buttons.Add(button);
        }
    }
    #endregion

    #region Interaction
    // =========================================================
    // Consume closing keys before they reach other gameplay handlers.
    public override void _Input(InputEvent input)
    {
        if (_opened == null || input is not InputEventKey key ||
            !key.Pressed || key.Echo) return;
        if (key.PhysicalKeycode != Key.E && key.PhysicalKeycode != Key.Escape &&
            key.PhysicalKeycode != Key.Delete) return;

        Close();
        GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Open the nearest eligible container while the backpack window is closed.
    public override void _UnhandledInput(InputEvent input)
    {
        if (input is not InputEventKey key || !key.Pressed || key.Echo ||
            key.PhysicalKeycode != Key.E || _backpack.IsOpen ||
            _opened != null || !GodotObject.IsInstanceValid(_nearby) ||
            !_nearby.CanInteract(_player)) return;

        _opened = _nearby;
        _opened.Changed += Refresh;
        _backpack.SetExternalWindowOpen(true);
        _window.Show();
        _prompt.Text = "";
        _notice.Text = "Click an occupied slot to transfer its complete stack.";
        Refresh();
        GetViewport().SetInputAsHandled();
    }

    // =========================================================
    // Disconnect the container and release the shared input block.
    private void Close()
    {
        if (GodotObject.IsInstanceValid(_opened))
            _opened.Changed -= Refresh;
        _opened = null;
        _window?.Hide();
        if (GodotObject.IsInstanceValid(_backpack))
            _backpack.SetExternalWindowOpen(false);
        _scanTimer = 0.0;
    }

    // =========================================================
    // Recheck interaction range before requesting a whole-stack transfer.
    private void Transfer(int index, bool deposit)
    {
        if (!GodotObject.IsInstanceValid(_opened) ||
            !_opened.CanInteract(_player))
        {
            Close();
            return;
        }

        string reason;
        bool success = deposit
            ? _opened.TryDeposit(_inventory, index, out reason)
            : _opened.TryWithdraw(_inventory, index, out reason);
        _notice.Text = success ? "Transferred." : reason;
    }
    #endregion

    #region Display
    // =========================================================
    // Refresh cached totals and stack labels after content changes.
    private void Refresh()
    {
        if (!GodotObject.IsInstanceValid(_opened)) return;
        EnsureButtons(_bagButtons, _bagList, _inventory.BagSlotCount, true);
        EnsureButtons(_containerButtons, _containerList, _opened.SlotCount, false);

        StorageDefinition definition = _opened.Definition;
        _title.Text = definition.DisplayName;
        _totals.Text =
            $"Container: {_opened.WeightKg:0.0}/{definition.MaximumWeightKg:0.#} kg" +
            $"   {_opened.VolumeLitres:0.0}/{definition.CapacityLitres:0.#} L\n" +
            $"Carried: {_inventory.TotalWeightKg:0.0} kg" +
            $"   {_inventory.UsedVolumeLitres:0.0} L";

        for (int i = 0; i < _bagButtons.Count; i++)
            RefreshButton(_bagButtons[i], i, _inventory.GetStack(
                new InventoryAddress(InventoryArea.Bag, i)));
        for (int i = 0; i < _containerButtons.Count; i++)
            RefreshButton(_containerButtons[i], i, _opened.GetStack(i));
    }

    // =========================================================
    // Disable empty-slot requests and display stack quantities.
    private static void RefreshButton(Button button, int index, InventoryStack stack)
    {
        button.Disabled = stack.IsEmpty;
        button.Text = stack.IsEmpty ? $"{index + 1}. Empty"
            : $"{index + 1}. {stack.Item.DisplayName} ×{stack.Count}";
    }
    #endregion
}