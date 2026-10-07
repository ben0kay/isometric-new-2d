// Builds the inventory overview: equipment, backpack, vitals, and item inspector.
// Refreshes through gameplay events; clothing placeholders await wearable item data.
using Godot;
using System.Collections.Generic;

public partial class InventoryTab : HBoxContainer
{
    #region Configuration
    public UIInventoryMaster Hud { get; set; }
    #endregion

    #region State
    private readonly List<InventorySlot> _slots = new();
    private readonly List<InventorySlot> _bagSlots = new();
    private readonly List<Label> _reserveLabels = new();
    private readonly List<ProgressBar> _reserveBars = new();
    private GridContainer _bagGrid;
    private Label _bagTitle, _load, _healthText, _itemTitle, _itemDetails, _notice;
    private TextureRect _itemIcon;
    private ProgressBar _healthBar;
    private Button _drop;
    private InventoryAddress? _selected;

    private static readonly PlayerReserve[] DisplayReserves =
{
    PlayerReserve.Stamina,
    PlayerReserve.Oxygen,
    PlayerReserve.Energy,
    PlayerReserve.Food,
    PlayerReserve.Water,
    PlayerReserve.Fatigue
};

    #endregion

    #region Lifecycle
    // =========================================================
    // Build the overview and subscribe to inventory and vital changes.
    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 12);
        BuildEquipment();
        BuildBackpack();
        BuildDetails();

        Hud.Inventory.Changed += Refresh;
        Hud.Inventory.Notice += ShowNotice;
        Hud.Hotbar.Changed += Refresh;
        Hud.Vitals.Changed += RefreshVitals;
        Refresh();
        RefreshVitals();
    }

    // =========================================================
    // Release tab subscriptions when the HUD is removed.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(Hud.Inventory))
        {
            Hud.Inventory.Changed -= Refresh;
            Hud.Inventory.Notice -= ShowNotice;
        }
        if (GodotObject.IsInstanceValid(Hud.Hotbar))
            Hud.Hotbar.Changed -= Refresh;
        if (GodotObject.IsInstanceValid(Hud.Vitals))
            Hud.Vitals.Changed -= RefreshVitals;
    }
    #endregion

    #region Equipment
    // =========================================================
    // Arrange wearable placeholders and carried equipment around a player preview.
    private void BuildEquipment()
    {
        VBoxContainer section = Hud.Section(this, "EQUIPMENT");
        section.GetParent<Control>().CustomMinimumSize = new Vector2(320, 0);

        HBoxContainer body = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        section.AddChild(body);
        VBoxContainer clothing = new();
        body.AddChild(clothing);
        foreach (string name in new[] { "HEAD", "CHEST", "HANDS", "LEGS", "FEET" })
        {
            clothing.AddChild(new Label { Text = name });
            Button placeholder = new()
            {
                Text = "—", Disabled = true,
                CustomMinimumSize = new Vector2(70, 62),
                TooltipText = "Reserved for wearable equipment."
            };
            clothing.AddChild(placeholder);
        }

        VBoxContainer portrait = new()
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        body.AddChild(portrait);
        Label figure = new()
{
    Text = "PLAYER",
    HorizontalAlignment = HorizontalAlignment.Center,
    VerticalAlignment = VerticalAlignment.Center,
    SizeFlagsVertical = SizeFlags.ExpandFill
};
        figure.AddThemeFontSizeOverride("font_size", 24);
        figure.AddThemeColorOverride("font_color", new Color("#7bd5df"));
        portrait.AddChild(figure);

        VBoxContainer equipment = new();
        body.AddChild(equipment);
        equipment.AddChild(new Label { Text = "BACKPACK" });
        AddSlot(equipment, new InventoryAddress(InventoryArea.Backpack), 70);

        for (int i = 0; i < Hud.Inventory.Equipment.ToolSlotCount; i++)
        {
            equipment.AddChild(new Label { Text = $"GEAR {i + 1}" });
            AddSlot(equipment, new InventoryAddress(InventoryArea.Tools, i), 70);
        }

        Label note = new()
        {
            Text = "Equipped gear contributes to total weight.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        section.AddChild(note);
    }
    #endregion

    #region Backpack
    // =========================================================
    // Place backpack cells in the middle with physical carrying totals below.
    private void BuildBackpack()
    {
        VBoxContainer section = Hud.Section(this, "BACKPACK");
        section.GetParent<Control>().CustomMinimumSize = new Vector2(510, 0);
        _bagTitle = new Label();
        section.AddChild(_bagTitle);

        ScrollContainer scroll = new()
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        section.AddChild(scroll);
        _bagGrid = new GridContainer { Columns = 6 };
        _bagGrid.AddThemeConstantOverride("h_separation", 5);
        _bagGrid.AddThemeConstantOverride("v_separation", 5);
        scroll.AddChild(_bagGrid);

        _load = new Label();
        _notice = new Label
        {
            Text = "Drag to quick access to assign a shortcut.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        section.AddChild(_load);
        section.AddChild(_notice);
    }

    // =========================================================
    // Construct a selectable physical slot.
    private InventorySlot AddSlot(Node parent, InventoryAddress address, int size)
    {
        InventorySlot slot = new()
        {
            Inventory = Hud.Inventory, Hotbar = Hud.Hotbar,
            Address = address, SlotSize = size
        };
        parent.AddChild(slot);
        slot.Pressed += () => SelectItem(address);
        _slots.Add(slot);
        return slot;
    }

    // =========================================================
    // Rebuild backpack controls only when equipped capacity changes.
    private void EnsureBagSlots()
    {
        if (_bagSlots.Count == Hud.Inventory.BagSlotCount) return;
        foreach (InventorySlot slot in _bagSlots)
        {
            _slots.Remove(slot);
            _bagGrid.RemoveChild(slot);
            slot.QueueFree();
        }
        _bagSlots.Clear();
        for (int i = 0; i < Hud.Inventory.BagSlotCount; i++)
            _bagSlots.Add(AddSlot(_bagGrid,
                new InventoryAddress(InventoryArea.Bag, i), 76));
    }
    #endregion

    #region Vitals And Inspector
// =========================================================
// Show all player reserves above the selected-item inspector.
private void BuildDetails()
{
    VBoxContainer right = new()
    {
        CustomMinimumSize = new Vector2(320, 0),
        SizeFlagsHorizontal = SizeFlags.ExpandFill
    };
    AddChild(right);
    right.AddThemeConstantOverride("separation", 12);

    VBoxContainer stats = Hud.Section(right, "VITALS");
    stats.AddThemeConstantOverride("separation", 4);

    _healthText = new Label();
    stats.AddChild(_healthText);
    _healthBar = AddBar(stats, new Color("#ee6575"));

    foreach (PlayerReserve reserve in DisplayReserves)
    {
        Label label = new();
        stats.AddChild(label);
        _reserveLabels.Add(label);

        Color tint = reserve switch
        {
            PlayerReserve.Stamina => new Color("#4adc98"),
            PlayerReserve.Oxygen => new Color("#55cfeb"),
            PlayerReserve.Energy => new Color("#b795ed"),
            PlayerReserve.Food => new Color("#dcb96b"),
            PlayerReserve.Water => new Color("#559fee"),
            PlayerReserve.Fatigue => new Color("#e79767"),
            _ => Colors.White
        };

        ProgressBar bar = AddBar(stats, tint);
        bar.TooltipText = reserve == PlayerReserve.Fatigue
            ? "Accumulated fatigue: lower is better."
            : "Remaining reserve: higher is better.";
        _reserveBars.Add(bar);
    }

    VBoxContainer inspector = Hud.Section(right, "ITEM INFORMATION");
    _itemTitle = new Label
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart
    };
    inspector.AddChild(_itemTitle);

    _itemIcon = new TextureRect
    {
        CustomMinimumSize = new Vector2(80, 80),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
    };
    inspector.AddChild(_itemIcon);

    _itemDetails = new Label
    {
        AutowrapMode = TextServer.AutowrapMode.WordSmart,
        SizeFlagsVertical = SizeFlags.ExpandFill
    };
    inspector.AddChild(_itemDetails);

    _drop = new Button
    {
        Text = "DROP STACK",
        FocusMode = FocusModeEnum.None,
        CustomMinimumSize = new Vector2(0, 36)
    };
    inspector.AddChild(_drop);
    _drop.Pressed += DropSelected;
}

// =========================================================
// Create compact reserve bars without the outer panel's content padding.
private static ProgressBar AddBar(Node parent, Color tint)
{
    ProgressBar bar = new()
    {
        CustomMinimumSize = new Vector2(0, 10),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        ShowPercentage = false
    };

    bar.AddThemeStyleboxOverride("background", new StyleBoxFlat
    {
        BgColor = new Color("#18343c"),
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3
    });

    bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
    {
        BgColor = tint,
        CornerRadiusTopLeft = 3,
        CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3,
        CornerRadiusBottomRight = 3
    });

    parent.AddChild(bar);
    return bar;
}

