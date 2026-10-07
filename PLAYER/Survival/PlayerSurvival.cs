// Applies passive survival changes on one shared one-second clock.
// PlayerVitals owns reserve values; this helper owns their passive rules.
using Godot;
using System;

public partial class PlayerSurvival : Node
{
    #region Configuration
    [Export] public SurvivalProfile Profile { get; set; }
    #endregion

    #region State
    private PlayerVitals _vitals;
    private double _elapsed;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve reserves without starting another processing loop.
    public override void _Ready()
    {
        _vitals = GetNode<PlayerVitals>("../Vitals");
        Profile ??= new SurvivalProfile();
        SetProcess(false);
        SetPhysicsProcess(false);
    }
    #endregion

    #region Survival Clock
    // =========================================================
    // Apply whole elapsed seconds without losing fractional time or catch-up time.
    public void Tick(double delta)
    {
        if (!_vitals.Health.IsAlive)
        {
            _elapsed = 0.0;
            return;
        }

        if (delta <= 0.0) return;
        _elapsed += delta;
        if (_elapsed < 1.0) return;

        double seconds = Math.Floor(_elapsed);
        _elapsed -= seconds;

        if (Profile.FoodDrainEnabled)
            Drain(PlayerReserve.Food, Profile.FoodMinutesToEmpty, seconds);

        if (Profile.WaterDrainEnabled)
            Drain(PlayerReserve.Water, Profile.WaterMinutesToEmpty, seconds);
    }

    // =========================================================
    // Convert a full-to-empty duration into capacity-relative depletion.
    private void Drain(PlayerReserve reserve, float minutes, double seconds)
    {
        if (!float.IsFinite(minutes) || minutes <= 0f) return;

        double amount = _vitals.GetMaximum(reserve) *
            seconds / (minutes * 60.0);

        _vitals.Change(reserve,
            -(float)Math.Min(amount, float.MaxValue));
    }
    #endregion
}