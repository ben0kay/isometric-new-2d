// Shares chasing and ranged spacing between any health-bearing entity types.
// The caller supplies positioning settings and optional cross-layer pursuit goals.
using Godot;

public sealed class EntityCombatMovement
{
    #region State
    private readonly EntityBody _actor;
    private readonly EntityWandering _wandering;
    private readonly RandomNumberGenerator _rng;
    private bool _retreating;
    private float? _chosenRangedDistance;
    #endregion

    #region Setup
    // =========================================================
    // Bind shared movement without requiring Enemy or a species definition.
    public EntityCombatMovement(
        EntityBody actor, EntityWandering wandering,
        RandomNumberGenerator random)
    {
        _actor = actor;
        _wandering = wandering;
        _rng = random;
    }

    // =========================================================
    // Reset temporary retreat state without changing personal spacing.
    public void Reset()
    {
        _retreating = false;
    }
    #endregion

    #region Decisions
    // =========================================================
    // Choose idle movement, supplied entrance pursuit or combat positioning.
    public void Decide(
        Node2D target, bool hasSight,
        EntityCombatMovementSettings settings,
        Vector2? layerGoal = null)
    {
        EntityMotor motor = _actor.Motor;

        if (!GodotObject.IsInstanceValid(target) ||
            !target.IsInsideTree() || target.IsQueuedForDeletion())
        {
            _wandering?.Decide();
            return;
        }

        if (!WorldLayerMember.Same(_actor, target))
        {
            if (layerGoal.HasValue)
                motor.SetGoal(layerGoal.Value, settings.Speed, 6f);
            else
                motor.Stop();

            return;
        }

        Vector2 point = target.GlobalPosition;

        if (settings.Positioning == EntityCombatPositioning.Chase)
        {
            motor.SetGoal(point, settings.Speed, settings.StopDistance);
            return;
        }

        if (settings.Positioning == EntityCombatPositioning.KeepDistance)
        {
            DecideSpacing(point, hasSight, settings);
            return;
        }

        motor.Stop();
    }

    // =========================================================
    // Chase around cover, retreat when crowded or hold a personal range.
    private void DecideSpacing(
        Vector2 targetPoint, bool hasSight,
        EntityCombatMovementSettings settings)
    {
        EntityMotor motor = _actor.Motor;
        float chosenDistance = GetRangedDistance(settings);

        if (!hasSight)
        {
            _retreating = false;
            motor.SetGoal(targetPoint, settings.Speed, 1f);
            return;
        }

        float distance = _actor.GlobalPosition.DistanceTo(targetPoint);

        if (distance < settings.BackAwayRange) _retreating = true;
        if (distance >= chosenDistance) _retreating = false;

        if (_retreating)
        {
            DecideRetreat(targetPoint, settings.Speed);
            return;
        }

        if (distance <= chosenDistance)
        {
            motor.Stop();
            return;
        }

        motor.SetGoal(targetPoint, settings.Speed, settings.StopDistance);
    }

    // =========================================================
    // Find a short clear retreat around nearby obstacles.
    private void DecideRetreat(Vector2 targetPoint, float speed)
    {
        WorldNavigation navigation = _actor.Navigation;
        EntityMotor motor = _actor.Motor;

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

            Vector2 point = position + away.Rotated(angle) * 80f;
            if (!navigation.CanTravelDirectly(position, point))
                continue;

            motor.SetGoal(point, speed, 6f);
            return;
        }

        motor.Stop();
    }

    // =========================================================
    // Choose one distance per actor from its supplied spacing settings.
    private float GetRangedDistance(EntityCombatMovementSettings settings)
    {
        if (_chosenRangedDistance.HasValue)
            return _chosenRangedDistance.Value;

        float weight = Mathf.Pow(_rng.Randf(), settings.RangeBias);
        _chosenRangedDistance = Mathf.Lerp(
            settings.PreferredRange, settings.AttackRange, weight);

        return _chosenRangedDistance.Value;
    }
    #endregion
}