// =========================================================
// Display live reserves with enough precision to see gradual survival changes.
private void RefreshVitals()
{
    Health health = Hud.Vitals.Health;
    _healthText.Text =
        $"{health.VitalityLabel}   {health.Current}/{health.MaxHealth}";
    _healthBar.MaxValue = Mathf.Max(1, health.MaxHealth);
    _healthBar.Value = health.Current;

    for (int i = 0; i < DisplayReserves.Length; i++)
    {
        PlayerReserve reserve = DisplayReserves[i];
        float current = Hud.Vitals.GetCurrent(reserve);
        float maximum = Hud.Vitals.GetMaximum(reserve);

        string name = reserve switch
        {
            PlayerReserve.Food => "Hunger reserve",
            PlayerReserve.Water => "Thirst reserve",
            _ => reserve.ToString()
        };

        _reserveLabels[i].Text =
            $"{name}   {current:0.00}/{maximum:0.#}";
        _reserveBars[i].MaxValue = Mathf.Max(1f, maximum);
        _reserveBars[i].Value = current;
    }
}

    // =========================================================
    // Inspect an inventory cell without changing the active hotbar selection.
    private void SelectItem(InventoryAddress address)
    {
        _selected = address;
        Refresh();
    }

    // =========================================================
    // Drop the selected physical stack through the inventory coordinator.
    private void DropSelected()
    {
        if (_selected.HasValue) Hud.Inventory.TryDrop(_selected.Value);
    }

    // =========================================================
    // Display only the item's current physical data and usable attack settings.
    private void RefreshInspector()
    {
        InventoryStack stack = _selected.HasValue
            ? Hud.Inventory.GetStack(_selected.Value) : default;
        _drop.Disabled = stack.IsEmpty;
        _itemIcon.Texture = stack.Item?.Icon;

        if (stack.IsEmpty)
        {
            _itemTitle.Text = "Select an item";
            _itemDetails.Text = "Click an inventory slot to inspect its contents.";
            return;
        }

        ItemDefinition item = stack.Item;
        _itemTitle.Text = item.DisplayName;
        _itemDetails.Text =
            $"Quantity: {stack.Count}\n" +
            $"Weight each: {item.WeightKg:0.##} kg\n" +
            $"Stack weight: {item.WeightKg * stack.Count:0.##} kg\n" +
            $"Volume each: {item.VolumeLitres:0.##} L\n" +
            $"Stack volume: {item.VolumeLitres * stack.Count:0.##} L\n" +
            $"Maximum stack: {item.MaxStack}";

        if (item.Attack is MiningAttack mining)
            _itemDetails.Text += $"\n\nMining strength: {mining.MiningStrength}" +
                $"\nMining power: {mining.MiningPower:0.##}";
        else if (item.Attack != null)
            _itemDetails.Text += "\n\nUsable weapon";
    }
    #endregion

    #region Refresh
