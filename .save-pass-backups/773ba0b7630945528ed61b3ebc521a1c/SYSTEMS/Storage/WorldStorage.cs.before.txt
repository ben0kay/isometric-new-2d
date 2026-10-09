// Owns container contents and performs complete-stack inventory transfers.
// Loot containers extend this component without duplicating storage or transfer rules.
using Godot;
using System;

public partial class WorldStorage : Node
{
    #region Configuration
    [ExportGroup("Identity")]
    [Export] public string PersistentId { get; set; } = "";

    [ExportGroup("Storage")]
    [Export] public StorageDefinition Definition { get; set; }

    [ExportGroup("Owner")]
    [Export] public NodePath HostPath { get; set; } = new("../..");
    #endregion

    #region State
    public event Action Changed;
    public bool Initialized { get; private set; }
    public Node2D Host { get; private set; }
    public int SlotCount => _contents.SlotCount;
    public float WeightKg { get; private set; }
    public float VolumeLitres { get; private set; }
    public virtual bool CanDeposit => true;

    protected InventoryStorage _contents = new(0);
    #endregion

    #region Lifecycle
    // =========================================================
    // Register regular storage and loot with the same interaction interface.
    public override void _EnterTree()
    {
        AddToGroup("world_storage");
    }

    // =========================================================
    // Allocate contents and resolve the physical container.
    public override void _Ready()
    {
        if (Definition == null)
            throw new InvalidOperationException("WorldStorage requires a Definition.");

        Definition.Validate();
        Host = GetNode<Node2D>(HostPath);
        _contents = new InventoryStorage(Definition.SlotCount);
        Initialized = true;
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Queries
    // =========================================================
    // Read a stack without exposing mutable storage.
    public InventoryStack GetStack(int index)
    {
        return _contents.Get(index);
    }

// =========================================================
// Require matching world layers as well as normal proximity and health.
public bool CanInteract(Player player)
{
    return Initialized && GodotObject.IsInstanceValid(Host) &&
        Host.IsInsideTree() && !Host.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(player) && player.IsInsideTree() &&
        !player.IsQueuedForDeletion() &&
        WorldLayerMember.Same(Host, player) &&
        player.GetNode<Health>("Systems/Health").IsAlive &&
        Host.GlobalPosition.DistanceSquaredTo(player.GlobalPosition) <=
            Definition.InteractionRange * Definition.InteractionRange;
}
    #endregion

    #region Transfers
    // =========================================================
    // Validate capacity before removing a complete backpack stack.
    public bool TryDeposit(PlayerInventory player, int index, out string reason)
    {
        reason = "";
        if (!Initialized || player == null) return false;
        if (!CanDeposit)
        {
            reason = "This is salvage, not a storage container.";
            return false;
        }

        InventoryStack stack = player.GetStack(
            new InventoryAddress(InventoryArea.Bag, index));
        if (stack.IsEmpty) return false;

        InventoryStorage staged = _contents.Clone();
        if (!staged.TryAdd(stack.Item, stack.Count))
        {
            reason = "Container has no free slots or stack space.";
            return false;
        }

        staged.GetTotals(out float weight, out float volume);
        if (weight > Definition.MaximumWeightKg)
        {
            reason = "Container weight limit reached.";
            return false;
        }
        if (volume > Definition.CapacityLitres)
        {
            reason = "Container volume limit reached.";
            return false;
        }
        if (!player.TryRemoveBagStack(index, stack))
        {
            reason = "Backpack contents changed; try again.";
            return false;
        }

        _contents = staged;
        PublishContents();
        return true;
    }

    // =========================================================
    // Accept the stack into the backpack before removing container contents.
    public bool TryWithdraw(PlayerInventory player, int index, out string reason)
    {
        reason = "";
        if (!Initialized || player == null || !_contents.HasSlot(index))
            return false;

        InventoryStack stack = _contents.Get(index);
        if (stack.IsEmpty) return false;

        if (!player.TryCollect(stack.Item, stack.Count))
        {
            reason = "Backpack cannot accept this stack. Check slots, weight and volume.";
            return false;
        }

        _contents.Set(index, default);
        PublishContents();
        return true;
    }

    // =========================================================
    // Update physical totals and notify the shared inventory window.
    protected virtual void PublishContents()
    {
        _contents.GetTotals(out float weight, out float volume);
        WeightKg = weight;
        VolumeLitres = volume;
        Changed?.Invoke();
    }
    #endregion
}