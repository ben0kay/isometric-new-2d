// Stores shared starting attributes; runtime players copy these values.
// Equipment and upgrades never modify this shared profile.
using Godot;
using System;

[Tool, GlobalClass]
public partial class PlayerStatProfile : Resource
{
    #region Capacities
    [ExportGroup("Capacities")]
    [ExportSubgroup("Combat And Suit")]
    [Export] public float MaxHealth { get; set; } = 100f;
    [Export] public float MaxStamina { get; set; } = 100f;
    [Export] public float MaxOxygen { get; set; } = 100f;
    [Export] public float MaxEnergy { get; set; } = 100f;

    [ExportSubgroup("Survival")]
    [Export] public float MaxFood { get; set; } = 100f;
    [Export] public float MaxWater { get; set; } = 100f;
    [Export] public float MaxFatigue { get; set; } = 100f;
    #endregion

    #region Performance
    [ExportGroup("Performance")]
    [Export] public float MovementSpeed { get; set; } = 240f;
    [Export] public float MiningEfficiency { get; set; } = 1f;
    #endregion

    #region Queries
    // =========================================================
    // Read a starting attribute without reflection or string lookups.
    public float Get(PlayerStat stat)
    {
        return stat switch
        {
            PlayerStat.MaxHealth => MaxHealth,
            PlayerStat.MaxStamina => MaxStamina,
            PlayerStat.MaxOxygen => MaxOxygen,
            PlayerStat.MaxEnergy => MaxEnergy,
            PlayerStat.MaxFood => MaxFood,
            PlayerStat.MaxWater => MaxWater,
            PlayerStat.MaxFatigue => MaxFatigue,
            PlayerStat.MovementSpeed => MovementSpeed,
            PlayerStat.MiningEfficiency => MiningEfficiency,
            _ => throw new ArgumentOutOfRangeException(nameof(stat))
        };
    }
    #endregion
}