// Displays shared inventory cells for backpacks, equipment and world containers.
// Player slots support dragging; container windows can supply their own stack reader.
using Godot;
using System;

public partial class InventorySlot : Button
{
	#region Configuration
	public PlayerInventory Inventory { get; set; }
	public PlayerHotbar Hotbar { get; set; }
	public InventoryAddress Address { get; set; }
	public int SlotSize { get; set; } = 76;

	public Func<InventoryStack> StackReader { get; set; }
	public bool AllowDragging { get; set; } = true;
	#endregion

	#region Lifecycle
	// =========================================================
	// Configure the same cell appearance for every inventory window.
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
	// Read live contents without giving the UI ownership of item storage.
	private InventoryStack ReadStack()
	{
		if (StackReader != null) return StackReader();
		return GodotObject.IsInstanceValid(Inventory)
			? Inventory.GetStack(Address) : default;
	}

	// =========================================================
	// Display quantity, icon and an optional player hotbar badge.
	public void Refresh(bool selected)
	{
		InventoryStack stack = ReadStack();
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
	// Preview carried contents without removing the physical stack.
	public override Variant _GetDragData(Vector2 atPosition)
	{
		if (!AllowDragging || StackReader != null ||
			!GodotObject.IsInstanceValid(Inventory)) return default;

		InventoryStack stack = ReadStack();
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
	// Decode a carried-item drag belonging to this player.
	public static bool ReadPayload(
		Variant data, PlayerInventory inventory,
		out InventoryAddress address)
	{
		address = default;
		if (!GodotObject.IsInstanceValid(inventory) ||
			data.VariantType != Variant.Type.Dictionary) return false;

		var payload = data.AsGodotDictionary();
		if (!payload.ContainsKey("inventory") ||
			!payload.ContainsKey("area") ||
			!payload.ContainsKey("index") ||
			payload["inventory"].AsGodotObject() != inventory) return false;

		address = new InventoryAddress(
			(InventoryArea)payload["area"].AsInt32(),
			payload["index"].AsInt32());
		return inventory.HasAddress(address);
	}

	// =========================================================
	// Accept compatible player moves without modifying contents during hover.
	public override bool _CanDropData(Vector2 atPosition, Variant data)
	{
		if (!AllowDragging || StackReader != null ||
			!ReadPayload(data, Inventory, out InventoryAddress from) ||
			(from.Area == Address.Area && from.Index == Address.Index))
			return false;

		InventoryStack source = Inventory.GetStack(from);
		return !source.IsEmpty && Inventory.CanPlace(Address, source) &&
			Inventory.CanPlace(from, Inventory.GetStack(Address));
	}

	// =========================================================
	// Commit a player move, stack merge or equipment swap.
	public override void _DropData(Vector2 atPosition, Variant data)
	{
		if (!_CanDropData(atPosition, data)) return;
		ReadPayload(data, Inventory, out InventoryAddress from);
		Inventory.TryMove(from, Address);
	}
	#endregion
}
