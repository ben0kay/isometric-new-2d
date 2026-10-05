// Owns equipped tools, backpack selection, and the active weapon attack.
// Inventory coordinates equipment transfers so capacity checks remain centralized.
using Godot;
using System;

public partial class PlayerEquipment : Node
{
    #region Configuration
    [ExportGroup("Tools")]
    [Export(PropertyHint.Range, "1,8,1")]
    public int ToolSlotCount { get; set; } = 3;
    [Export] public Godot.Collections.Array<ItemDefinition> StartingTools
        { get; set; } = new();

    [ExportGroup("Backpack")]
    [Export] public BackpackDefinition StartingBackpack { get; set; }
    #endregion

    #region State
    public event Action SelectionChanged;
    public BackpackDefinition Backpack { get; private set; }
    public int SelectedSlot { get; private set; }
    public ItemDefinition CurrentTool =>
    GetNodeOrNull<PlayerHotbar>("../Hotbar")?.CurrentItem;

    private ItemDefinition[] _tools = Array.Empty<ItemDefinition>();
    private Weapon _weapon;
    #endregion

    #region Lifecycle
    // =========================================================
    // Populate equipment and resolve the actor's existing weapon component.
    public override void _Ready()
    {
        ToolSlotCount = Math.Clamp(ToolSlotCount, 1, 8);
        _tools = new ItemDefinition[ToolSlotCount];

        for (int i = 0; i < Math.Min(_tools.Length, StartingTools.Count); i++)
        {
            ItemDefinition item = StartingTools[i];
            if (item?.Attack != null) _tools[i] = item;
        }

        Backpack = StartingBackpack;
        _weapon = GetNode<Weapon>("../Weapon");
        SyncAttack();
    }
    #endregion

    #region Queries
    // =========================================================
    // Return the item equipped in a valid tool slot.
    public ItemDefinition GetTool(int index)
    {
        return index >= 0 && index < _tools.Length ? _tools[index] : null;
    }

    // =========================================================
    // Copy tools for validating an inventory transaction.
    internal ItemDefinition[] CopyTools()
    {
        return (ItemDefinition[])_tools.Clone();
    }
    #endregion

    #region Selection
    // =========================================================
    // Select a tool slot, including empty hands.
    public void Select(int index)
    {
        if (index < 0 || index >= _tools.Length) return;
        SelectedSlot = index;
        SyncAttack();
        SelectionChanged?.Invoke();
    }

    // =========================================================
    // Cycle between occupied tool slots without stopping on empty slots.
    public void Cycle(int direction)
    {
        if (_tools.Length == 0 || direction == 0) return;
        int step = direction > 0 ? 1 : -1;

        for (int offset = 1; offset <= _tools.Length; offset++)
        {
            int next = ((SelectedSlot + offset * step) % _tools.Length
                + _tools.Length) % _tools.Length;
            if (_tools[next]?.Attack == null) continue;
            Select(next);
            return;
        }
    }

    // =========================================================
    // Apply the selected tool's attack and clear an old mining beam.
    private void SyncAttack()
    {
        AttackDefinition attack = CurrentTool?.Attack;
        if (_weapon.Attack == attack) return;

        GetNodeOrNull<MiningEmitter>("../MiningEmitter")?.Stop();
        _weapon.Attack = attack;
    }

    // =========================================================
// Synchronize attacks with the selected inventory shortcut.
public void RefreshActiveAttack()
{
    SyncAttack();
}
    #endregion

    #region Inventory Integration
    // =========================================================
    // Commit equipment already validated by the inventory coordinator.
    internal void ApplyContents(
        ItemDefinition[] tools, BackpackDefinition backpack)
    {
        _tools = tools;
        Backpack = backpack;
        SyncAttack();
    }
    #endregion
}