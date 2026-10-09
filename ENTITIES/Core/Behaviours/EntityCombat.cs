// Shares attack eligibility, melee delivery and optional weapon use across entities.
// Behaviour chooses targets; this helper owns combat checks and melee cooldown.
using Godot;
using System;

public sealed class EntityCombat
{
    #region State
    public event Action MeleeExecuted;

    private readonly EntityBody _actor;
    private readonly Weapon _weapon;
    private Node2D _cachedTarget;
    private Health _targetHealth;
    private double _meleeCooldown;
    #endregion

    #region Setup
    // =========================================================
    // Bind a shared actor with an optional weapon component.
    public EntityCombat(EntityBody actor, Weapon weapon = null)
    {
        _actor = actor;
        _weapon = weapon;
    }
    #endregion

    #region Health
    // =========================================================
    // Use shared actor health or the common health node for other actor types.
    private static Health FindHealth(Node2D actor)
    {
        return actor is EntityBody body
            ? body.Health
            : actor.GetNodeOrNull<Health>("Systems/Health");
    }

    // =========================================================
    // Reject missing, departing and dead actors during candidate scans.
    public static bool IsLiving(Node2D actor)
    {
        if (!GodotObject.IsInstanceValid(actor) ||
            !actor.IsInsideTree() || actor.IsQueuedForDeletion())
            return false;

        Health health = FindHealth(actor);
        return GodotObject.IsInstanceValid(health) && health.IsAlive;
    }

    // =========================================================
    // Cache the current target's health instead of searching on every attack check.
    private bool ValidateTarget(Node2D target)
    {
        if (!GodotObject.IsInstanceValid(target) || target == _actor ||
            !target.IsInsideTree() || target.IsQueuedForDeletion())
            return false;

        if (target != _cachedTarget ||
            !GodotObject.IsInstanceValid(_targetHealth))
        {
            _cachedTarget = target;
            _targetHealth = FindHealth(target);
        }

        return GodotObject.IsInstanceValid(_targetHealth) &&
            _targetHealth.IsAlive;
    }
    #endregion

    #region Combat
    // =========================================================
    // Advance cooldowns through the owning actor's existing update loop.
    public void Tick(double delta)
    {
        _meleeCooldown = Math.Max(0.0, _meleeCooldown - delta);
        _weapon?.Tick(delta);
    }

    // =========================================================
    // Check shared health, layer and reach before attempting an attack.
    public bool CanAttack(Node2D target, float range)
    {
        return GodotObject.IsInstanceValid(_actor) &&
            _actor.IsInsideTree() && !_actor.IsQueuedForDeletion() &&
            GodotObject.IsInstanceValid(_actor.Health) &&
            _actor.Health.IsAlive &&
            ValidateTarget(target) &&
            WorldLayerMember.Same(_actor, target) &&
            range > 0f &&
            _actor.GlobalPosition.DistanceSquaredTo(target.GlobalPosition)
                <= range * range;
    }

    // =========================================================
    // Deliver melee damage with the attacker identity and shared navigation checks.
    public bool TryMelee(
        Node2D target, float range, int damage,
        DamageType type, double cooldown,
        bool cooldownOnRejectedDamage = false)
    {
        if (_meleeCooldown > 0.0 || !CanAttack(target, range))
            return false;

        WorldNavigation navigation = _actor.Navigation;
        if (navigation == null ||
            !navigation.CanTravelDirectly(
                _actor.GlobalPosition, target.GlobalPosition))
            return false;

        double duration = Math.Max(0.1, cooldown);

        if (cooldownOnRejectedDamage)
            _meleeCooldown = duration;

        if (!_targetHealth.Damage(damage, type, _actor))
            return false;

        _meleeCooldown = duration;
        MeleeExecuted?.Invoke();
        return true;
    }

    // =========================================================
    // Delegate final projectile aiming and cover checks to the optional weapon.
    public bool TryWeapon(Node2D target, float range)
    {
        return GodotObject.IsInstanceValid(_weapon) &&
            CanAttack(target, range) &&
            _weapon.TryFireAtActor(target);
    }
    #endregion
}