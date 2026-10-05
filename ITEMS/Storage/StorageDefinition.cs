// Defines reusable container capacity and interaction settings.
// Individual containers keep their contents separate from this shared resource.
using Godot;
using System;

[Tool, GlobalClass]
public partial class StorageDefinition : Resource
{
    #region Identity
    [ExportGroup("Identity")]
    [Export] public string DisplayName { get; set; } = "Storage";
    #endregion

    #region Capacity
    [ExportGroup("Capacity")]
    [Export] public int SlotCount { get; set; } = 24;
    [Export] public float MaximumWeightKg { get; set; } = 250f;
    [Export] public float CapacityLitres { get; set; } = 180f;
    #endregion

    #region Interaction
    [ExportGroup("Interaction")]
    [Export] public float InteractionRange { get; set; } = 110f;
    #endregion

    #region Validation
    // =========================================================
    // Reject invalid capacities before constructing runtime storage.
    public void Validate()
    {
        if (SlotCount < 1 || SlotCount > 96 ||
            !float.IsFinite(MaximumWeightKg) || MaximumWeightKg <= 0f ||
            !float.IsFinite(CapacityLitres) || CapacityLitres <= 0f ||
            !float.IsFinite(InteractionRange) || InteractionRange <= 0f)
            throw new InvalidOperationException(
                $"Storage '{DisplayName}' has invalid settings.");
    }
    #endregion
}