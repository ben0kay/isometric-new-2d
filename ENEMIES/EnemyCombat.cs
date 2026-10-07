// Checks attack eligibility and starts optional enemy sequences.
// Ranged attacks aim at visible actor hitboxes; melee uses ground distance.
using Godot;
using System;

public partial class EnemyCombat : Node
{
    #region State
    private Enemy _actor;
    private Weapon _weapon;
    private EnemySequence _sequence;
    private WorldNavigation _navigation;
    private double _timer;
    public event Action MeleeExecuted;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve sibling components without depending on ready order.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Enemy>();
        _weapon = GetNode<Weapon>("../Weapon");
        _sequence = GetNode<EnemySequence>("../Sequence");
    }
    #endregion

    #region Combat
// =========================================================
// Require a same-layer target before starting any attack or sequence.
public void Tick(double delta)
{
    if (!_actor.Initialized || !_actor.IsActivated ||
        _actor.SpawnPending || _actor.IsQueuedForDeletion() ||
        _actor.Health?.IsAlive != true)
        return;

    _weapon.Tick(delta);
    _timer -= delta;
    if (_sequence.IsRunning || _timer > 0.0) return;
    _timer = 0.1;

    if (!_actor.HasTarget || !_actor.HasSight ||
        !WorldLayerMember.Same(_actor, _actor.Target))
        return;

    EnemyCombatSettings combat = _actor.Definition.Combat;
    Vector2 point = _actor.Target.GlobalPosition;

    if (_actor.GlobalPosition.DistanceSquaredTo(point) >
        combat.AttackRange * combat.AttackRange)
        return;

    if (_sequence.IsConfigured)
    {
        _sequence.TryStart();
        return;
    }

    if (combat is RangedCombatSettings)
    {
        _weapon.TryFireAtActor(_actor.Target);
        return;
    }

    if (combat is not MeleeCombatSettings melee) return;

    _navigation = WorldNavigation.For(_actor);
    if (_navigation == null ||
        !_navigation.CanTravelDirectly(_actor.GlobalPosition, point))
        return;

    Health targetHealth = _actor.Target.GetNodeOrNull<Health>(
        "Systems/Health");

    if (targetHealth?.Damage(melee.Damage, melee.DamageType) != true)
        return;

    _timer = Math.Max(0.1, melee.Cooldown);
    MeleeExecuted?.Invoke();
}
    #endregion
}