// Owns robot chasing, ranged spacing, retreating and entrance pursuit.
// Attack delivery and shoot/dodge sequences remain separate components.
using Godot;

public sealed class EnemyCombatMovement
{
    #region State
    private readonly Enemy _actor;
    private readonly EnemyWandering _wandering;
    private readonly RandomNumberGenerator _rng;
    private bool _retreating;
    private float? _chosenRangedDistance;
    #endregion

    #region Setup
    // =========================================================
    // Bind existing movement and seeded randomness without adding nodes.
    public EnemyCombatMovement(
        Enemy actor, EnemyWandering wandering,
        RandomNumberGenerator rng)
    {
        _actor = actor;
        _wandering = wandering;
        _rng = rng;
    }

    // =========================================================
    // Reset retreat state while preserving the actor's chosen ranged distance.
    public void Reset()
    {
        _retreating = false;
    }
    #endregion

    #region Decisions
    // =========================================================
    // Select peaceful wandering, cave pursuit or normal combat positioning.
    public void Decide()
    {
        EnemyMotor motor = _actor.Motor;
        EnemyDefinition definition = _actor.Definition;

        if (!_actor.HasTarget)
        {
            _wandering.Decide();
            return;
        }

        if (!WorldLayerMember.Same(_actor, _actor.Target))
        {
            CaveEnemyPursuit pursuit = CaveEnemyPursuit.Find(_actor);

            if (pursuit != null &&
                pursuit.TryGetGoal(_actor, out Vector2 mouth))
                motor.SetGoal(mouth, definition.MoveSpeed, 6f);
            else
                motor.Stop();

            return;
        }

        Vector2 point = _actor.Target.GlobalPosition;
        EnemyCombatSettings combat = definition.Combat;

        if (combat is MeleeCombatSettings)
        {
            motor.SetGoal(
                point, definition.MoveSpeed, combat.StopDistance);
            return;
        }

        if (combat is RangedCombatSettings ranged)
        {
            DecideRanged(point, ranged);
            return;
        }

        motor.Stop();
    }

    // =========================================================
    // Chase around cover, retreat when crowded or stop at the chosen range.
    private void DecideRanged(
        Vector2 targetPoint, RangedCombatSettings ranged)
    {
        EnemyMotor motor = _actor.Motor;
        float speed = _actor.Definition.MoveSpeed;
        float chosenDistance = GetRangedDistance(ranged);

        if (!_actor.HasSight)
        {
            _retreating = false;
            motor.SetGoal(targetPoint, speed, 1f);
            return;
        }

        float distance = _actor.GlobalPosition.DistanceTo(targetPoint);

        if (distance < ranged.BackAwayRange) _retreating = true;
        if (distance >= chosenDistance) _retreating = false;

        if (_retreating)
        {
            DecideRetreat(targetPoint);
            return;
        }

        if (distance <= chosenDistance)
        {
            motor.Stop();
            return;
        }

        motor.SetGoal(targetPoint, speed, ranged.StopDistance);
    }

    // =========================================================
    // Try nearby retreat directions before giving up against an obstacle.
    private void DecideRetreat(Vector2 targetPoint)
    {
        WorldNavigation navigation = _actor.Navigation;
        EnemyMotor motor = _actor.Motor;

        if (navigation == null)
        {
            motor.Stop();
            return;
        }

        Vector2 position = _actor.GlobalPosition;
        Vector2 away = position - targetPoint;
        away = away.LengthSquared() > 0.001f
            ? away.Normalized() : Vector2.Right;

        for (int i = 0; i < 5; i++)
        {
            float angle = i == 0 ? 0f
                : (i % 2 == 1 ? 1f : -1f) *
                    ((i + 1) / 2) * Mathf.Pi / 4f;

            Vector2 point =
                position + away.Rotated(angle) * 80f;

            if (!navigation.CanTravelDirectly(position, point))
                continue;

            motor.SetGoal(point, _actor.Definition.MoveSpeed, 6f);
            return;
        }

        motor.Stop();
    }

    // =========================================================
    // Choose one personal combat distance, biased toward the preferred range.
    private float GetRangedDistance(RangedCombatSettings ranged)
    {
        if (_chosenRangedDistance.HasValue)
            return _chosenRangedDistance.Value;

        float weight = Mathf.Pow(_rng.Randf(), ranged.RangeBias);
        _chosenRangedDistance = Mathf.Lerp(
            ranged.PreferredRange, ranged.AttackRange, weight);

        return _chosenRangedDistance.Value;
    }
    #endregion
}