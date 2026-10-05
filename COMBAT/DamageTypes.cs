// Identifies damage channels independently from weapons and actor defenses.
// Keep Neutral first so older attacks without a type retain normal damage.
public enum DamageType
{
    Neutral,
    Kinetic,
    Energy,
    Explosive,
    Electric,
    Thermal,
    Corrosive
}

public enum VitalityKind
{
    Health,
    Hull
}