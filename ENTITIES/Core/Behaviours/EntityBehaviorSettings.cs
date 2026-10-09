// Supplies species-independent settings to shared entity behaviours.
// These plain structs are runtime settings, not additional resource files.
using Godot;

public enum EntityCombatPositioning { None, Chase, KeepDistance }

public struct EntityWanderSettings
{
    public bool Enabled;
    public float Speed;
    public float Radius;
    public float HomeLeash;
    public Vector2 Wait;
    public float ArrivalDistance;
    public float ReturnDistance;
    public bool RequireDirectPath;
    public bool TickMotor;
}

public struct EntityCombatMovementSettings
{
    public EntityCombatPositioning Positioning;
    public float Speed;
    public float StopDistance;
    public float PreferredRange;
    public float AttackRange;
    public float BackAwayRange;
    public float RangeBias;
}