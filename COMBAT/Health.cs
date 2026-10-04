// Stores reusable health and damage immunity independently from actor behaviour.
// Signals let visuals, death handling, and UI respond without owning health logic.
using Godot;

public partial class Health : Node
{
    #region Configuration
    [Export] public int MaxHealth { get; set; } = 100;
    [Export] public float DamageImmunity { get; set; }
    #endregion

    #region Signals
    [Signal] public delegate void ChangedEventHandler(int current, int maximum);
    [Signal] public delegate void HitEventHandler();
    [Signal] public delegate void DiedEventHandler();
    #endregion

    #region State
    public int Current { get; private set; }
    public bool IsAlive => Current > 0;
    private double _immunity;
    #endregion

    #region Lifecycle
    // =========================================================
    // Initialize this instance without modifying another actor's health.
    public override void _Ready()
    {
        MaxHealth = System.Math.Max(1, MaxHealth);
        Current = MaxHealth;
    }

    // =========================================================
    // Count down temporary protection using physics time.
    public override void _PhysicsProcess(double delta)
    {
        _immunity = System.Math.Max(0.0, _immunity - delta);
    }
    #endregion

    #region Health Operations
    // =========================================================
    // Apply accepted damage and emit death once when health reaches zero.
    public bool Damage(int amount)
    {
        if (!IsAlive || amount <= 0 || _immunity > 0.0) return false;
        Current = System.Math.Max(0, Current - amount);
        _immunity = System.Math.Max(0.0, DamageImmunity);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
        EmitSignal(SignalName.Hit);
        if (!IsAlive) EmitSignal(SignalName.Died);
        return true;
    }

    // =========================================================
    // Restore full health and optionally grant protection after respawning.
    public void Restore(float protection = 1f)
    {
        Current = MaxHealth;
        _immunity = System.Math.Max(0.0, protection);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
    }
    #endregion
}