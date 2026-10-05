// Stores current suit and survival reserves separately from calculated capacities.
// Bridges maximum health to existing combat and resets reserves on respawn.
using Godot;
using System;

public enum PlayerReserve { Stamina, Oxygen, Energy, Food, Water, Fatigue, Count }

public partial class PlayerVitals : Node
{
    #region State
    public event Action Changed;
    public Health Health { get; private set; }

    private PlayerStats _stats;
    private readonly float[] _current = new float[(int)PlayerReserve.Count];
    private bool _wasAlive;
    #endregion

    #region Lifecycle
    // =========================================================
    // Initialize reserves after stats and health have entered the scene.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        _stats = GetNode<PlayerStats>("../Stats");
        Health = GetNode<Health>("../Health");
        _wasAlive = Health.IsAlive;
        _stats.Changed += OnStatsChanged;
        Health.Changed += OnHealthChanged;

        ResetReserves();
        Health.SetMaximum(HealthMaximum(), true);
    }

    // =========================================================
    // Remove subscriptions when this player leaves the world.
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_stats)) _stats.Changed -= OnStatsChanged;
        if (GodotObject.IsInstanceValid(Health)) Health.Changed -= OnHealthChanged;
    }
    #endregion

    #region Reserves
    // =========================================================
    // Read one current reserve.
    public float GetCurrent(PlayerReserve reserve)
    {
        return _current[Index(reserve)];
    }

    // =========================================================
    // Read the calculated capacity for one reserve.
    public float GetMaximum(PlayerReserve reserve)
    {
        Index(reserve);
        return _stats.Get((PlayerStat)((int)reserve + (int)PlayerStat.MaxStamina));
    }

    // =========================================================
    // Add or consume a reserve without changing its capacity.
    public void Change(PlayerReserve reserve, float amount)
    {
        if (!float.IsFinite(amount)) throw new ArgumentOutOfRangeException(nameof(amount));
        int index = Index(reserve);
        float next = Mathf.Clamp(_current[index] + amount, 0f, GetMaximum(reserve));
        if (_current[index] == next) return;
        _current[index] = next;
        Changed?.Invoke();
    }

    // =========================================================
    // Fill suit and nutrition reserves and clear accumulated fatigue.
    public void ResetReserves()
    {
        for (int i = 0; i < _current.Length; i++)
            _current[i] = i == (int)PlayerReserve.Fatigue
                ? 0f : GetMaximum((PlayerReserve)i);
        Changed?.Invoke();
    }

    // =========================================================
    // Reject values that do not identify actual reserves.
    private static int Index(PlayerReserve reserve)
    {
        int index = (int)reserve;
        if (index < 0 || index >= (int)PlayerReserve.Count)
            throw new ArgumentOutOfRangeException(nameof(reserve));
        return index;
    }
    #endregion

    #region Synchronization
    // =========================================================
    // Convert calculated health capacity to the existing integer health system.
    private int HealthMaximum()
    {
        return (int)Math.Min(int.MaxValue,
            Math.Ceiling(_stats.Get(PlayerStat.MaxHealth)));
    }

    // =========================================================
    // Clamp reserves after capacity changes without granting free replenishment.
    private void OnStatsChanged()
    {
        Health.SetMaximum(HealthMaximum());
        for (int i = 0; i < _current.Length; i++)
            _current[i] = Mathf.Min(_current[i], GetMaximum((PlayerReserve)i));
        Changed?.Invoke();
    }

    // =========================================================
    // Refresh health display and refill reserves when a dead player respawns.
    private void OnHealthChanged(int current, int maximum)
    {
        bool alive = current > 0;
        bool revived = !_wasAlive && alive;
        _wasAlive = alive;
        if (revived) ResetReserves();
        else Changed?.Invoke();
    }
    #endregion
}