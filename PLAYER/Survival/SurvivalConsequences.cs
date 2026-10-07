// Applies reserve-depletion consequences using the shared survival clock.
// Fractional damage accumulates until it reaches a whole health point.
using System;

public sealed class SurvivalConsequences
{
    #region State
    private readonly PlayerVitals _vitals;
    private double _pendingDamage;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind existing reserves without creating another scene node.
    public SurvivalConsequences(PlayerVitals vitals)
    {
        _vitals = vitals;
    }
    #endregion

    #region Consequences
    // =========================================================
    // Accumulate starvation and dehydration damage while reserves are empty.
    public void Tick(double seconds, SurvivalProfile profile)
    {
        if (!_vitals.Health.IsAlive)
        {
            _pendingDamage = 0.0;
            return;
        }

        double rate = 0.0;

        if (profile.FoodDrainEnabled &&
            _vitals.GetCurrent(PlayerReserve.Food) <= 0f)
            rate += Math.Max(0f, profile.StarvationDamagePerSecond);

        if (profile.WaterDrainEnabled &&
            _vitals.GetCurrent(PlayerReserve.Water) <= 0f)
            rate += Math.Max(0f, profile.DehydrationDamagePerSecond);

        if (rate <= 0.0)
        {
            _pendingDamage = 0.0;
            return;
        }

        _pendingDamage += rate * seconds;
        int damage = (int)Math.Min(int.MaxValue, Math.Floor(_pendingDamage));
        if (damage <= 0) return;

        _pendingDamage -= damage;
        _vitals.Health.DamageSurvival(damage);
    }

    // =========================================================
    // Clear fractional damage after death.
    public void Reset()
    {
        _pendingDamage = 0.0;
    }
    #endregion
}