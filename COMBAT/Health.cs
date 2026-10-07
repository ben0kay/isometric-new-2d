// Stores per-actor vitality, immunity timing, and an optional defense profile.
// Existing callers can still use Damage(amount); typed attacks supply a damage channel.
using Godot;
using System;

public partial class Health : Node
{
    #region Configuration
    [ExportGroup("Vitality")]
    [Export] public int MaxHealth { get; set; } = 100;
    [Export] public DefenseDefinition Defense { get; set; }

    [ExportGroup("Protection")]
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
    public string VitalityLabel => Defense?.VitalityLabel ?? "Health";
    private double _immunity;
    #endregion

    #region Lifecycle
    // =========================================================
    // Initialize this actor without changing its shared defense resource.
    public override void _Ready()
    {
        MaxHealth = Math.Max(1, MaxHealth);
        Current = MaxHealth;
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Process temporary protection only while it is active.
    public override void _PhysicsProcess(double delta)
    {
        _immunity = Math.Max(0.0, _immunity - delta);
        if (_immunity <= 0.0) SetPhysicsProcess(false);
    }
    #endregion

    #region Operations
    // =========================================================
    // Resolve defenses, apply accepted damage, and emit death once.
    public bool Damage(int amount, DamageType type = DamageType.Neutral)
    {
        if (!IsAlive || amount <= 0 || _immunity > 0.0) return false;
        int resolved = Defense?.ResolveDamage(amount, type) ?? amount;
        if (resolved <= 0) return false;

        Current = Math.Max(0, Current - resolved);
        _immunity = Math.Max(0.0, DamageImmunity);
        SetPhysicsProcess(_immunity > 0.0);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
        EmitSignal(SignalName.Hit);
        if (!IsAlive) EmitSignal(SignalName.Died);
        return true;
    }

    // =========================================================
    // Restore full vitality with optional respawn protection.
    public void Restore(float protection = 1f)
    {
        Current = MaxHealth;
        _immunity = Math.Max(0.0, protection);
        SetPhysicsProcess(_immunity > 0.0);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
    }

    // =========================================================
    // Restore a living streamed actor without granting fresh immunity.
    public void RestoreState(int current)
    {
        Current = Math.Clamp(current, 1, MaxHealth);
        _immunity = 0.0;
        SetPhysicsProcess(false);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
    }

    // =========================================================
// Change maximum vitality while preserving current health unless explicitly filled.
public void SetMaximum(int maximum, bool fill = false)
{
    maximum = Math.Max(1, maximum);
    if (MaxHealth == maximum && !fill) return;

    MaxHealth = maximum;
    Current = fill ? MaxHealth : Math.Min(Current, MaxHealth);
    EmitSignal(SignalName.Changed, Current, MaxHealth);
}
    #endregion

        // =========================================================
    // Apply ongoing environmental exposure independently from combat hit immunity.
    public bool DamageEnvironment(
        int amount, DamageType type = DamageType.Neutral)
    {
        if (!IsAlive || amount <= 0) return false;

        int resolved = Defense?.ResolveDamage(amount, type) ?? amount;
        if (resolved <= 0) return false;

        Current = Math.Max(0, Current - resolved);
        EmitSignal(SignalName.Changed, Current, MaxHealth);
        if (!IsAlive) EmitSignal(SignalName.Died);
        return true;
    }

    // =========================================================
// Apply internal survival damage independently from armor and combat immunity.
public bool DamageSurvival(int amount)
{
    if (!IsAlive || amount <= 0) return false;

    Current = Math.Max(0, Current - amount);
    EmitSignal(SignalName.Changed, Current, MaxHealth);
    if (!IsAlive) EmitSignal(SignalName.Died);
    return true;
}
}