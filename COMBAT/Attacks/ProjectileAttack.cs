// Defines projectile damage, movement, appearance, and reusable shot patterns.
// Resources contain settings only; they never store an actor's cooldown.
using Godot;

[GlobalClass]
public partial class ProjectileAttack : AttackDefinition
{
    #region Configuration
    public enum ShotPattern { Cone, Ring }

    [Export] public int Damage { get; set; } = 15;
    [Export] public float Speed { get; set; } = 700f;
    [Export] public float Lifetime { get; set; } = 1.5f;
    [Export] public Color Tint { get; set; } = new("#77e5ee");
    [Export] public ShotPattern Pattern { get; set; }
    [Export] public int ProjectileCount { get; set; } = 1;
    [Export] public float SpreadDegrees { get; set; } = 30f;
    #endregion

    #region Delivery
    // =========================================================
    // Emit an evenly spaced volley from this muzzle.
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