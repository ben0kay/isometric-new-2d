// Displays a shortcut to an existing inventory stack.
// Dropping onto this control assigns quick access without moving the source.
using Godot;

public partial class HotbarSlot : Button
{
    #region Configuration
    public PlayerInventory Inventory { get; set; }
    public PlayerHotbar Hotbar { get; set; }
    public int Index { get; set; }
    #endregion

    #region Lifecycle
    // =========================================================
    // Configure compact quick-access controls.
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(76, 66);
        FocusMode = FocusModeEnum.None;
        ExpandIcon = true;
        AddThemeFontSizeOverride("font_size", 11);
        AddThemeConstantOverride("icon_max_width", 26);
        Pressed += () => Hotbar.Select(Index);
        Refresh();
    }
    #endregion

    #region Display
    // =========================================================
    // Display the live inventory quantity without owning a duplicate stack.
    public void Refresh()
    {
        InventoryStack stack = Hotbar.GetStack(Index);
        Text = $"{(Index + 1) % 10}\n" +
            (stack.IsEmpty ? "—" : stack.Item.ShortName +
            (stack.Count > 1 ? $" ×{stack.Count}" : ""));
        Icon = stack.Item?.Icon;
        TooltipText = stack.IsEmpty ? "Empty hands" :
            $"{stack.Item.DisplayName}\nRight-click: clear shortcut";
        StyleBoxFlat style = UIInventoryMaster.Style(
            new Color("#0b2029cc"),
            new Color(Index == Hotbar.SelectedSlot ? "#80eff8" : "#34606d"));
        AddThemeStyleboxOverride("normal", style);
        AddThemeStyleboxOverride("hover", UIInventoryMaster.Style(
            new Color("#163d48dd"), new Color("#83edf6")));
        AddThemeStyleboxOverride("pressed", style);
    }

    // =========================================================
    // Clear only the shortcut when right-clicked.
    public override void _GuiInput(InputEvent input)
    {
        if (input is InputEventMouseButton mouse && mouse.Pressed &&
            mouse.ButtonIndex == MouseButton.Right)
        {
            Hotbar.Clear(Index);
            AcceptEvent();
        }
    }
    #endregion

    #region Drag And Drop
    // =========================================================
    // Accept a carried item as a quick-access reference.
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        return InventorySlot.ReadPayload(data, Inventory,
            out InventoryAddress address) &&
            address.Area != InventoryArea.Backpack &&
            !Inventory.GetStack(address).IsEmpty;
    }

    // =========================================================
    // Assign the shortcut while keeping the physical stack in its source.
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!_CanDropData(atPosition, data)) return;
        InventorySlot.ReadPayload(data, Inventory, out InventoryAddress address);
        Hotbar.Bind(Index, address);
    }
    #endregion
}