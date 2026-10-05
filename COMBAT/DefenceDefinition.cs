// Stores vitality terminology and damage responses for a reusable defense profile.
// Multipliers: zero = immune, one = normal, above one = vulnerable.
using Godot;
using System;

[Tool, GlobalClass]
public partial class DefenseDefinition : Resource
{
    #region Configuration
    [ExportGroup("Vitality")]
    [Export] public VitalityKind Kind { get; set; } = VitalityKind.Health;

    [ExportGroup("Damage Responses")]
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Neutral { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Kinetic { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Energy { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Explosive { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Electric { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Thermal { get; set; } = 1f;
    [Export(PropertyHint.Range, "0,4,0.05,or_greater")]
    public float Corrosive { get; set; } = 1f;
    #endregion

    #region Queries
    public string VitalityLabel => Kind == VitalityKind.Hull ? "Hull" : "Health";

    // =========================================================
    // Resolve one damage channel through this profile.
    public int ResolveDamage(int amount, DamageType type)
    {
        float multiplier = type switch
        {
            DamageType.Kinetic => Kinetic,
            DamageType.Energy => Energy,
            DamageType.Explosive => Explosive,
            DamageType.Electric => Electric,
            DamageType.Thermal => Thermal,
            DamageType.Corrosive => Corrosive,
            _ => Neutral
        };

        if (!float.IsFinite(multiplier)) multiplier = 1f;
        if (amount <= 0 || multiplier <= 0f) return 0;
        return (int)Math.Min(int.MaxValue, Math.Ceiling(amount * (double)multiplier));
    }
    #endregion
}