// Defines replenishment and use timing for food and drink items.
// Current reserves and inventory quantities belong to the player.
using Godot;
using System;

[Tool, GlobalClass]
public partial class ConsumableDefinition : Resource
{
    #region Configuration
    [ExportGroup("Replenishment")]
    [Export(PropertyHint.Range, "0,1000,0.5")]
    public float FoodRestored { get; set; }

    [Export(PropertyHint.Range, "0,1000,0.5")]
    public float WaterRestored { get; set; }

    [ExportGroup("Consumption")]
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public double UseIntervalSeconds { get; set; } = 0.75;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid replenishment amounts and consumption intervals.
    public void Validate()
    {
        if (!float.IsFinite(FoodRestored) || FoodRestored < 0f ||
            !float.IsFinite(WaterRestored) || WaterRestored < 0f ||
            FoodRestored + WaterRestored <= 0f ||
            !double.IsFinite(UseIntervalSeconds) ||
            UseIntervalSeconds < 0.1)
            throw new InvalidOperationException(
                "Consumable requires valid restoration and use settings.");
    }
    #endregion
}