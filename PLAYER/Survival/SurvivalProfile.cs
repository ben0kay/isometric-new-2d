// Stores passive drain, exertion multipliers and immediate action costs.
// All settings belong to the profile; current reserves belong to PlayerVitals.
using Godot;

[Tool, GlobalClass]
public partial class SurvivalProfile : Resource
{
    #region Food
    [ExportGroup("Food")]
    [Export] public bool FoodDrainEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.5,1440,0.5")]
    public float FoodMinutesToEmpty { get; set; } = 120f;
    [Export(PropertyHint.Range, "1,10,0.05")]
    public float WalkingFoodMultiplier { get; set; } = 1.25f;
    [Export(PropertyHint.Range, "1,10,0.05")]
    public float WorkingFoodMultiplier { get; set; } = 1.5f;
    #endregion

    #region Water
    [ExportGroup("Water")]
    [Export] public bool WaterDrainEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0.5,1440,0.5")]
    public float WaterMinutesToEmpty { get; set; } = 90f;
    [Export(PropertyHint.Range, "1,10,0.05")]
    public float WalkingWaterMultiplier { get; set; } = 1.5f;
    [Export(PropertyHint.Range, "1,10,0.05")]
    public float WorkingWaterMultiplier { get; set; } = 1.75f;
    #endregion

    #region Fatigue
    [ExportGroup("Fatigue")]
    [Export] public bool FatigueEnabled { get; set; } = true;
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float AwakeFatiguePerMinute { get; set; } = 0.15f;
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float WalkingFatiguePerMinute { get; set; } = 0.3f;
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float WorkingFatiguePerMinute { get; set; } = 0.6f;
    #endregion

    #region Jumping
    [ExportGroup("Jump Costs")]
    [Export(PropertyHint.Range, "0,10,0.01")]
    public float JumpFoodCost { get; set; } = 0.1f;
    [Export(PropertyHint.Range, "0,10,0.01")]
    public float JumpWaterCost { get; set; } = 0.2f;
    [Export(PropertyHint.Range, "0,10,0.05")]
    public float JumpFatigueCost { get; set; } = 0.5f;
    #endregion

    #region Sprinting
[ExportGroup("Sprint")]
[Export(PropertyHint.Range, "1,3,0.05")]
public float SprintSpeedMultiplier { get; set; } = 1.6f;

[Export(PropertyHint.Range, "0,100,0.5")]
public float SprintStaminaPerSecond { get; set; } = 15f;

[Export(PropertyHint.Range, "0,100,0.5")]
public float StaminaRecoveryPerSecond { get; set; } = 12f;

[Export(PropertyHint.Range, "0,10,0.1")]
public double StaminaRecoveryDelaySeconds { get; set; } = 1.5;

[Export(PropertyHint.Range, "1,10,0.05")]
public float SprintFoodMultiplier { get; set; } = 2f;

[Export(PropertyHint.Range, "1,10,0.05")]
public float SprintWaterMultiplier { get; set; } = 2.5f;

[Export(PropertyHint.Range, "0,10,0.05")]
public float SprintFatiguePerMinute { get; set; } = 1.2f;
#endregion

#region Consequences
[ExportGroup("Consequences")]
[Export(PropertyHint.Range, "0,20,0.1")]
public float StarvationDamagePerSecond { get; set; } = 0.5f;

[Export(PropertyHint.Range, "0,20,0.1")]
public float DehydrationDamagePerSecond { get; set; } = 1f;
#endregion
}