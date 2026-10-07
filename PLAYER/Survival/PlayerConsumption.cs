// Consumes selected hotbar food while LMB is held, using item-defined timing.
// Successful uses remove one inventory item and immediately restore reserves.
using Godot;
using System;

public partial class PlayerConsumption : Node
{
    #region State
    private PlayerVitals _vitals;
    private PlayerInventory _inventory;
    private PlayerHotbar _hotbar;
    private double _cooldown;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve existing player systems and disable independent processing.
    public override void _Ready()
    {
        _vitals = GetNode<PlayerVitals>("../Vitals");
        _inventory = GetNode<PlayerInventory>("../Inventory");
        _hotbar = GetNode<PlayerHotbar>("../Hotbar");
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Consumption
    // =========================================================
    // Consume at most one item per update without catching up missed uses.
    public void Tick(double delta, bool useHeld)
    {
        if (!_vitals.Health.IsAlive)
        {
            _cooldown = 0.0;
            return;
        }

        _cooldown = Math.Max(0.0, _cooldown - delta);
        if (!useHeld || _cooldown > 0.0) return;

        ItemDefinition item = _hotbar.CurrentItem;
        ConsumableDefinition effect = item?.Consumable;
        if (effect == null) return;

        // Full reserves need no repeated inventory transaction attempts.
        if (!Needs(effect))
        {
            _cooldown = 0.25;
            return;
        }

        InventoryAddress? address = _hotbar.GetAddress(
            _hotbar.SelectedSlot);

        if (!address.HasValue ||
            !_inventory.TryTakeOne(address.Value, item))
            return;

        _vitals.Change(PlayerReserve.Food, effect.FoodRestored);
        _vitals.Change(PlayerReserve.Water, effect.WaterRestored);
        _cooldown = Math.Max(0.1, effect.UseIntervalSeconds);
    }

    // =========================================================
    // Allow use when either reserve can benefit from this particular item.
    private bool Needs(ConsumableDefinition effect)
    {
        return (effect.FoodRestored > 0f &&
                _vitals.GetCurrent(PlayerReserve.Food) <
                _vitals.GetMaximum(PlayerReserve.Food)) ||
            (effect.WaterRestored > 0f &&
                _vitals.GetCurrent(PlayerReserve.Water) <
                _vitals.GetMaximum(PlayerReserve.Water));
    }
    #endregion
}