// Coordinates storage, equipment, and carrying rules through one public interface.
// Mining and UI request transactions without editing storage or equipment directly.
using Godot;
using System;

public partial class PlayerInventory : Node
{
	#region Configuration
	[ExportGroup("Carrying")]
	[Export] public CarryingRules Rules { get; set; }
	#endregion

	#region State
	public event Action Changed;
	public event Action<string> Notice;
	public event Action<InventoryAddress, InventoryAddress, bool> Moved;
public event Action<InventoryAddress> Removed;

	public float TotalWeightKg { get; private set; }
	public float UsedVolumeLitres { get; private set; }
	public float MovementFactor { get; private set; } = 1f;
	public int BagSlotCount => _storage.SlotCount;
	public PlayerEquipment Equipment { get; private set; }

	private InventoryStorage _storage = new(0);
	private string _lastNotice = "";
	#endregion

	#region Lifecycle
	// =========================================================
	// Allocate the starting backpack and calculate the initial carried load.
	public override void _Ready()
	{
		Rules ??= new CarryingRules();
		Equipment = GetNode<PlayerEquipment>("../Equipment");
		_storage = new InventoryStorage(PackSlots(Equipment.Backpack));
		Recalculate();
	}
	#endregion

	#region Queries
	// =========================================================
	// Return the configured slot count for an equipped backpack.
	private static int PackSlots(BackpackDefinition pack)
	{
		return pack == null ? 0 : Math.Clamp(pack.SlotCount, 1, 96);
	}

	// =========================================================
	// Check whether an address belongs to this player's current inventory.
	public bool HasAddress(InventoryAddress address)
	{
		return address.Area switch
		{
			InventoryArea.Bag => _storage.HasSlot(address.Index),
			InventoryArea.Tools => address.Index >= 0 &&
				address.Index < Equipment.ToolSlotCount,
			InventoryArea.Backpack => address.Index == 0,
			_ => false
		};
	}

// =========================================================
// Read one slot directly without allocating a temporary equipment array.
public InventoryStack GetStack(InventoryAddress address)
{
	if (!HasAddress(address)) return default;

	return address.Area switch
	{
		InventoryArea.Bag => _storage.Get(address.Index),
		InventoryArea.Tools => new InventoryStack(
			Equipment.GetTool(address.Index), 1),
		InventoryArea.Backpack => new InventoryStack(Equipment.Backpack, 1),
		_ => default
	};
}

	// =========================================================
	// Check basic slot compatibility without cloning or calculating capacity.
	public bool CanPlace(InventoryAddress address, InventoryStack stack)
	{
		if (!HasAddress(address)) return false;
		if (stack.IsEmpty) return true;

		return address.Area switch
		{
			InventoryArea.Bag => true,
			InventoryArea.Tools => stack.Count == 1 && stack.Item.Attack != null,
			InventoryArea.Backpack => stack.Count == 1 &&
				stack.Item is BackpackDefinition,
			_ => false
		};
	}
	#endregion

	#region Collection
// =========================================================
// Check player carrying strength and backpack storage before collecting.
public bool TryCollect(ItemDefinition item, int count)
{
	if (item == null || count <= 0) return false;
	BackpackDefinition pack = Equipment.Backpack;

	if (pack == null)
		return Reject("Equip a backpack before collecting items.");

	float weight = TotalWeightKg + Mathf.Max(0f, item.WeightKg) * count;
	float volume = UsedVolumeLitres +
		Mathf.Max(0f, item.VolumeLitres) * count;

	if (!Rules.Allows(
		weight, volume, Rules.MaximumWeightKg, pack.CapacityLitres,
		out string reason))
		return Reject(reason);

	if (!_storage.TryAdd(item, count))
		return Reject("No free backpack slots or stack space.");

	Recalculate();
	Report($"+{count} {item.DisplayName}");
	return true;
}
	#endregion

	#region Transfers
// =========================================================
// Stage a move using backpack slots and volume, but player carrying strength.
public bool TryMove(InventoryAddress from, InventoryAddress to)
{
	if (!HasAddress(from) || !HasAddress(to) ||
		(from.Area == to.Area && from.Index == to.Index)) return false;

	InventoryStorage bag = _storage.Clone();
	ItemDefinition[] tools = Equipment.CopyTools();
	BackpackDefinition pack = Equipment.Backpack;
	InventoryStack source = Read(from, bag, tools, pack);
	InventoryStack target = Read(to, bag, tools, pack);
	if (source.IsEmpty) return false;

	bool merge = InventoryStorage.SameItem(source.Item, target.Item);
	InventoryStack newSource, newTarget;

	if (merge)
	{
		int limit = to.Area == InventoryArea.Bag
			? Math.Max(1, target.Item.MaxStack) : 1;
		int moved = Math.Min(source.Count, Math.Max(0, limit - target.Count));
		if (moved == 0) return false;

		newSource = new InventoryStack(source.Item, source.Count - moved);
		newTarget = new InventoryStack(target.Item, target.Count + moved);
	}
	else
	{
		newSource = target;
		newTarget = source;
	}

	if (!CanPlace(from, newSource) || !CanPlace(to, newTarget))
		return Reject("That item does not fit this equipment slot.");

	Write(from, newSource, bag, tools, ref pack);
	Write(to, newTarget, bag, tools, ref pack);

	if (!bag.TryResize(PackSlots(pack)))
		return Reject(pack == null
			? "Empty the backpack before removing it."
			: "Clear the end slots before using a smaller backpack.");

	GetTotals(bag, tools, pack, out float weight, out float volume);
	float capacity = pack?.CapacityLitres ?? 0f;

	if (!Rules.Allows(
		weight, volume, Rules.MaximumWeightKg, capacity, out string reason))
		return Reject(reason);

	_storage = bag;
	Equipment.ApplyContents(tools, pack);
	Moved?.Invoke(from, to, !merge);
	Recalculate();
	return true;
}

