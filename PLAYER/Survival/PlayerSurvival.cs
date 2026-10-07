// Accumulates activity-weighted survival costs and applies them once per second.
// Jump costs apply immediately; climate multipliers are available for future biomes.
using Godot;
using System;

public partial class PlayerSurvival : Node
{
    #region Configuration
    [Export] public SurvivalProfile Profile { get; set; }
    #endregion

    #region State
    private PlayerVitals _vitals;
    private double _elapsed, _workRemaining;
    private double _foodPending, _waterPending, _fatiguePending;
    private float _climateFood = 1f, _climateWater = 1f;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve reserve storage without starting an independent update loop.
    public override void _Ready()
    {
        _vitals = GetNode<PlayerVitals>("../Vitals");
        Profile ??= new SurvivalProfile();
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Activity
    // =========================================================
    // Mark successful work for its action duration without stacking overlapping time.
    public void ReportWork(double seconds)
    {
        if (!_vitals.Health.IsAlive || !double.IsFinite(seconds)) return;
        _workRemaining = Math.Max(
            _workRemaining, Math.Max(0.0, seconds));
    }

    // =========================================================
    // Apply one immediate cost when a jump actually starts.
    public void OnJump()
    {
        if (!_vitals.Health.IsAlive) return;

        if (Profile.FoodDrainEnabled)
            _vitals.Change(PlayerReserve.Food, -Positive(Profile.JumpFoodCost));
        if (Profile.WaterDrainEnabled)
            _vitals.Change(PlayerReserve.Water, -Positive(Profile.JumpWaterCost));
        if (Profile.FatigueEnabled)
            _vitals.Change(PlayerReserve.Fatigue, Positive(Profile.JumpFatigueCost));
    }

    // =========================================================
    // Accept future biome modifiers; one means ordinary climate.
    public void SetClimateMultipliers(float food, float water)
    {
        _climateFood = Positive(food);
        _climateWater = Positive(water);
    }
    #endregion

    #region Survival Clock
    // =========================================================
    // Accumulate the exact time spent moving and working before the next update.
    public void Tick(double delta, bool walking)
    {
        if (!_vitals.Health.IsAlive)
        {
            _elapsed = _workRemaining = 0.0;
            _foodPending = _waterPending = _fatiguePending = 0.0;
            return;
        }

        if (!double.IsFinite(delta) || delta <= 0.0) return;

        double walkingSeconds = walking ? delta : 0.0;
        double workingSeconds = Math.Min(delta, _workRemaining);
        _workRemaining = Math.Max(0.0, _workRemaining - delta);

        if (Profile.FoodDrainEnabled)
            _foodPending += DrainAmount(
                PlayerReserve.Food, Profile.FoodMinutesToEmpty,
                WeightedSeconds(delta, walkingSeconds, workingSeconds,
                    Profile.WalkingFoodMultiplier, Profile.WorkingFoodMultiplier)
                    * _climateFood);

        if (Profile.WaterDrainEnabled)
            _waterPending += DrainAmount(
                PlayerReserve.Water, Profile.WaterMinutesToEmpty,
                WeightedSeconds(delta, walkingSeconds, workingSeconds,
                    Profile.WalkingWaterMultiplier, Profile.WorkingWaterMultiplier)
                    * _climateWater);

        if (Profile.FatigueEnabled)
            _fatiguePending += (
                delta * Positive(Profile.AwakeFatiguePerMinute) +
                walkingSeconds * Positive(Profile.WalkingFatiguePerMinute) +
                workingSeconds * Positive(Profile.WorkingFatiguePerMinute)) / 60.0;

        _elapsed += delta;
        if (_elapsed < 1.0) return;
        _elapsed %= 1.0;
        ApplyPending();
    }

    // =========================================================
    // Add activity surcharges to the ordinary elapsed survival time.
    private static double WeightedSeconds(
        double seconds, double walking, double working,
        float walkingMultiplier, float workingMultiplier)
    {
        return seconds +
            walking * Math.Max(0f, Positive(walkingMultiplier) - 1f) +
            working * Math.Max(0f, Positive(workingMultiplier) - 1f);
    }

    // =========================================================
    // Convert weighted seconds into capacity-relative food or water depletion.
    private double DrainAmount(
        PlayerReserve reserve, float minutes, double seconds)
    {
        if (!float.IsFinite(minutes) || minutes <= 0f) return 0.0;
        return _vitals.GetMaximum(reserve) * seconds / (minutes * 60.0);
    }

    // =========================================================
    // Apply accumulated costs and clear the pending amounts.
    private void ApplyPending()
    {
        if (Profile.FoodDrainEnabled)
            _vitals.Change(PlayerReserve.Food,
                -(float)Math.Min(_foodPending, float.MaxValue));
        if (Profile.WaterDrainEnabled)
            _vitals.Change(PlayerReserve.Water,
                -(float)Math.Min(_waterPending, float.MaxValue));
        if (Profile.FatigueEnabled)
            _vitals.Change(PlayerReserve.Fatigue,
                (float)Math.Min(_fatiguePending, float.MaxValue));

        _foodPending = _waterPending = _fatiguePending = 0.0;
    }

    // =========================================================
    // Keep malformed runtime settings from introducing invalid reserve values.
    private static float Positive(float value)
    {
        return float.IsFinite(value) ? Mathf.Max(0f, value) : 0f;
    }
    #endregion
}