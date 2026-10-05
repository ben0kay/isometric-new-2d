// Defines one reusable attribute modifier for equipment, upgrades, or temporary effects.
using Godot;

public enum StatModifierOperation { Flat, AddPercent, Multiply }

[Tool, GlobalClass]
public partial class StatModifier : Resource
{
    #region Modifier
    [ExportGroup("Modifier")]
    [Export] public PlayerStat Stat { get; set; } = PlayerStat.MiningEfficiency;
    [Export] public StatModifierOperation Operation { get; set; } =
        StatModifierOperation.AddPercent;
    [Export] public float Amount { get; set; } = 0.25f;
    #endregion
}