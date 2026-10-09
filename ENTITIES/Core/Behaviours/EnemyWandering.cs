// Owns optional robot wandering, its home anchor and waiting timer.
// Combat can interrupt movement without changing wandering configuration.
using Godot;

public sealed class EnemyWandering
{
    #region State
    public Vector2 Home { get; private set; }

    private readonly Enemy _actor;
    private readonly RandomNumberGenerator _rng;
    private double _wait;
    #endregion

    #region Setup
    // =========================================================
    // Share the actor's seeded random stream and preserve its initial wait.
    public EnemyWandering(
        Enemy actor, RandomNumberGenerator rng, Vector2 home)
    {
        _actor = actor;
        _rng = rng;
        Home = home;
        Pause();
    }

    // =========================================================
    // Move the home anchor when the actor crosses a world layer.
    public void SetHome(Vector2 position)
    {
        Home = position;
    }
    #endregion

    #region Scheduling
    // =========================================================
    // Advance waiting with the existing actor tick, including during combat.
    public void Tick(double delta)
    {
        _wait -= delta;
    }

    // =========================================================
    // Choose the next idle wait from the definition.
    private void Pause()
    {
        Vector2 wait = _actor.Definition.WanderWait;
        _wait = _rng.RandfRange(wait.X, wait.Y);
    }
    #endregion

    #region Decisions
    // =========================================================
    // Return home after long chases or choose a valid wandering destination.
    public void Decide()
    {
        EnemyDefinition definition = _actor.Definition;
        EnemyMotor motor = _actor.Motor;

        if (!definition.WanderingEnabled)
        {
            motor.Stop();
            return;
        }

        Vector2 position = _actor.GlobalPosition;

        if (position.DistanceSquaredTo(Home) >
            definition.HomeLeash * definition.HomeLeash)
        {
            _wait = 0.0;
            motor.SetGoal(Home, definition.WanderSpeed, 8f);
            return;
        }

        if (motor.HasGoal && !motor.Arrived && !motor.IsStuck)
            return;

        if (motor.HasGoal)
        {
            motor.Stop();
            Pause();
        }

        if (_wait > 0.0) return;

        if (position.DistanceSquaredTo(Home) >
            definition.WanderRadius * definition.WanderRadius)
        {
            motor.SetGoal(Home, definition.WanderSpeed, 8f);
            return;
        }

        WorldNavigation navigation = _actor.Navigation;
        if (navigation == null) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            float angle = _rng.Randf() * Mathf.Tau;
            float radius = Mathf.Sqrt(_rng.Randf()) *
                definition.WanderRadius;
            Vector2 point =
                Home + Vector2.Right.Rotated(angle) * radius;

            if (!navigation.CanTravelDirectly(point, point))
                continue;

            motor.SetGoal(point, definition.WanderSpeed, 8f);
            return;
        }

        _wait = 1.0;
    }
    #endregion
}