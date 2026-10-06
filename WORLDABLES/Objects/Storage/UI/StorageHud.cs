// Displays nearby storage using the same inventory cells as the backpack.
// Whole-stack transfers use existing storage APIs and refresh through events.
using Godot;
using System.Collections.Generic;

public partial class StorageHud : CanvasLayer
{
	#region State
	private Player _player;
	private PlayerInventory _inventory;
	private PlayerHotbar _hotbar;
	private InventoryHud _backpack;
	private WorldStorage _nearby, _opened;
	private Control _root;
	private PanelContainer _window;
	private Label _prompt, _title, _totals, _notice;
	private GridContainer _bagGrid, _containerGrid;
	private readonly List<InventorySlot> _bagSlots = new();
	private readonly List<InventorySlot> _containerSlots = new();
	private double _scanTimer;
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve player services and build the shared-slot transfer window.
	public override void _Ready()
	{
		Layer = 21;
		_player = GetParent<Player>();
		_inventory = _player.GetNode<PlayerInventory>("Systems/Inventory");
		_backpack = _player.GetNode<InventoryHud>("InventoryHud");

		foreach (Node child in _player.GetNode("Systems").GetChildren())
			if (child is PlayerHotbar hotbar) _hotbar = hotbar;

		BuildUi();
		_inventory.Changed += Refresh;
		if (_hotbar != null) _hotbar.Changed += Refresh;
		GetViewport().SizeChanged += FitWindow;
		FitWindow();
	}

	// =========================================================
	// Search at ten hertz and close storage when interaction becomes invalid.
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
	// Disconnect events and release the external-window input block.
	public override void _ExitTree()
	{
		Close();
		if (GodotObject.IsInstanceValid(_inventory))
			_inventory.Changed -= Refresh;
		if (GodotObject.IsInstanceValid(_hotbar))
			_hotbar.Changed -= Refresh;
		GetViewport().SizeChanged -= FitWindow;
	}
	#endregion

	#region Layout
	// =========================================================
	// Build a translucent transfer window with two shared inventory grids.
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
		_window.OffsetLeft = -430;
		_window.OffsetRight = 430;
		_window.OffsetTop = -260;
		_window.OffsetBottom = 260;
		_window.PivotOffset = new Vector2(430, 260);
		_window.AddThemeStyleboxOverride("panel", UIInventoryMaster.Style(
			new Color("#12202be8"), new Color("#577981")));

		MarginContainer margin = new();
		foreach (string side in new[] { "left", "right", "top", "bottom" })
			margin.AddThemeConstantOverride("margin_" + side, 14);
		_window.AddChild(margin);

		VBoxContainer contents = new();
		contents.AddThemeConstantOverride("separation", 8);
		margin.AddChild(contents);

		HBoxContainer heading = new();
		contents.AddChild(heading);
		_title = new Label
		{
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
		};
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

		_bagGrid = AddColumn(columns, "BACKPACK → DEPOSIT");
		_containerGrid = AddColumn(columns, "CONTAINER → TAKE");

		_notice = new Label
		{
			Text = "Click an occupied slot to transfer its complete stack.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};
		contents.AddChild(_notice);
	}

	// =========================================================
	// Create a scrolling grid so larger definitions add rows automatically.
	private static GridContainer AddColumn(Node parent, string title)
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
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		column.AddChild(scroll);

		GridContainer grid = new() { Columns = 5 };
		grid.AddThemeConstantOverride("h_separation", 5);
		grid.AddThemeConstantOverride("v_separation", 5);
		scroll.AddChild(grid);
		return grid;
	}

	// =========================================================
	// Fit the window when viewport dimensions change.
	private void FitWindow()
	{
		Vector2 size = GetViewport().GetVisibleRect().Size;
		float scale = Mathf.Clamp(Mathf.Min(
			(size.X - 24f) / 860f, (size.Y - 40f) / 520f), 0.2f, 1f);
		_window.Scale = Vector2.One * scale;
	}

	// =========================================================
	// Rebuild only when the definition's required slot count changes.
	private void EnsureSlots(
		List<InventorySlot> slots, GridContainer grid,
		int count, bool deposit)
	{
		if (slots.Count == count) return;

		foreach (InventorySlot slot in slots)
		{
			grid.RemoveChild(slot);
			slot.QueueFree();
		}
		slots.Clear();

		for (int i = 0; i < count; i++)
		{
			int index = i;
			InventorySlot slot = new()
			{
				SlotSize = 76,
				AllowDragging = false
			};

			if (deposit)
			{
				slot.Inventory = _inventory;
				slot.Hotbar = _hotbar;
				slot.Address = new InventoryAddress(InventoryArea.Bag, index);
			}
			else
			{
				slot.StackReader = () =>
					GodotObject.IsInstanceValid(_opened)
						? _opened.GetStack(index) : default;
			}

			slot.Pressed += () => Transfer(index, deposit);
			grid.AddChild(slot);
			slots.Add(slot);
		}
	}
	#endregion

	#region Interaction
	// =========================================================
	// Consume closing keys before other gameplay handlers receive them.
	public override void _Input(InputEvent input)
	{
		if (_opened == null || input is not InputEventKey key ||
			!key.Pressed || key.Echo) return;
		if (key.PhysicalKeycode != Key.E &&
			key.PhysicalKeycode != Key.Escape &&
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
	// Disconnect the container and release inventory input protection.
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
	// Recheck proximity before requesting an existing whole-stack transaction.
	private void Transfer(int index, bool deposit)
	{
		if (!GodotObject.IsInstanceValid(_opened) ||
			!_opened.CanInteract(_player))
		{
			Close();
			return;
		}

		InventoryStack stack = deposit
			? _inventory.GetStack(new InventoryAddress(InventoryArea.Bag, index))
			: _opened.GetStack(index);
		if (stack.IsEmpty) return;

		string reason;
		bool success = deposit
			? _opened.TryDeposit(_inventory, index, out reason)
			: _opened.TryWithdraw(_inventory, index, out reason);

		_notice.Text = success ? "Transferred." : reason;
	}
	#endregion

	#region Display
	// =========================================================
	// Refresh shared cells and cached physical totals after transactions.
	private void Refresh()
	{
		if (!GodotObject.IsInstanceValid(_opened)) return;

		EnsureSlots(_bagSlots, _bagGrid, _inventory.BagSlotCount, true);
		EnsureSlots(_containerSlots, _containerGrid, _opened.SlotCount, false);

		StorageDefinition definition = _opened.Definition;
		_title.Text = definition.DisplayName;
		_totals.Text =
			$"Container: {_opened.WeightKg:0.0}/{definition.MaximumWeightKg:0.#} kg" +
			$"   {_opened.VolumeLitres:0.0}/{definition.CapacityLitres:0.#} L\n" +
			$"Player: {_inventory.TotalWeightKg:0.0}/" +
			$"{_inventory.Rules.MaximumWeightKg:0.#} kg" +
			$"   Backpack: {_inventory.BagSlotCount} slots";

		foreach (InventorySlot slot in _bagSlots) slot.Refresh(false);
		foreach (InventorySlot slot in _containerSlots) slot.Refresh(false);
	}
	#endregion
}
