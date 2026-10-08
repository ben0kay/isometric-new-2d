// Executes optional action sequences with exclusive ownership of enemy movement.
// All timers and progress are local to this actor; shared resources remain unchanged.
using Godot;
using System;

public partial class EnemySequence : Node
{
    #region Public State
    public Enemy Actor { get; private set; }
    public EnemyMotor Motor { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsConfigured => Actor.Definition.Sequence != null;

  // Navigation follows this actor when it crosses between layers.
public WorldNavigation Navigation => WorldNavigation.For(Actor);
    #endregion

    #region Private State
    private Weapon _weapon;
    private WorldNavigation _navigation;
    private readonly RandomNumberGenerator _rng = new();
    private EnemySequenceDefinition _definition;
    private AttackDefinition _defaultAttack;
    private Player _target;
    private EnemyActionState _state;
    private int _index;
    private double _cooldown;
    private bool _entered;
    #endregion

    #region Lifecycle
    // =========================================================
    // Cache sibling components and initialize this enemy's own random stream.
    public override void _Ready()
    {
        Actor = GetParent().GetParent<Enemy>();
        Motor = GetNode<EnemyMotor>("../Motor");
        _weapon = GetNode<Weapon>("../Weapon");
        _rng.Seed = (Actor.RandomSeed != 0 ? Actor.RandomSeed : Actor.GetInstanceId())
            ^ 0x9E3779B185EBCA87UL;
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Dispose the instance's random generator when it leaves the world.
    public override void _ExitTree()
    {
        _rng.Dispose();
    }
    #endregion

    #region Starting And Cancellation
    // =========================================================
    // Begin the optional sequence only for an active enemy with an eligible target.
    public bool TryStart()
    {
        if (IsRunning || !IsConfigured || _cooldown > 0.0 ||
            !Actor.Initialized || !Actor.IsActivated || Actor.SpawnPending ||
            Actor.IsQueuedForDeletion() || Actor.Health?.IsAlive != true ||
            !Actor.HasTarget || !Actor.HasSight)
            return false;

        float range = Actor.Definition.Combat.AttackRange;
        if (Actor.GlobalPosition.DistanceSquaredTo(Actor.Target.GlobalPosition) >
            range * range) return false;

        _definition = Actor.Definition.Sequence;
        _defaultAttack = _weapon.Attack;
        _target = Actor.Target;
        _index = 0;
        _entered = false;
        _state = default;
        Motor.Stop();
        IsRunning = true;
        return true;
    }

    // =========================================================
    // Cancel future actions and return weapon and movement control to normal behaviour.
    public void Cancel()
    {
        if (!IsRunning) return;
        Finish();
    }

    // =========================================================
    // Finish or abort with the configured repeat cooldown.
    private void Finish()
    {
        _weapon.Attack = _defaultAttack;
        Motor.Stop();
        _cooldown = _definition.Cooldown;
        IsRunning = false;
        _target = null;
        _definition = null;
        _state = default;
        _entered = false;
    }
    #endregion

    #region Execution
    // =========================================================
    // Advance only the active action, with bounded immediate transitions.
    public void Tick(double delta)
    {
        _cooldown = Math.Max(0.0, _cooldown - delta);
        if (!IsRunning) return;

        if (!Actor.IsActivated || Actor.SpawnPending || Actor.IsQueuedForDeletion() ||
            Actor.Health?.IsAlive != true || !Actor.HasTarget || Actor.Target != _target)
        {
            Cancel();
            return;
        }

        float forget = Actor.Definition.ForgetRange;
        if (Actor.GlobalPosition.DistanceSquaredTo(_target.GlobalPosition) >
            forget * forget)
        {
            Cancel();
            return;
        }

        // Only the first running action receives this tick's elapsed time.
        double stepDelta = delta;
        for (int transition = 0; transition < 16 && IsRunning; transition++)
        {
            if (_index >= _definition.Actions.Count) { Finish(); return; }

            EnemyAction action = _definition.Actions[_index];
            EnemyActionResult result = EnemyActionResult.Running;

            if (!_entered)
            {
                _state = default;
                _entered = true;
                result = action.Begin(this, ref _state);
            }

            if (result == EnemyActionResult.Running)
            {
                _state.Elapsed += stepDelta;
                result = action.Tick(this, ref _state, stepDelta);
                stepDelta = 0.0;
            }

            if (result == EnemyActionResult.Running) return;
            if (result == EnemyActionResult.Failed &&
                action.OnFailure == EnemyActionFailure.AbortSequence)
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
// Fire sequence attacks at the target's visible combat silhouette.
public bool TryFire(AttackDefinition attack)
{
    if (!Actor.HasTarget || !Actor.HasSight) return false;

    float range = Actor.Definition.Combat.AttackRange;
    Vector2 point = Actor.Target.GlobalPosition;

    if (Actor.GlobalPosition.DistanceSquaredTo(point) > range * range)
        return false;

    _weapon.Attack = attack ?? _defaultAttack;
    return _weapon.TryFireAtActor(Actor.Target);
}

    // =========================================================
    // Choose a reproducible random initial dodge side.
    public float RandomSide()
    {
        return _rng.RandiRange(0, 1) == 0 ? -1f : 1f;
    }
    #endregion
}