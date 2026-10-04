// Defines shared attack timing and local muzzle positions.
// Delivery subclasses implement projectiles, and later beams or area attacks.
using Godot;

public enum CombatTeam { Player, Enemy }

public abstract partial class AttackDefinition : Resource
{
    #region Configuration
    [Export] public double Cooldown { get; set; } = 0.18;
    [Export] public float VisualHeight { get; set; } = 28f;
    [Export] public Godot.Collections.Array<Vector2> MuzzleOffsets { get; set; } =
        new() { new Vector2(18, 0) };
    #endregion

    #region Delivery
    // =========================================================
    // Deliver one attack from a resolved ground position and aim direction.
    public abstract void Deliver(Weapon weapon, Vector2 origin, Vector2 direction);
    #endregion
}