// =========================================================
// Refresh definition-driven slots and player-owned carrying limits.
private void Refresh()
{
    EnsureBagSlots();
    int occupied = 0;

    for (int i = 0; i < Hud.Inventory.BagSlotCount; i++)
        if (!Hud.Inventory.GetStack(
            new InventoryAddress(InventoryArea.Bag, i)).IsEmpty)
            occupied++;

    BackpackDefinition pack = Hud.Inventory.Equipment.Backpack;
    _bagTitle.Text = $"{pack?.DisplayName ?? "NO BACKPACK"}" +
        $"   {occupied}/{Hud.Inventory.BagSlotCount} slots";

    _load.Text =
        $"WEIGHT  {Hud.Inventory.TotalWeightKg:0.0}/" +
        $"{Hud.Inventory.Rules.MaximumWeightKg:0.#} kg\n" +
        $"VOLUME  {Hud.Inventory.UsedVolumeLitres:0.0}/" +
        $"{pack?.CapacityLitres ?? 0f:0.#} L" +
        $"     MOVEMENT  {Hud.Inventory.MovementFactor * 100f:0}%";

    foreach (InventorySlot slot in _slots)
        slot.Refresh(_selected.HasValue &&
            _selected.Value.Area == slot.Address.Area &&
            _selected.Value.Index == slot.Address.Index);

    RefreshInspector();
}

    // =========================================================
    // Display inventory collection and transaction feedback.
    private void ShowNotice(string message)
    {
        _notice.Text = message;
    }
    #endregion
}