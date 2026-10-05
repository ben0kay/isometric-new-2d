// Stores quick-access references to inventory slots without owning extra items.
// Transfers remap shortcuts; removing a stack clears its bindings.
using Godot;
using System;

public partial class PlayerHotbar : Node
{
    #region Configuration
    [Export(PropertyHint.Range, "1,10,1")]
    public int SlotCount { get; set; } = 10;
    #endregion

    #region State
    public event Action Changed;
    public int SelectedSlot { get; private set; }
    public ItemDefinition CurrentItem => GetStack(SelectedSlot).Item;

    private PlayerInventory _inventory;
    private PlayerEquipment _equipment;
    private InventoryAddress?[] _bindings;
    private ItemDefinition[] _expected;
    #endregion

    #region Lifecycle
    // =========================================================
    // Move starting tools into the backpack and assign initial shortcuts.
    public override void _Ready()
    {
        SlotCount = Math.Clamp(SlotCount, 1, 10);
        _bindings = new InventoryAddress?[SlotCount];
        _expected = new ItemDefinition[SlotCount];
        _inventory = GetNode<PlayerInventory>("../Inventory");
        _equipment = GetNode<PlayerEquipment>("../Equipment");

        _inventory.Moved += OnMoved;
        _inventory.Removed += OnRemoved;
        _inventory.Changed += ValidateBindings;

        for (int tool = 0; tool < _equipment.ToolSlotCount; tool++)
        {
            InventoryAddress source = new(InventoryArea.Tools, tool);
            if (_inventory.GetStack(source).IsEmpty) continue;

            for (int bag = 0; bag < _inventory.BagSlotCount; bag++)
            {
                InventoryAddress target = new(InventoryArea.Bag, bag);
                if (!_inventory.GetStack(target).IsEmpty) continue;
                if (!_inventory.TryMove(source, target)) break;
                if (tool < SlotCount) Bind(tool, target);
                break;
            }
        }

        Publish();
    }

    // =========================================================
    // Disconnect from the inventory when this player leaves the scene.
    public override void _ExitTree()
    {
        if (!GodotObject.IsInstanceValid(_inventory)) return;
        _inventory.Moved -= OnMoved;
        _inventory.Removed -= OnRemoved;
        _inventory.Changed -= ValidateBindings;
    }
    #endregion

    #region Queries
    // =========================================================
    // Resolve a live shortcut only while its expected item remains there.
    public InventoryStack GetStack(int index)
    {
        if (_bindings == null || index < 0 || index >= _bindings.Length ||
            !_bindings[index].HasValue) return default;

        InventoryStack stack = _inventory.GetStack(_bindings[index].Value);
        return InventoryStorage.SameItem(stack.Item, _expected[index])
            ? stack : default;
    }

    // =========================================================
    // Return the inventory address referenced by a hotbar slot.
    public InventoryAddress? GetAddress(int index)
    {
        return !GetStack(index).IsEmpty ? _bindings[index] : null;
    }

    // =========================================================
    // Find the shortcut badge belonging to one inventory slot.
    public int GetBinding(InventoryAddress address)
    {
        if (_bindings == null) return -1;
        for (int i = 0; i < _bindings.Length; i++)
            if (_bindings[i].HasValue && Same(_bindings[i].Value, address))
                return i;
        return -1;
    }

    // =========================================================
    // Compare physical inventory addresses.
    private static bool Same(InventoryAddress a, InventoryAddress b)
    {
        return a.Area == b.Area && a.Index == b.Index;
    }
    #endregion

    #region Selection
    // =========================================================
    // Select any hotbar slot; an empty shortcut means empty hands.
    public void Select(int index)
    {
        if (index < 0 || index >= SlotCount) return;
        SelectedSlot = index;
        Publish();
    }

    // =========================================================
    // Include empty slots when scrolling through quick access.
    public void Cycle(int direction)
    {
        if (direction == 0) return;
        Select((SelectedSlot + Math.Sign(direction) + SlotCount) % SlotCount);
    }

    // =========================================================
    // Assign a shortcut without transferring or duplicating the item.
    public void Bind(int index, InventoryAddress address)
    {
        if (_bindings == null || index < 0 || index >= SlotCount) return;
        InventoryStack stack = _inventory.GetStack(address);
        if (stack.IsEmpty || address.Area == InventoryArea.Backpack) return;

        for (int i = 0; i < SlotCount; i++)
            if (_bindings[i].HasValue && Same(_bindings[i].Value, address))
            {
                _bindings[i] = null;
                _expected[i] = null;
            }

        _bindings[index] = address;
        _expected[index] = stack.Item;
        Publish();
    }

    // =========================================================
    // Remove a shortcut while leaving its item in the inventory.
    public void Clear(int index)
    {
        if (_bindings == null || index < 0 || index >= SlotCount) return;
        _bindings[index] = null;
        _expected[index] = null;
        Publish();
    }

    // =========================================================
    // Update the weapon and notify the HUD after shortcut changes.
    private void Publish()
    {
        _equipment?.RefreshActiveAttack();
        Changed?.Invoke();
    }
    #endregion

    #region Inventory Synchronization
    // =========================================================
    // Follow swaps and fully transferred or merged stacks.
    private void OnMoved(InventoryAddress from, InventoryAddress to, bool swap)
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!_bindings[i].HasValue) continue;
            InventoryAddress address = _bindings[i].Value;

            if (Same(address, from))
            {
                InventoryStack remaining = _inventory.GetStack(from);
                if (swap || !InventoryStorage.SameItem(
                    remaining.Item, _expected[i]))
                    _bindings[i] = to;
            }
            else if (swap && Same(address, to))
                _bindings[i] = from;
        }
    }

    // =========================================================
    // Clear bindings when their physical stack leaves the player.
    private void OnRemoved(InventoryAddress address)
    {
        for (int i = 0; i < SlotCount; i++)
            if (_bindings[i].HasValue && Same(_bindings[i].Value, address))
            {
                _bindings[i] = null;
                _expected[i] = null;
            }
    }

    // =========================================================
    // Reject stale bindings and refresh counts after inventory transactions.
    private void ValidateBindings()
    {
        for (int i = 0; i < SlotCount; i++)
        {
            if (!_bindings[i].HasValue) continue;
            if (!GetStack(i).IsEmpty) continue;
            _bindings[i] = null;
            _expected[i] = null;
        }
        Publish();
    }
    #endregion
}