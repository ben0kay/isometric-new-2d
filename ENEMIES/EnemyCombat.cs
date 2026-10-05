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
    // Check attacks at ten hertz while keeping weapon cooldowns in physics time.
    public void Tick(double delta)
    {
        _weapon.Tick(delta);
        _timer -= delta;
        if (_timer > 0.0) return;
        _timer = 0.1;

        if (!_actor.Health.IsAlive || !_actor.HasTarget || !_actor.HasSight) return;
        EnemyDefinition definition = _actor.Definition;
        Vector2 point = _actor.Target.GlobalPosition;
        if (_actor.GlobalPosition.DistanceSquaredTo(point) >
            definition.CombatRange * definition.CombatRange) return;

        if (definition.CombatStyle == EnemyCombatStyle.Ranged)
        {
            _weapon.TryFireAt(point);
            return;
        }

        _navigation ??= GetTree().GetFirstNodeInGroup("world_navigation") as WorldNavigation;
        if (_navigation == null ||
            !_navigation.CanTravelDirectly(_actor.GlobalPosition, point)) return;

        Health targetHealth = _actor.Target.GetNodeOrNull<Health>("Systems/Health");
        if (targetHealth?.Damage(definition.MeleeDamage, definition.MeleeDamageType) == true)
            _timer = Math.Max(0.1, definition.MeleeCooldown);
    }
    #endregion
}