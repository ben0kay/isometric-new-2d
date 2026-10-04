// Displays a slot address and requests validated inventory transfers.
// Native drag/drop supplies previews without moving items until a drop succeeds.
using Godot;

public partial class InventorySlot : Button
{
    #region Configuration
    public PlayerInventory Inventory { get; set; }
    public InventoryAddress Address { get; set; }
    private StyleBoxFlat _normal;
    private StyleBoxFlat _selected;
    #endregion

    #region Lifecycle
    // =========================================================
    // Create cached slot styles and configure compact text/icon display.
    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(66, 70);
        FocusMode = FocusModeEnum.None;
        ExpandIcon = true;
        AddThemeFontSizeOverride("font_size", 12);
        AddThemeConstantOverride("icon_max_width", 24);

        _normal = MakeStyle(new Color("#43515b"));
        _selected = MakeStyle(new Color("#80e5ef"));
        AddThemeStyleboxOverride("hover", MakeStyle(new Color("#879aa2")));
        AddThemeStyleboxOverride("pressed", _selected);
    }

    // =========================================================
    // Create a dark reusable slot with a clear border.
    private static StyleBoxFlat MakeStyle(Color border)
    {
        return new StyleBoxFlat
        {
            BgColor = new Color("#172128"),
            BorderColor = border,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5,
            CornerRadiusTopRight = 5,
            CornerRadiusBottomLeft = 5,
            CornerRadiusBottomRight = 5
        };
    }
    #endregion

    #region Display
    // =========================================================
    // Refresh item text, count, tooltip, and equipment selection.
    public void Refresh(bool selected)
    {
        InventoryStack stack = Inventory.GetStack(Address);
        string prefix = Address.Area == InventoryArea.Tools
            ? $"{Address.Index + 1}\n" : "";

        Text = stack.IsEmpty ? prefix + "—"
            : prefix + stack.Item.ShortName +
                (stack.Count > 1 ? $"\n×{stack.Count}" : "");
        Icon = stack.Item?.Icon;
        TooltipText = stack.IsEmpty ? "Empty slot" :
            $"{stack.Item.DisplayName} ×{stack.Count}\n" +
            $"{stack.Item.WeightKg:0.##} kg / " +
            $"{stack.Item.VolumeLitres:0.##} L per unit";

        AddThemeColorOverride("font_color", stack.Item?.Tint ?? Colors.White);
        AddThemeStyleboxOverride("normal", selected ? _selected : _normal);
    }
    #endregion

    #region Drag And Drop
    // =========================================================
    // Preview an existing stack without removing it from its source.
    public override Variant _GetDragData(Vector2 atPosition)
    {
        InventoryStack stack = Inventory.GetStack(Address);
        if (stack.IsEmpty) return default;

        Label preview = new()
        {
            Text = $"{stack.Item.DisplayName} ×{stack.Count}",
            MouseFilter = MouseFilterEnum.Ignore
        };
        preview.AddThemeColorOverride("font_color", stack.Item.Tint);
        SetDragPreview(preview);

        return new Godot.Collections.Dictionary
        {
            ["inventory"] = Inventory,
            ["area"] = (int)Address.Area,
            ["index"] = Address.Index
        };
    }

    // =========================================================
    // Accept compatible slots belonging to this same player's inventory.
    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        Godot.Collections.Dictionary payload = data.AsGodotDictionary();

        if (!payload.ContainsKey("inventory") ||
            !payload.ContainsKey("area") || !payload.ContainsKey("index"))
            return false;
        if (payload["inventory"].AsGodotObject() != Inventory) return false;

        InventoryAddress from = new(
            (InventoryArea)payload["area"].AsInt32(),
            payload["index"].AsInt32());

        if (!Inventory.HasAddress(from) ||
            (from.Area == Address.Area && from.Index == Address.Index))
            return false;

        InventoryStack source = Inventory.GetStack(from);
        InventoryStack target = Inventory.GetStack(Address);
        return !source.IsEmpty && Inventory.CanPlace(Address, source) &&
            Inventory.CanPlace(from, target);
    }

    // =========================================================
    // Request an atomic transfer through the inventory coordinator.
    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!_CanDropData(atPosition, data)) return;
        Godot.Collections.Dictionary payload = data.AsGodotDictionary();

        Inventory.TryMove(new InventoryAddress(
            (InventoryArea)payload["area"].AsInt32(),
            payload["index"].AsInt32()), Address);
    }
    #endregion
}