// Connects enemy gameplay events to replaceable visual behaviour.
// The default presentation flashes baked artwork; subclasses can play animations.
using Godot;

public partial class EntityPresentation : Node
{
    #region Configuration
    [ExportGroup("Damage Feedback")]
    [Export] public Color HitTint { get; set; } = new(2f, 2f, 2f, 1f);
    [Export] public double HitFlashSeconds { get; set; } = 0.12;
    #endregion

    #region State
    protected Entity Actor { get; private set; }
    protected CanvasItem Artwork { get; private set; }
    private Health _health;
    private Weapon _weapon;
    private EntityCombatController _combat;
    private Color _normalTint;
    private Tween _flash;
    private bool _bound, _dead;
    #endregion

    #region Lifecycle
    // =========================================================
    // Presentation reacts to events and requires no frame update of its own.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Bind shared health and any optional weapon or combat presentation events.
    public void Bind(Entity actor, CanvasItem artwork)
    {
        Unbind();

        Actor = actor;
        Artwork = artwork;
        _normalTint = artwork.Modulate;
        _dead = false;

        _health = actor.Health;
        _weapon = actor.GetNodeOrNull<Weapon>("Systems/Weapon");
        _combat = actor.GetNodeOrNull<EntityCombatController>("Systems/Combat");

        _health.Hit += OnDamageReceived;
        _health.Died += HandleDeath;

        if (GodotObject.IsInstanceValid(_weapon))
            _weapon.AttackFired += OnAttackFired;

        if (GodotObject.IsInstanceValid(_combat))
            _combat.MeleeExecuted += OnMeleeExecuted;

        _bound = true;
    }

    // =========================================================
    // Disconnect when streamed out or removed from the world.
    public override void _ExitTree()
    {
        Unbind();
    }

    // =========================================================
    // Remove subscriptions and cancel temporary presentation work.
    private void Unbind()
    {
        _flash?.Kill();
        _flash = null;
        if (!_bound) return;

        if (GodotObject.IsInstanceValid(_health))
        {
            _health.Hit -= OnDamageReceived;
            _health.Died -= HandleDeath;
        }
        if (GodotObject.IsInstanceValid(_weapon))
            _weapon.AttackFired -= OnAttackFired;
        if (GodotObject.IsInstanceValid(_combat))
            _combat.MeleeExecuted -= OnMeleeExecuted;

        _bound = false;
    }
    #endregion

    #region Presentation Hooks
    // =========================================================
    // Flash accepted hits; replace this method with a hurt animation later.
    protected virtual void OnDamageReceived()
    {
        if (_dead || !GodotObject.IsInstanceValid(Artwork)) return;
        _flash?.Kill();
        Artwork.Modulate = _normalTint * HitTint;
        _flash = CreateTween();
        _flash.TweenProperty(Artwork, "modulate", _normalTint,
            System.Math.Max(0.01, HitFlashSeconds));
    }

    // =========================================================
    // Hook for recoil, muzzle effects, sound, or a ranged firing animation.
    protected virtual void OnAttackFired(AttackDefinition attack)
    {
    }

    // =========================================================
    // Hook for a melee strike animation or impact effect.
    protected virtual void OnMeleeExecuted()
    {
    }

    // =========================================================
    // Hook for death presentation; gameplay still owns removal and population.
    protected virtual void OnDeath()
    {
    }

    // =========================================================
    // Stop hit feedback before notifying the selected death presentation.
    private void HandleDeath()
    {
        if (_dead) return;
        _dead = true;
        _flash?.Kill();
        _flash = null;
        if (GodotObject.IsInstanceValid(Artwork))
            Artwork.Modulate = _normalTint;
        OnDeath();
    }
    #endregion
}