// Adapts existing robot scenes to the shared EntitySequence runner.
// Sequence execution itself contains no Enemy-specific behaviour.
using Godot;

public partial class EnemySequence : EntitySequence
{
    #region Lifecycle
    // =========================================================
    // Supply existing robot settings without duplicating sequence execution.
    public override void _Ready()
    {
        base._Ready();

        Enemy actor = GetParent().GetParent<Enemy>();

        Bind(new EntitySequenceBinding
        {
            Actor = actor,
            Weapon = GetNode<Weapon>("../Weapon"),
            RandomSeed = actor.RandomSeed,
            GetDefinition = () => actor.Definition.Sequence,
            GetTarget = () => actor.Target,
            IsActive = () =>
                actor.Initialized && actor.IsActivated &&
                !actor.SpawnPending,
            HasSight = () => actor.HasSight,
            GetMoveSpeed = () => actor.Definition.MoveSpeed,
            GetAttackRange = () => actor.Definition.Combat.AttackRange,
            GetForgetRange = () => actor.Definition.ForgetRange
        });
    }
    #endregion
}