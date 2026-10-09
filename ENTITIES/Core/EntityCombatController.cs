// Connects existing robot combat settings to shared EntityCombat.
// Uses the shared entity sequence directly.
using Godot;
using System;

public partial class EntityCombatController : Node
{
    #region State
    public event Action MeleeExecuted;

    private Entity _actor;
    private EntitySequence _sequence;
    private EntityCombat _combat;
    private double _checkTimer;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind shared combat and the shared sequence component.
    public override void _Ready()
    {
        _actor = GetParent().GetParent<Entity>();
        _sequence = GetNode<EntitySequence>("../Sequence");

        _combat = new EntityCombat(
            _actor, GetNode<Weapon>("../Weapon"));

        _combat.MeleeExecuted += ForwardMelee;

        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release the shared combat event subscription.
    public override void _ExitTree()
    {
        if (_combat != null)
            _combat.MeleeExecuted -= ForwardMelee;
    }

    // =========================================================
    // Forward the event consumed by robot presentation.
    private void ForwardMelee()
    {
        MeleeExecuted?.Invoke();
    }
    #endregion

    #region Combat
    // =========================================================
    // Preserve staggered attack checks and exclusive sequence movement.
    public void Tick(double delta)
    {
        if (!_actor.Initialized || !_actor.IsActivated ||
            _actor.SpawnPending || _actor.IsQueuedForDeletion() ||
            _actor.Health?.IsAlive != true)
            return;

        _combat.Tick(delta);
        _checkTimer -= delta;

        if (_sequence.IsRunning || _checkTimer > 0.0 ||
            !StaggeredUpdate.Due(_actor, _actor.AiStaggerTicks, 13))
            return;

        _checkTimer = 0.1;

        if (!_actor.HasTarget || !_actor.HasSight)
            return;

        EntityCombatSettings settings = _actor.Definition.Combat;
        Node2D target = _actor.Target;

        if (!_combat.CanAttack(target, settings.AttackRange))
            return;

        if (_sequence.IsConfigured)
        {
            _sequence.TryStart();
            return;
        }

        if (settings is RangedCombatSettings)
        {
            _combat.TryWeapon(target, settings.AttackRange);
            return;
        }

        if (settings is MeleeCombatSettings melee)
            _combat.TryMelee(
                target, melee.AttackRange, melee.Damage,
                melee.DamageType, melee.Cooldown);
    }
    #endregion
}