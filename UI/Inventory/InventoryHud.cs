// Builds the backpack interface and displays cached carrying state.
// UI sends inventory requests and observes events without owning gameplay data.
using Godot;
using System.Collections.Generic;

public partial class InventoryHud : CanvasLayer
{
	#region State
	public bool IsOpen => _window != null && _window.Visible;

	private PlayerInventory _inventory;
	private PlayerEquipment _equipment;
	private MiningEmitter _mining;
	private Viewport _viewport;
	private Control _root;
	private PanelContainer _window;
	private GridContainer _grid;
	private Label _packName;
	private Label _packDetails;
	private Label _load;
	private Label _notice;
	private Label _status;
	private bool _blockUntilRelease;

	private readonly List<InventorySlot> _slots = new();
	private readonly List<InventorySlot> _bagSlots = new();
		public bool ExternalWindowOpen { get; private set; }
	public bool BlocksWorldMovement => IsOpen || ExternalWindowOpen;


	
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve player components and construct the interface once.
	public override void _Ready()
	{
		Layer = 20;
		Player player = GetParent<Player>();
		_inventory = player.GetNode<PlayerInventory>("Systems/Inventory");
		_equipment = player.GetNode<PlayerEquipment>("Systems/Equipment");
		_mining = player.GetNode<MiningEmitter>("Systems/MiningEmitter");
		_viewport = GetViewport();

		BuildUi();
		_inventory.Changed += Refresh;
		_inventory.Notice += ShowNotice;
		_equipment.SelectionChanged += Refresh;
		_viewport.SizeChanged += FitWindow;

		Refresh();
		FitWindow();
	}

	// =========================================================
	// Remove event subscriptions when this player's HUD leaves the scene.
	public override void _ExitTree()
	{
		if (_inventory == null) return;
		_inventory.Changed -= Refresh;
		_inventory.Notice -= ShowNotice;
		_equipment.SelectionChanged -= Refresh;
		_viewport.SizeChanged -= FitWindow;
	}
	#endregion

	#region Layout
	// =========================================================
	// Build the equipment column, backpack body, and equipped-pack column.
	private void BuildUi()
	{
		_root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
		AddChild(_root);
		_root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);

