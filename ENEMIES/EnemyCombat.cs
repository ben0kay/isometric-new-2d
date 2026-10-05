// Checks attack eligibility and starts optional enemy sequences.
// Weapon cooldowns continue advancing while sequences own movement and attacks.
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
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve sibling components without depending on their Ready callback order.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Enemy>();
        _weapon = GetNode<Weapon>("../Weapon");
        _sequence = GetNode<EnemySequence>("../Sequence");
    }
    #endregion

    #region Combat
      // =========================================================
    // Advance weapon timing and check attacks at ten hertz; notify accepted melee hits.
    public void Tick(double delta)
    {
        if (!_actor.Initialized || !_actor.IsActivated || _actor.SpawnPending ||
            _actor.IsQueuedForDeletion() || _actor.Health?.IsAlive != true)
            return;

        _weapon.Tick(delta);
        _timer -= delta;
        if (_sequence.IsRunning || _timer > 0.0) return;
        _timer = 0.1;

        if (!_actor.HasTarget || !_actor.HasSight) return;

        EnemyCombatSettings combat = _actor.Definition.Combat;
        Vector2 point = _actor.Target.GlobalPosition;
        if (_actor.GlobalPosition.DistanceSquaredTo(point) >
            combat.AttackRange * combat.AttackRange) return;

        if (_sequence.IsConfigured)
        {
            _sequence.TryStart();
            return;
        }

        if (combat is RangedCombatSettings)
        {
            _weapon.TryFireAt(point);
            return;
        }

        if (combat is not MeleeCombatSettings melee) return;

        _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation")
            as WorldNavigation;
        if (_navigation == null ||
            !_navigation.CanTravelDirectly(_actor.GlobalPosition, point)) return;

        Health targetHealth = _actor.Target.GetNodeOrNull<Health>("Systems/Health");
        if (targetHealth?.Damage(melee.Damage, melee.DamageType) != true) return;

        _timer = Math.Max(0.1, melee.Cooldown);
        MeleeExecuted?.Invoke();
    }
    #endregion

        #region Events
    public event Action MeleeExecuted;
    #endregion
}