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

    [Export(PropertyHint.Range, "0,100,0.05")]
public float FatigueRelief { get; set; }
    public float FoodRestored { get; set; }

    [Export(PropertyHint.Range, "0,1000,0.5")]
    public float WaterRestored { get; set; }

    [ExportGroup("Consumption")]
    [Export(PropertyHint.Range, "0.1,10,0.05")]
    public double UseIntervalSeconds { get; set; } = 0.75;
    #endregion

    #region Validation
// =========================================================
// Validate restoration, fatigue relief and consumption timing.
public void Validate()
{
    if (!float.IsFinite(FoodRestored) || FoodRestored < 0f ||
        !float.IsFinite(WaterRestored) || WaterRestored < 0f ||
        !float.IsFinite(FatigueRelief) || FatigueRelief < 0f ||
        FoodRestored + WaterRestored + FatigueRelief <= 0f ||
        !double.IsFinite(UseIntervalSeconds) ||
        UseIntervalSeconds < 0.1)
        throw new ArgumentException(
            "Consumable requires valid restoration and use settings.");
}
    #endregion
}