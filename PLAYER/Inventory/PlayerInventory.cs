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
    // Accept a complete mining reward only when physical and slot capacity allow it.
    public bool TryCollect(ItemDefinition item, int count)
    {
        if (item == null || count <= 0) return false;
        BackpackDefinition pack = Equipment.Backpack;

        if (pack == null)
            return Reject("Equip a backpack before collecting items.");

        float weight = TotalWeightKg +
            Mathf.Max(0f, item.WeightKg) * count;
        float volume = UsedVolumeLitres +
            Mathf.Max(0f, item.VolumeLitres) * count;

        if (!Rules.Allows(
            weight, volume, pack.MaximumWeightKg, pack.CapacityLitres,
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
    // Stage a move, merge, or swap and commit only after all checks succeed.
    public bool TryMove(InventoryAddress from, InventoryAddress to)
    {
        if (!HasAddress(from) || !HasAddress(to) ||
            (from.Area == to.Area && from.Index == to.Index))
            return false;

        InventoryStorage bag = _storage.Clone();
        ItemDefinition[] tools = Equipment.CopyTools();
        BackpackDefinition pack = Equipment.Backpack;
        InventoryStack source = Read(from, bag, tools, pack);
        InventoryStack target = Read(to, bag, tools, pack);
        if (source.IsEmpty) return false;

        InventoryStack newSource;
        InventoryStack newTarget;

        if (InventoryStorage.SameItem(source.Item, target.Item))
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
                ? "A backpack cannot be stored inside itself."
                : "Move items out of the end slots before using a smaller backpack.");

        GetTotals(bag, tools, pack, out float weight, out float volume);
        float maximum = pack?.MaximumWeightKg ?? Rules.MaximumWeightKg;
        float capacity = pack?.CapacityLitres ?? 0f;

        if (!Rules.Allows(weight, volume, maximum, capacity, out string reason))
            return Reject(reason);

        _storage = bag;
        Equipment.ApplyContents(tools, pack);
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
    // Remove a complete backpack stack only if it matches the expected contents.
    public bool TryRemoveBagStack(int index, InventoryStack expected)
    {
        if (!_storage.HasSlot(index) || expected.IsEmpty) return false;

        InventoryStack current = _storage.Get(index);
        if (current.Count != expected.Count ||
            !InventoryStorage.SameItem(current.Item, expected.Item))
            return false;

        _storage.Set(index, default);
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
    // Cache totals and movement penalty when inventory contents change.
    private void Recalculate()
    {
        BackpackDefinition pack = Equipment.Backpack;
        GetTotals(_storage, Equipment.CopyTools(), pack,
            out float weight, out float volume);

        TotalWeightKg = weight;
        UsedVolumeLitres = volume;
        MovementFactor = Rules.SpeedFactor(
            weight,
            pack?.ComfortableWeightKg ?? Rules.ComfortableWeightKg,
            pack?.MaximumWeightKg ?? Rules.MaximumWeightKg);

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