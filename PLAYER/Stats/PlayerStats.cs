// Calculates and caches player attributes from base values and named modifier sources.
// Runtime snapshots prevent shared Resource changes from leaking between players.
using Godot;
using System;
using System.Collections.Generic;

public partial class PlayerStats : Node
{
    #region Configuration
    [ExportGroup("Base Attributes")]
    [Export] public PlayerStatProfile Profile { get; set; }

    [ExportGroup("Testing")]
    [Export] public bool TestModifiersEnabled { get; set; }
    [Export] public Godot.Collections.Array<StatModifier> TestModifiers
        { get; set; } = new();
    #endregion

    #region State
    public event Action Changed;

    private readonly record struct ModifierValue(
        PlayerStat Stat, StatModifierOperation Operation, float Amount);

    private readonly Dictionary<string, ModifierValue[]> _sources = new();
    private readonly float[] _base = new float[(int)PlayerStat.Count];
    private readonly float[] _final = new float[(int)PlayerStat.Count];
    #endregion

    #region Lifecycle
    // =========================================================
    // Copy starting values and optionally install the Inspector's test modifiers.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
        Profile ??= new PlayerStatProfile();

        for (int i = 0; i < _base.Length; i++)
        {
            float value = Profile.Get((PlayerStat)i);
            if (!float.IsFinite(value) || value < 0f)
                throw new InvalidOperationException(
                    $"Invalid base player stat: {(PlayerStat)i}.");
            _base[i] = value;
        }

        if (TestModifiersEnabled) SetModifierSource("test", TestModifiers);
        else Recalculate();
    }
    #endregion

    #region Queries And Base Values
    // =========================================================
    // Return the cached final attribute.
    public float Get(PlayerStat stat)
    {
        return _final[Index(stat)];
    }

    // =========================================================
    // Return this player's unmodified base attribute.
    public float GetBase(PlayerStat stat)
    {
        return _base[Index(stat)];
    }

    // =========================================================
    // Change a per-player base value without editing the shared profile.
    public void SetBase(PlayerStat stat, float value)
    {
        if (!float.IsFinite(value) || value < 0f)
            throw new ArgumentOutOfRangeException(nameof(value));

        int index = Index(stat);
        if (_base[index] == value) return;
        _base[index] = value;
        Recalculate();
    }

    // =========================================================
    // Reject enum values that are not actual attributes.
    private static int Index(PlayerStat stat)
    {
        int index = (int)stat;
        if (index < 0 || index >= (int)PlayerStat.Count)
            throw new ArgumentOutOfRangeException(nameof(stat));
        return index;
    }
    #endregion

    #region Modifier Sources
    // =========================================================
    // Replace one source's modifiers instead of accidentally stacking it repeatedly.
    public void SetModifierSource(
        string source, Godot.Collections.Array<StatModifier> modifiers)
    {
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("A modifier source requires an identifier.");

        if (modifiers == null || modifiers.Count == 0)
        {
            RemoveModifierSource(source);
            return;
        }

        ModifierValue[] snapshot = new ModifierValue[modifiers.Count];
        for (int i = 0; i < modifiers.Count; i++)
        {
            StatModifier modifier = modifiers[i] ??
                throw new ArgumentException("Modifier entries cannot be empty.");

            Index(modifier.Stat);
            if (!Enum.IsDefined(typeof(StatModifierOperation), modifier.Operation) ||
                !float.IsFinite(modifier.Amount) ||
                (modifier.Operation == StatModifierOperation.Multiply &&
                 modifier.Amount < 0f))
                throw new ArgumentException("Invalid modifier operation or amount.");

            snapshot[i] = new ModifierValue(
                modifier.Stat, modifier.Operation, modifier.Amount);
        }

        _sources[source] = snapshot;
        Recalculate();
    }

    // =========================================================
    // Remove equipment, an upgrade, or a buff without affecting other sources.
    public void RemoveModifierSource(string source)
    {
        if (_sources.Remove(source)) Recalculate();
    }
    #endregion

    #region Calculation
    // =========================================================
    // Cache final values using flat, summed percentage, then multiplied bonuses.
    private void Recalculate()
    {
        for (int i = 0; i < _final.Length; i++)
        {
            double flat = 0.0, percent = 0.0, multiplier = 1.0;
            foreach (ModifierValue[] source in _sources.Values)
            foreach (ModifierValue modifier in source)
            {
                if ((int)modifier.Stat != i) continue;
                switch (modifier.Operation)
                {
                    case StatModifierOperation.Flat:
                        flat += modifier.Amount;
                        break;
                    case StatModifierOperation.AddPercent:
                        percent += modifier.Amount;
                        break;
                    case StatModifierOperation.Multiply:
                        multiplier *= modifier.Amount;
                        break;
                }
            }

            double value = (_base[i] + flat) * Math.Max(0.0, 1.0 + percent) * multiplier;
            double minimum = i <= (int)PlayerStat.MaxFatigue ? 1.0 : 0.0;
            _final[i] = (float)Math.Clamp(value, minimum, float.MaxValue);
        }
        Changed?.Invoke();
    }
    #endregion
}