		_status = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore
		};
		_root.AddChild(_status);
		_status.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_status.OffsetLeft = 16;
		_status.OffsetRight = -16;
		_status.OffsetTop = -38;
		_status.OffsetBottom = -8;

		_window = new PanelContainer { Visible = false };
		_root.AddChild(_window);
		_window.SetAnchorsPreset(Control.LayoutPreset.Center);
		_window.OffsetLeft = -450;
		_window.OffsetRight = 450;
		_window.OffsetTop = -270;
		_window.OffsetBottom = 270;
		_window.PivotOffset = new Vector2(450, 270);
		_window.AddThemeStyleboxOverride(
			"panel", PanelStyle(new Color("#101a23"), 12));

		VBoxContainer contents = Contents(_window, 14);
		HBoxContainer heading = new();
		contents.AddChild(heading);
		heading.AddChild(new Label
		{
			Text = "FIELD EQUIPMENT",
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		});

		Button close = new()
		{
			Text = "Close [Delete]",
			FocusMode = Control.FocusModeEnum.None
		};
		heading.AddChild(close);
		close.Pressed += Toggle;

		HBoxContainer columns = new()
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill
		};
		columns.AddThemeConstantOverride("separation", 12);
		contents.AddChild(columns);

		VBoxContainer tools = new()
		{
			CustomMinimumSize = new Vector2(120, 0)
		};
		columns.AddChild(tools);
		tools.AddChild(new Label { Text = "TOOLS" });

		for (int i = 0; i < _equipment.ToolSlotCount; i++)
		{
			int index = i;
			InventorySlot slot = AddSlot(
				tools, new InventoryAddress(InventoryArea.Tools, index));
			slot.Pressed += () => _equipment.Select(index);
		}

		PanelContainer bagBody = new()
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		bagBody.AddThemeStyleboxOverride(
			"panel", PanelStyle(new Color("#29383b"), 20));
		columns.AddChild(bagBody);

		VBoxContainer bagContents = Contents(bagBody, 12);
		_packName = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center
		};
		bagContents.AddChild(_packName);

		ScrollContainer scroll = new()
		{
			SizeFlagsVertical = Control.SizeFlags.ExpandFill,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
		bagContents.AddChild(scroll);

		_grid = new GridContainer { Columns = 6 };
		_grid.AddThemeConstantOverride("h_separation", 6);
		_grid.AddThemeConstantOverride("v_separation", 6);
		scroll.AddChild(_grid);

		VBoxContainer packColumn = new()
		{
			CustomMinimumSize = new Vector2(160, 0)
		};
		columns.AddChild(packColumn);
		packColumn.AddChild(new Label { Text = "BACKPACK" });
		AddSlot(packColumn, new InventoryAddress(InventoryArea.Backpack));

		_packDetails = new Label();
		packColumn.AddChild(_packDetails);

		_load = new Label();
		_notice = new Label
		{
			Text = "Drag items to move, merge, swap, or equip.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		contents.AddChild(_load);
		contents.AddChild(_notice);
	}

	// =========================================================
	// Create a rounded panel style used by the window and backpack body.
	private static StyleBoxFlat PanelStyle(Color color, int radius)
	{
		return new StyleBoxFlat
		{
			BgColor = color,
			BorderColor = new Color("#526269"),
			BorderWidthLeft = 2,
			BorderWidthRight = 2,
			BorderWidthTop = 2,
			BorderWidthBottom = 2,
			CornerRadiusTopLeft = radius,
			CornerRadiusTopRight = radius,
			CornerRadiusBottomLeft = radius,
			CornerRadiusBottomRight = radius
		};
	}

	// =========================================================
	// Add consistent padding around a panel's vertical content.
	private static VBoxContainer Contents(PanelContainer panel, int padding)
	{
		MarginContainer margin = new();
		foreach (string side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride("margin_" + side, padding);

		panel.AddChild(margin);
		VBoxContainer contents = new();
		contents.AddThemeConstantOverride("separation", 8);
		margin.AddChild(contents);
		return contents;
	}

	// =========================================================
	// Create a slot bound to a public inventory address.
	private InventorySlot AddSlot(Node parent, InventoryAddress address)
	{
		InventorySlot slot = new()
		{
			Inventory = _inventory,
			Address = address
		};
		parent.AddChild(slot);
		_slots.Add(slot);
		return slot;
	}

	// =========================================================
	// Rebuild backpack controls only when the backpack's slot count changes.
	private void RefreshBagLayout()
	{
		if (_bagSlots.Count == _inventory.BagSlotCount) return;

		foreach (InventorySlot slot in _bagSlots)
		{
			_slots.Remove(slot);
			_grid.RemoveChild(slot);
			slot.QueueFree();
		}
		_bagSlots.Clear();

		for (int i = 0; i < _inventory.BagSlotCount; i++)
			_bagSlots.Add(AddSlot(
				_grid, new InventoryAddress(InventoryArea.Bag, i)));
	}

	// =========================================================
	// Scale the interface to fit smaller windows without a per-frame layout loop.
	private void FitWindow()
	{
		Vector2 size = _viewport.GetVisibleRect().Size;
		float scale = Mathf.Clamp(Mathf.Min(
			(size.X - 24f) / 900f, (size.Y - 70f) / 540f), 0.2f, 1f);
		_window.Scale = Vector2.One * scale;
	}
	#endregion

	#region Input
	// =========================================================
	// Handle equipment input only when no external inventory window is active.
	public override void _Input(InputEvent input)
	{
		if (ExternalWindowOpen) return;

		if (input is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.PhysicalKeycode == Key.Delete)
			{
				if (!_viewport.GuiIsDragging()) Toggle();
				_viewport.SetInputAsHandled();
				return;
			}

			int index = (int)key.PhysicalKeycode - (int)Key.Key1;
			if (!IsOpen && index >= 0 && index < _equipment.ToolSlotCount)
			{
				_equipment.Select(index);
				_viewport.SetInputAsHandled();
				return;
			}
		}

		if (IsOpen ||
			input is not InputEventMouseButton mouse || !mouse.Pressed)
			return;

		int direction = mouse.ButtonIndex == MouseButton.WheelDown ? 1 :
			mouse.ButtonIndex == MouseButton.WheelUp ? -1 : 0;
		if (direction == 0) return;

		_equipment.Cycle(direction);
		_viewport.SetInputAsHandled();
	}

	// =========================================================
	// Toggle the window and prevent a closing click from firing into the world.
	private void Toggle()
	{
		_window.Visible = !_window.Visible;
		_blockUntilRelease = true;
		_mining.Stop();
	}

	// =========================================================
	// Block tools during inventory use and until a closing click is released.
	public bool BlocksWorldAttack()
	{
		if (_blockUntilRelease)
		{
			if (!Input.IsMouseButtonPressed(MouseButton.Left))
				_blockUntilRelease = false;
			return true;
		}
		return IsOpen || ExternalWindowOpen || _viewport.GuiIsDragging();
	}

	// =========================================================
	// Protect world input while another inventory interface is open.
	public void SetExternalWindowOpen(bool open)
	{
		if (ExternalWindowOpen == open) return;
		ExternalWindowOpen = open;
		_blockUntilRelease = true;
		_mining.Stop();
	}
	#endregion

	#region Display
	// =========================================================
	// Refresh cached totals and slots when inventory or selection changes.
	private void Refresh()
	{
		RefreshBagLayout();
		BackpackDefinition pack = _equipment.Backpack;
		string tool = _equipment.CurrentTool?.DisplayName ?? "Empty hands";

		_packName.Text = pack?.DisplayName ?? "NO BACKPACK";
		_packDetails.Text = pack == null ? "None equipped" :
			$"{pack.SlotCount} slots\n" +
			$"{pack.CapacityLitres:0.#} L capacity\n\n" +
			$"Comfort: {pack.ComfortableWeightKg:0.#} kg\n" +
			$"Maximum: {pack.MaximumWeightKg:0.#} kg\n\n" +
			$"Pack: {pack.WeightKg:0.#} kg";

		float maxWeight = pack?.MaximumWeightKg
			?? _inventory.Rules.MaximumWeightKg;
		float maxVolume = pack?.CapacityLitres ?? 0f;

		string load =
			$"Weight {_inventory.TotalWeightKg:0.0}/{maxWeight:0.#} kg" +
			$"    Volume {_inventory.UsedVolumeLitres:0.0}/{maxVolume:0.#} L" +
			$"    Movement {_inventory.MovementFactor * 100f:0}%";

		_load.Text = load;
		_status.Text = $"{tool}    |    {load}    |    Delete: backpack";

		foreach (InventorySlot slot in _slots)
			slot.Refresh(slot.Address.Area == InventoryArea.Tools &&
				slot.Address.Index == _equipment.SelectedSlot);
	}

	// =========================================================
	// Display collection or capacity feedback.
	private void ShowNotice(string text)
	{
		_notice.Text = text;
	}
	#endregion
}
