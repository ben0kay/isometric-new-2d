// Displays physical inventory stacks and their quick-access badges.
// Dragging between inventory slots transfers items; hotbar drops only bind shortcuts.
using Godot;

public partial class InventorySlot : Button
{
    #region Configuration
    public PlayerInventory Inventory { get; set; }
    public PlayerHotbar Hotbar { get; set; }
    public InventoryAddress Address { get; set; }
    public int SlotSize { get; set; } = 76;
    #endregion

    #region Lifecycle
    // =========================================================
    // Configure a reusable inventory cell.
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(SlotSize, SlotSize);
        FocusMode = FocusModeEnum.None;
        ExpandIcon = true;
        AddThemeFontSizeOverride("font_size", 12);
        AddThemeConstantOverride("icon_max_width", 32);
        AddThemeStyleboxOverride("hover", UIInventoryMaster.Style(
            new Color("#17333dcc"), new Color("#69d9e5")));
        AddThemeStyleboxOverride("pressed", UIInventoryMaster.Style(
            new Color("#20505bcc"), new Color("#a0f8ff")));
        Refresh(false);
    }
    #endregion

    #region Display
    // =========================================================
    // Display stack quantity and the shortcut assigned to this physical slot.
    public void Refresh(bool selected)
    {
        InventoryStack stack = Inventory.GetStack(Address);
        int binding = Hotbar?.GetBinding(Address) ?? -1;
        string badge = binding < 0 ? "" : $"[{(binding + 1) % 10}]\n";
        Text = stack.IsEmpty ? "—" : badge + stack.Item.ShortName +
            (stack.Count > 1 ? $"\n×{stack.Count}" : "");
        Icon = stack.Item?.Icon;
        TooltipText = stack.IsEmpty ? "Empty slot" :
            $"{stack.Item.DisplayName} ×{stack.Count}\n" +
            $"{stack.Item.WeightKg:0.##} kg / " +
            $"{stack.Item.VolumeLitres:0.##} L per unit";
        AddThemeColorOverride("font_color", stack.Item?.Tint ?? Colors.White);
        AddThemeStyleboxOverride("normal", UIInventoryMaster.Style(
            new Color("#0b2029bb"),
            new Color(selected ? "#83edf6" : "#34606d")));
    }
    #endregion

    #region Drag And Drop
    // =========================================================
    // Preview a stack while its physical contents remain in place.
    public override Variant _GetDragData(Vector2 atPosition)
    {
        InventoryStack stack = Inventory.GetStack(Address);
        if (stack.IsEmpty) return default;
        SetDragPreview(new Label
        {
            Text = $"{stack.Item.DisplayName} ×{stack.Count}",
            MouseFilter = MouseFilterEnum.Ignore
        });
        return new Godot.Collections.Dictionary
        {
            ["inventory"] = Inventory,
            ["area"] = (int)Address.Area,
            ["index"] = Address.Index
        };
    }

    // =========================================================
    // Decode an inventory drag belonging to this player.
    public static bool ReadPayload(Variant data, PlayerInventory inventory,
        out InventoryAddress address)
    {
        address = default;
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var payload = data.AsGodotDictionary();
        if (!payload.ContainsKey("inventory") || !payload.ContainsKey("area") ||
            !payload.ContainsKey("index") ||
            payload["inventory"].AsGodotObject() != inventory) return false;

        address = new InventoryAddress(
            (InventoryArea)payload["area"].AsInt32(),
            payload["index"].AsInt32());
        return inventory.HasAddress(address);
    }

    // =========================================================
    // Accept compatible transfers without modifying inventory during hover.
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (!ReadPayload(data, Inventory, out InventoryAddress from) ||
            (from.Area == Address.Area && from.Index == Address.Index))
            return false;

        InventoryStack source = Inventory.GetStack(from);
        return !source.IsEmpty && Inventory.CanPlace(Address, source) &&
            Inventory.CanPlace(from, Inventory.GetStack(Address));
    }

    // =========================================================
    // Commit a validated move, merge, or swap.
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!_CanDropData(atPosition, data)) return;
        ReadPayload(data, Inventory, out InventoryAddress from);
        Inventory.TryMove(from, Address);
    }
    #endregion
}