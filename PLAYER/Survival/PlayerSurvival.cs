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

    private SurvivalConsequences _consequences;
private double _staminaRecoveryDelay;
private bool _sprintExhausted;
public bool IsSprinting { get; private set; }
    #endregion

    #region Lifecycle
// =========================================================
// Resolve reserves and consequences without independent processing loops.
public override void _Ready()
{
    _vitals = GetNode<PlayerVitals>("../Vitals");
    Profile ??= new SurvivalProfile();
    _consequences = new SurvivalConsequences(_vitals);
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

    // =========================================================
// Spend stamina for sprint movement and recover after a short exertion delay.
public float UpdateSprint(double delta, bool requested)
{
    IsSprinting = false;
    if (!_vitals.Health.IsAlive || delta <= 0.0) return 1f;

    // Releasing Shift allows another sprint after exhausting stamina.
    if (!requested) _sprintExhausted = false;

    float cost = Positive(Profile.SprintStaminaPerSecond) * (float)delta;
    float stamina = _vitals.GetCurrent(PlayerReserve.Stamina);

    if (requested && !_sprintExhausted && stamina > 0f)
    {
        if (stamina >= cost)
        {
            _vitals.Change(PlayerReserve.Stamina, -cost);
            _staminaRecoveryDelay =
                Math.Max(0.0, Profile.StaminaRecoveryDelaySeconds);
            IsSprinting = true;
            return Mathf.Max(1f, Profile.SprintSpeedMultiplier);
        }

        _vitals.Change(PlayerReserve.Stamina, -stamina);
        _sprintExhausted = true;
        _staminaRecoveryDelay =
            Math.Max(0.0, Profile.StaminaRecoveryDelaySeconds);
    }
    else if (requested && stamina <= 0f)
        _sprintExhausted = true;

    double recoveryTime = Math.Max(0.0, delta - _staminaRecoveryDelay);
    _staminaRecoveryDelay = Math.Max(0.0, _staminaRecoveryDelay - delta);

    if (recoveryTime > 0.0)
        _vitals.Change(PlayerReserve.Stamina,
            Positive(Profile.StaminaRecoveryPerSecond) * (float)recoveryTime);

    return 1f;
}
    #endregion

    #region Survival Clock
// =========================================================
// Accumulate walking, sprinting and work costs on the shared survival clock.
public void Tick(double delta, bool walking, bool sprinting = false)
{
    if (!_vitals.Health.IsAlive)
    {
        _elapsed = _workRemaining = 0.0;
        _foodPending = _waterPending = _fatiguePending = 0.0;
        _staminaRecoveryDelay = 0.0;
        _sprintExhausted = IsSprinting = false;
        _consequences.Reset();
        return;
    }

    if (!double.IsFinite(delta) || delta <= 0.0) return;

    double movingSeconds = walking ? delta : 0.0;
    double workingSeconds = Math.Min(delta, _workRemaining);
    _workRemaining = Math.Max(0.0, _workRemaining - delta);

    float movementFood = sprinting
        ? Profile.SprintFoodMultiplier : Profile.WalkingFoodMultiplier;
    float movementWater = sprinting
        ? Profile.SprintWaterMultiplier : Profile.WalkingWaterMultiplier;
    float movementFatigue = sprinting
        ? Profile.SprintFatiguePerMinute : Profile.WalkingFatiguePerMinute;

    if (Profile.FoodDrainEnabled)
        _foodPending += DrainAmount(
            PlayerReserve.Food, Profile.FoodMinutesToEmpty,
            WeightedSeconds(delta, movingSeconds, workingSeconds,
                movementFood, Profile.WorkingFoodMultiplier) * _climateFood);

    if (Profile.WaterDrainEnabled)
        _waterPending += DrainAmount(
            PlayerReserve.Water, Profile.WaterMinutesToEmpty,
            WeightedSeconds(delta, movingSeconds, workingSeconds,
                movementWater, Profile.WorkingWaterMultiplier) * _climateWater);

    if (Profile.FatigueEnabled)
        _fatiguePending += (
            delta * Positive(Profile.AwakeFatiguePerMinute) +
            movingSeconds * Positive(movementFatigue) +
            workingSeconds * Positive(Profile.WorkingFatiguePerMinute)) / 60.0;

    _elapsed += delta;
    if (_elapsed < 1.0) return;

    // Apply all accumulated time, including any long frame.
    double elapsed = _elapsed;
    _elapsed = 0.0;

    ApplyPending();
    _consequences.Tick(elapsed, Profile);
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