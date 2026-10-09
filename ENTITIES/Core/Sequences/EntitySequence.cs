// Runs reusable action sequences for any shared entity actor.
// The owner supplies target, activity, sight and movement settings.
using Godot;
using System;

public sealed class EntitySequenceBinding
{
    public EntityBody Actor { get; init; }
    public Weapon Weapon { get; init; }
    public ulong RandomSeed { get; init; }

    public Func<EntitySequenceDefinition> GetDefinition { get; init; }
    public Func<Node2D> GetTarget { get; init; }
    public Func<bool> IsActive { get; init; }
    public Func<bool> HasSight { get; init; }
    public Func<float> GetMoveSpeed { get; init; }
    public Func<float> GetAttackRange { get; init; }
    public Func<float> GetForgetRange { get; init; }
}

public partial class EntitySequence : Node
{
    #region Public State
    public EntityBody Actor => _binding?.Actor;
    public EnemyMotor Motor => Actor?.Motor;
    public WorldNavigation Navigation => Actor?.Navigation;
    public bool IsRunning { get; private set; }

    public bool IsConfigured =>
        GodotObject.IsInstanceValid(_binding?.GetDefinition?.Invoke());

    public Node2D Target => IsRunning
        ? _target : _binding?.GetTarget?.Invoke();

    public float MoveSpeed => _binding?.GetMoveSpeed?.Invoke() ?? 0f;
    public float AttackRange => _binding?.GetAttackRange?.Invoke() ?? 0f;
    #endregion

    #region Private State
    private EntitySequenceBinding _binding;
    private EntitySequenceDefinition _definition;
    private EntityCombat _combat;
    private AttackDefinition _defaultAttack;
    private Node2D _target;
    private EntityActionState _state;
    private readonly RandomNumberGenerator _rng = new();

    private int _index;
    private double _cooldown;
    private bool _entered;

    private bool Active =>
        _binding != null &&
        GodotObject.IsInstanceValid(Actor) &&
        Actor.IsInsideTree() && !Actor.IsQueuedForDeletion() &&
        GodotObject.IsInstanceValid(Actor.Health) &&
        Actor.Health.IsAlive &&
        (_binding.IsActive?.Invoke() ?? true);

    private bool HasSight => _binding?.HasSight?.Invoke() ?? true;
    #endregion

    #region Lifecycle
    // =========================================================
    // The owning actor drives execution; this node adds no update callbacks.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Bind actor capabilities and validate the initial optional sequence.
    public void Bind(EntitySequenceBinding binding)
    {
        if (binding == null ||
            !GodotObject.IsInstanceValid(binding.Actor) ||
            binding.GetTarget == null ||
            binding.GetDefinition == null)
            throw new InvalidOperationException(
                "EntitySequence requires an actor, target provider and definition provider.");

        Cancel();
        _binding = binding;
        _cooldown = 0.0;
        _combat = new EntityCombat(binding.Actor);

        ulong seed = binding.RandomSeed != 0
            ? binding.RandomSeed : binding.Actor.GetInstanceId();
        _rng.Seed = seed ^ 0x9E3779B185EBCA87UL;

        binding.GetDefinition()?.Validate();
    }

    // =========================================================
    // Restore temporary weapon state and release owned random resources.
    public override void _ExitTree()
    {
        Cancel();
        _rng.Dispose();
        _binding = null;
        _combat = null;
    }
    #endregion

    #region Starting And Cancellation
    // =========================================================
    // Begin only when the owner and selected target are eligible.
    public bool TryStart()
    {
        if (IsRunning || _cooldown > 0.0 ||
            !Active || !IsConfigured || !HasSight)
            return false;

        Node2D target = _binding.GetTarget();
        if (!_combat.CanAttack(target, AttackRange)) return false;

        _definition = _binding.GetDefinition();
        _defaultAttack = GodotObject.IsInstanceValid(_binding.Weapon)
            ? _binding.Weapon.Attack : null;

        _target = target;
        _index = 0;
        _entered = false;
        _state = default;
        Motor.Stop();
        IsRunning = true;
        return true;
    }

    // =========================================================
    // Abort the running sequence and return movement control to the owner.
    public void Cancel()
    {
        if (IsRunning) Finish();
    }

    // =========================================================
    // Restore the original attack and apply the sequence's repeat cooldown.
    private void Finish()
    {
        if (GodotObject.IsInstanceValid(_binding?.Weapon))
            _binding.Weapon.Attack = _defaultAttack;

        if (GodotObject.IsInstanceValid(Actor) &&
            GodotObject.IsInstanceValid(Motor))
            Motor.Stop();

        _cooldown = Math.Max(0.1, _definition.Cooldown);
        IsRunning = false;
        _target = null;
        _definition = null;
        _defaultAttack = null;
        _state = default;
        _entered = false;
    }
    #endregion

    #region Execution
    // =========================================================
    // Execute bounded transitions without applying elapsed time twice.
    public void Tick(double delta)
    {
        _cooldown = Math.Max(0.0, _cooldown - delta);
        _combat?.Tick(delta);
        if (!IsRunning) return;

        float forgetRange = _binding.GetForgetRange?.Invoke()
            ?? AttackRange;

        if (!Active || _binding.GetTarget() != _target ||
            !_combat.CanAttack(_target, forgetRange))
        {
            Cancel();
            return;
        }

        double stepDelta = delta;

        for (int transition = 0; transition < 16 && IsRunning; transition++)
        {
            if (_index >= _definition.Actions.Count)
            {
                Finish();
                return;
            }

            EntityAction action = _definition.Actions[_index];
            EntityActionResult result = EntityActionResult.Running;

            if (!_entered)
            {
                _state = default;
                _entered = true;
                result = action.Begin(this, ref _state);
            }

            if (result == EntityActionResult.Running)
            {
                _state.Elapsed += stepDelta;
                result = action.Tick(this, ref _state, stepDelta);
                stepDelta = 0.0;
            }

            if (result == EntityActionResult.Running) return;

            if (result == EntityActionResult.Failed &&
                action.OnFailure == EntityActionFailure.AbortSequence)
            {
                Finish();
                return;
            }

            Motor.Stop();
            _index++;
            _entered = false;
        }
    }

    // =========================================================
    // Fire through the optional weapon with final target and cover checks.
    public bool TryFire(AttackDefinition attack)
    {
        if (!IsRunning || !Active || !HasSight ||
            !GodotObject.IsInstanceValid(_binding.Weapon) ||
            !_combat.CanAttack(_target, AttackRange))
            return false;

        _binding.Weapon.Attack = attack ?? _defaultAttack;
        return _binding.Weapon.TryFireAtActor(_target);
    }

    // =========================================================
    // Expose shared melee delivery for future bite, strike or charge actions.
    public bool TryMelee(
        int damage, DamageType type, double cooldown)
    {
        return IsRunning && Active && HasSight &&
            _combat.TryMelee(
                _target, AttackRange, damage, type, cooldown);
    }

    // =========================================================
    // Choose a reproducible initial sideways movement direction.
    public float RandomSide()
    {
        return _rng.RandiRange(0, 1) == 0 ? -1f : 1f;
    }
    #endregion
}