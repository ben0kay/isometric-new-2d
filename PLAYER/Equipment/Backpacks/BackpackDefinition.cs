// Defines backpack capacity independently from storage and carrying formulas.
// An unequipped backpack behaves like an ordinary inventory item.
using Godot;

[Tool, GlobalClass]
public partial class BackpackDefinition : ItemDefinition
{
    #region Capacity
    [ExportGroup("Backpack / Storage")]
    [Export(PropertyHint.Range, "1,96,1")]
    public int SlotCount { get; set; } = 24;
    [Export] public float CapacityLitres { get; set; } = 30f;
    #endregion

    #region Carrying
    [ExportGroup("Backpack / Carrying")]
    [Export] public float ComfortableWeightKg { get; set; } = 12f;
    [Export] public float MaximumWeightKg { get; set; } = 28f;
    #endregion
}