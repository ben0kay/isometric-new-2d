// Defines shovel digging independently from mining lasers and projectile attacks.
// The player component resolves ground targeting; the weapon owns its cooldown.
using Godot;

[Tool, GlobalClass]
public partial class DiggingAttack : AttackDefinition
{
    #region Configuration
    [ExportGroup("Digging")]
    [Export] public float Reach { get; set; } = 80f;
    [Export] public int ShovelStrength { get; set; } = 1;
    [Export] public float DiggingPower { get; set; } = 2f;
    #endregion

    #region Delivery
    // =========================================================
    // Dispatch one shovel action through the owning player's digging component.
    public override void Deliver(Weapon weapon, Vector2 origin, Vector2 direction)
    {
        weapon.Source.GetNodeOrNull<PlayerDigging>(
            "Systems/Digging")?.Dig(this);
    }
    #endregion
}