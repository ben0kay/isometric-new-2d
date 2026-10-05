// Executes an enemy's selected combat style independently from movement decisions.
// Ranged attacks reuse the shared Weapon component and projectile pool.
using Godot;
using System;

public partial class EnemyCombat : Node
{
    #region State
    private Enemy _actor;
    private Weapon _weapon;
    private WorldNavigation _navigation;
    private double _timer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve sibling components after their owning scene enters the tree.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Enemy>();
        _weapon = GetNode<Weapon>("../Weapon");
    }
    #endregion

    #region Combat
// =========================================================
// Check the selected combat resource at ten hertz and deliver its attack.
public void Tick(double delta)
{
    if (!_actor.Initialized || !_actor.IsActivated || _actor.SpawnPending ||
        _actor.IsQueuedForDeletion() || _actor.Health?.IsAlive != true)
        return;

    _weapon.Tick(delta);
    _timer -= delta;
    if (_timer > 0.0) return;
    _timer = 0.1;

    if (!_actor.HasTarget || !_actor.HasSight) return;

    EnemyCombatSettings combat = _actor.Definition.Combat;
    Vector2 point = _actor.Target.GlobalPosition;
    if (_actor.GlobalPosition.DistanceSquaredTo(point) >
        combat.AttackRange * combat.AttackRange) return;

    if (combat is RangedCombatSettings)
    {
        _weapon.TryFireAt(point);
        return;
    }

    if (combat is not MeleeCombatSettings melee) return;

    _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
    if (_navigation == null ||
        !_navigation.CanTravelDirectly(_actor.GlobalPosition, point)) return;

    Health targetHealth = _actor.Target.GetNodeOrNull<Health>("Systems/Health");
    if (targetHealth?.Damage(melee.Damage, melee.DamageType) == true)
        _timer = Math.Max(0.1, melee.Cooldown);
}
    #endregion
}