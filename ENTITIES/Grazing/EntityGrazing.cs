// Owns grass reservations, eating progress and feeding cooldown.
// The existing GrazingWorld remains the shared grass index and depletion service.
using Godot;

public partial class EntityGrazing : Node
{
    #region State
    private Entity _actor;
    private GrazingWorld _world;
    private Grass _target;
    private bool _eating;
    private double _elapsed, _cooldown;

    public bool HasTarget => GodotObject.IsInstanceValid(_target) &&
        !_target.IsQueuedForDeletion();
    public Vector2 TargetPosition => _target.GlobalPosition;
    #endregion

    #region Lifecycle
    // =========================================================
    // Bind grazing once without adding another processing loop.
    public void Bind(Entity actor)
    {
        _actor = actor;
        _world = GrazingWorld.GetOrCreate(actor);
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Relinquish any grass reservation when the component exits.
    public override void _ExitTree()
    {
        Release();
    }
    #endregion

    #region Feeding
    // =========================================================
    // Advance feeding; return true while eating occupies this movement tick.
    public bool Tick(double delta)
    {
        _cooldown -= delta;

        if (_target != null && !HasTarget)
        {
            Release();
            _actor.Motor.Stop();
        }

        if (!_eating || !HasTarget) return false;

        _elapsed += delta;
        if (_elapsed >= _actor.Definition.GrazingDuration)
        {
            _world.Consume(_actor, _target);
            Release();
            _cooldown = _actor.Definition.FeedingCooldown;
            _actor.Wandering.Pause();
        }

        return true;
    }

    // =========================================================
    // Reserve reachable grass within the current wander area.
    public bool TryReserve(Vector2 centre, float radius)
    {
        if (!_actor.Definition.GrazingEnabled || _cooldown > 0.0)
            return false;

        _target = _world.Reserve(_actor, centre, radius);
        return HasTarget;
    }

    // =========================================================
    // Start the eating timer once movement reaches the reserved tuft.
    public bool BeginEating()
    {
        if (!HasTarget) return false;
        _eating = true;
        _elapsed = 0.0;
        return true;
    }

    // =========================================================
    // Check whether reserved grass still belongs to a changed wander area.
    public bool TargetInside(Vector2 centre, float radius)
    {
        return HasTarget &&
            centre.DistanceSquaredTo(_target.GlobalPosition) <= radius * radius;
    }

    // =========================================================
    // Release reservations and eating state without resetting feeding cooldown.
    public void Release()
    {
        if (GodotObject.IsInstanceValid(_world))
            _world.Release(_actor, _target);

        _target = null;
        _eating = false;
        _elapsed = 0.0;
    }
    #endregion
}