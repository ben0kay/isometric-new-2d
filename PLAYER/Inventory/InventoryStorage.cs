// Stores stacks and handles slot capacity independently from gameplay rules.
// Shared value types identify backpack, tool, and equipped-backpack slots.
using System;

public enum InventoryArea { Bag, Tools, Backpack }

public readonly struct InventoryAddress
{
    public InventoryArea Area { get; }
    public int Index { get; }

    // =========================================================
    // Identify one slot without exposing its storage implementation.
    public InventoryAddress(InventoryArea area, int index = 0)
    {
        Area = area;
        Index = index;
    }
}

public readonly struct InventoryStack
{
    public ItemDefinition Item { get; }
    public int Count { get; }
    public bool IsEmpty => Item == null || Count <= 0;

    // =========================================================
    // Keep empty stacks consistent and quantities separate from item resources.
    public InventoryStack(ItemDefinition item, int count)
    {
        Item = item != null && count > 0 ? item : null;
        Count = Item != null ? count : 0;
    }
}

public sealed class InventoryStorage
{
    #region State
    public int SlotCount => _slots.Length;
    private InventoryStack[] _slots;
    #endregion

    #region Creation
    // =========================================================
    // Allocate a fixed set of empty storage slots.
    public InventoryStorage(int slotCount)
    {
        _slots = new InventoryStack[Math.Max(0, slotCount)];
    }

    // =========================================================
    // Clone runtime stacks for validating a transaction before committing it.
    public InventoryStorage Clone()
    {
        InventoryStorage copy = new(SlotCount);
        Array.Copy(_slots, copy._slots, SlotCount);
        return copy;
    }
    #endregion

    #region Slots
    // =========================================================
    // Check whether an index belongs to this storage.
    public bool HasSlot(int index)
    {
        return index >= 0 && index < SlotCount;
    }

    // =========================================================
    // Read a slot without exposing the underlying array.
    public InventoryStack Get(int index)
    {
        return HasSlot(index) ? _slots[index] : default;
    }

    // =========================================================
    // Replace one valid slot.
    public void Set(int index, InventoryStack stack)
    {
        if (!HasSlot(index)) throw new ArgumentOutOfRangeException(nameof(index));
        _slots[index] = stack;
    }

    // =========================================================
    // Resize without deleting items from occupied trailing slots.
    public bool TryResize(int count)
    {
        count = Math.Max(0, count);

        for (int i = count; i < SlotCount; i++)
            if (!_slots[i].IsEmpty) return false;

        Array.Resize(ref _slots, count);
        return true;
    }

    // =========================================================
    // Compare item resources using their stable IDs.
    public static bool SameItem(ItemDefinition first, ItemDefinition second)
    {
        return first != null && second != null &&
            (first == second || (!string.IsNullOrEmpty(first.Id) &&
            first.Id == second.Id));
    }
    #endregion

    #region Collection
    // =========================================================
    // Add an entire quantity or leave every slot unchanged.
    public bool TryAdd(ItemDefinition item, int count)
    {
        if (item == null || count <= 0) return false;

        int limit = Math.Max(1, item.MaxStack);
        long capacity = 0;

        foreach (InventoryStack stack in _slots)
        {
            if (stack.IsEmpty) capacity += limit;
            else if (SameItem(stack.Item, item))
                capacity += Math.Max(0, limit - stack.Count);
        }

        if (capacity < count) return false;
        int remaining = count;

        for (int i = 0; i < SlotCount && remaining > 0; i++)
        {
            InventoryStack stack = _slots[i];
            if (!SameItem(stack.Item, item)) continue;

            int added = Math.Min(
                remaining, Math.Max(0, limit - stack.Count));
            _slots[i] = new InventoryStack(stack.Item, stack.Count + added);
            remaining -= added;
        }

        for (int i = 0; i < SlotCount && remaining > 0; i++)
        {
            if (!_slots[i].IsEmpty) continue;
            int added = Math.Min(remaining, limit);
            _slots[i] = new InventoryStack(item, added);
            remaining -= added;
        }

        return true;
    }

    // =========================================================
    // Calculate physical contents only when inventory data changes.
    public void GetTotals(out float weight, out float volume)
    {
        weight = 0f;
        volume = 0f;

        foreach (InventoryStack stack in _slots)
        {
            if (stack.IsEmpty) continue;
            weight += Math.Max(0f, stack.Item.WeightKg) * stack.Count;
            volume += Math.Max(0f, stack.Item.VolumeLitres) * stack.Count;
        }
    }
    #endregion
}