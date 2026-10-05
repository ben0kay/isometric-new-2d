// Defines projectile damage, movement, appearance, and reusable shot patterns.
// Shared resources hold settings; weapons hold their own cooldowns.
using Godot;

[Tool,GlobalClass]
public partial class ProjectileAttack : AttackDefinition
{
    public enum ShotPattern { Cone, Ring }

    #region Configuration
    [ExportGroup("Damage")]
    [Export] public int Damage { get; set; } = 15;
    [Export] public DamageType Type { get; set; } = DamageType.Energy;

    [ExportGroup("Projectile")]
    [Export] public float Speed { get; set; } = 700f;
    [Export] public float Lifetime { get; set; } = 1.5f;
    [Export] public Color Tint { get; set; } = new("#77e5ee");

    [ExportGroup("Pattern")]
    [Export] public ShotPattern Pattern { get; set; }
    [Export] public int ProjectileCount { get; set; } = 1;
    [Export] public float SpreadDegrees { get; set; } = 30f;
    #endregion

    #region Delivery
    // =========================================================
    // Emit an evenly spaced volley from the resolved muzzle.
    public override void Deliver(Weapon weapon, Vector2 origin, Vector2 direction)
    {
        int count = System.Math.Clamp(ProjectileCount, 1, 128);
        float spread = Mathf.DegToRad(Mathf.Clamp(SpreadDegrees, 0f, 360f));

        for (int i = 0; i < count; i++)
        {
            float offset = Pattern == ShotPattern.Ring
                ? Mathf.Tau * i / count
                : count == 1 ? 0f : -spread * 0.5f + spread * i / (count - 1);
            weapon.EmitProjectile(origin, direction.Rotated(offset), this);
        }
    }
    #endregion
}