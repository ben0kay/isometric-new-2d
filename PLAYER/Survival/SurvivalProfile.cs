// Stores tunable survival rules separately from current player reserves.
// Drain durations describe active gameplay minutes from full to empty.
using Godot;

[Tool, GlobalClass]
public partial class SurvivalProfile : Resource
{
    #region Nutrition
    [ExportGroup("Food")]
    [Export] public bool FoodDrainEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.5,1440,0.5")]
    public float FoodMinutesToEmpty { get; set; } = 120f;

    [ExportGroup("Water")]
    [Export] public bool WaterDrainEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0.5,1440,0.5")]
    public float WaterMinutesToEmpty { get; set; } = 90f;
    #endregion
}