	// =========================================================
	// Read a slot from staged storage and equipment.
	private static InventoryStack Read(
		InventoryAddress address, InventoryStorage bag,
		ItemDefinition[] tools, BackpackDefinition pack)
	{
		return address.Area switch
		{
			InventoryArea.Bag => bag.Get(address.Index),
			InventoryArea.Tools => new InventoryStack(tools[address.Index], 1),
			InventoryArea.Backpack => new InventoryStack(pack, 1),
			_ => default
		};
	}

	// =========================================================
	// Write a compatible stack into staged storage or equipment.
	private static void Write(
		InventoryAddress address, InventoryStack stack,
		InventoryStorage bag, ItemDefinition[] tools,
		ref BackpackDefinition pack)
	{
		switch (address.Area)
		{
			case InventoryArea.Bag:
				bag.Set(address.Index, stack);
				break;
			case InventoryArea.Tools:
				tools[address.Index] = stack.Item;
				break;
			case InventoryArea.Backpack:
				pack = stack.Item as BackpackDefinition;
				break;
		}
	}

// =========================================================
// Remove a verified stack and clear shortcuts before refreshing the HUD.
public bool TryRemoveBagStack(int index, InventoryStack expected)
{
	if (!_storage.HasSlot(index) || expected.IsEmpty) return false;
	InventoryStack current = _storage.Get(index);

	if (current.Count != expected.Count ||
		!InventoryStorage.SameItem(current.Item, expected.Item)) return false;

	_storage.Set(index, default);
	Removed?.Invoke(new InventoryAddress(InventoryArea.Bag, index));
	Recalculate();
	return true;
}

// =========================================================
// Drop a complete stack only after the world accepts its spawn.
public bool TryDrop(InventoryAddress address)
{
	if (!HasAddress(address)) return false;
	InventoryStack stack = GetStack(address);
	if (stack.IsEmpty) return false;

	InventoryStorage bag = _storage.Clone();
	ItemDefinition[] tools = Equipment.CopyTools();
	BackpackDefinition pack = Equipment.Backpack;
	Write(address, default, bag, tools, ref pack);

	if (!bag.TryResize(PackSlots(pack)))
		return Reject("Empty the backpack before dropping it.");

	Player player = GetParent().GetParent<Player>();
	ResourceWorld world = ResourceWorld.Find(this);
	Vector2 direction = player.GetGlobalMousePosition() - player.GlobalPosition;
	direction = direction.LengthSquared() > 1f
		? direction.Normalized() : Vector2.Down;

	float distance = world == null ? 80f
		: Math.Max(80f, world.PickupRadius + 24f);

	if (world == null || !world.SpawnItem(
		stack.Item, stack.Count, player.GlobalPosition + direction * distance))
		return Reject("Unable to place this item in the world.");

	_storage = bag;
	Equipment.ApplyContents(tools, pack);
	Removed?.Invoke(address);
	Recalculate();
	Report($"Dropped {stack.Item.DisplayName} ×{stack.Count}");
	return true;
}

// =========================================================
// Remove one verified backpack item and preserve shortcuts until its stack empties.
public bool TryTakeOne(InventoryAddress address, ItemDefinition expected)
{
    if (address.Area != InventoryArea.Bag || !HasAddress(address))
        return false;

    InventoryStack stack = _storage.Get(address.Index);
    if (stack.IsEmpty ||
        !InventoryStorage.SameItem(stack.Item, expected))
        return false;

    InventoryStack remaining = new(stack.Item, stack.Count - 1);
    _storage.Set(address.Index, remaining);

    if (remaining.IsEmpty)
        Removed?.Invoke(address);

    Recalculate();
    return true;
}
	#endregion

	#region Carrying State
	// =========================================================
	// Include externally equipped tools and backpack mass, but only bag contents volume.
	private static void GetTotals(
		InventoryStorage bag, ItemDefinition[] tools, BackpackDefinition pack,
		out float weight, out float volume)
	{
		bag.GetTotals(out weight, out volume);
		if (pack != null) weight += Mathf.Max(0f, pack.WeightKg);

		foreach (ItemDefinition tool in tools)
			if (tool != null) weight += Mathf.Max(0f, tool.WeightKg);
	}

// =========================================================
// Calculate movement penalties from the player's strength, regardless of backpack.
private void Recalculate()
{
	BackpackDefinition pack = Equipment.Backpack;
	GetTotals(_storage, Equipment.CopyTools(), pack,
		out float weight, out float volume);

	TotalWeightKg = weight;
	UsedVolumeLitres = volume;
	MovementFactor = Rules.SpeedFactor(
		weight, Rules.ComfortableWeightKg, Rules.MaximumWeightKg);

	Changed?.Invoke();
}
	#endregion

	#region Feedback
	// =========================================================
	// Report a failed transaction without changing inventory contents.
	private bool Reject(string message)
	{
		Report(message);
		return false;
	}

	// =========================================================
	// Avoid repeatedly publishing an identical blocked-mining message.
	private void Report(string message)
	{
		if (_lastNotice == message) return;
		_lastNotice = message;
		Notice?.Invoke(message);
	}
	#endregion
}
