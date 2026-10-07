// Resolves cursor and actor aiming, muzzle positions and attack cooldowns.
// Projectile actor targeting uses visible combat hitboxes.
using Godot;

public partial class Weapon : Node
{
	#region Configuration
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public CombatTeam Team { get; set; } = CombatTeam.Player;
	#endregion

	#region State
	public CharacterBody2D Source { get; private set; }
	private Health _health;
	private TerrainElevation _elevation;
	private ProjectilePool _pool;
	private double _cooldown;
	public event System.Action<AttackDefinition> AttackFired;
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve this actor's components without a world-specific scene path.
    public override void _Ready()
    {
        Source = GetParent().GetParent<CharacterBody2D>();
        _health = GetNode<Health>("../Health");
    }

    // =========================================================
    // Advance cooldown once per actor physics update.
    public void Tick(double delta)
    {
        _cooldown = System.Math.Max(0.0, _cooldown - delta);
    }
    #endregion

    #region Aiming
    // =========================================================
    // Aim visible projectiles at the cursor and preserve terrain-aware tool aiming.
    public bool TryFireAtCursor()
    {
        if (Attack == null || !_health.IsAlive || _cooldown > 0.0)
            return false;

        ResolveElevation();
        Vector2 cursor = Source.GetGlobalMousePosition();

        if (Attack is ProjectileAttack)
            return TryFireAt(
                cursor + Vector2.Down *
                (SourceHeight() + Attack.VisualHeight));

        Vector2 target = cursor + Vector2.Down * Attack.VisualHeight;
        if (_elevation != null)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 next = cursor + Vector2.Down *
                    (_elevation.SampleWorldHeight(target) +
                    Attack.VisualHeight);

                if (next.DistanceSquaredTo(target) < 0.0001f)
                {
                    target = next;
                    break;
                }
                target = next;
            }
        }

        return TryFireAt(target);
    }

    // =========================================================
	// Translate a visible body aiming point onto this projectile's launch plane.
	public bool TryFireAtActor(Node2D target)
	{
		if (Attack == null || target == null ||
			!_health.IsAlive || _cooldown > 0.0)
			return false;

		if (Attack is not ProjectileAttack)
			return TryFireAt(target.GlobalPosition);

		CombatHitbox hitbox = CombatHitbox.Find(target);
		if (hitbox == null) return TryFireAt(target.GlobalPosition);

		hitbox.Refresh();
		if (!hitbox.CanReceiveProjectile) return false;

		Vector2 logicalTarget = hitbox.AimWorldPosition +
			Vector2.Down * (SourceHeight() + Attack.VisualHeight);

		return TryFireAt(logicalTarget);
	}

	// =========================================================
	// Deliver a configured attack and notify presentation.
	public bool TryFireAt(Vector2 groundTarget)
	{
		if (Attack == null || !_health.IsAlive || _cooldown > 0.0)
			return false;

		Vector2 difference = groundTarget - Source.GlobalPosition;
		if (difference.LengthSquared() < 0.0001f) return false;

		AttackDefinition attack = Attack;
		Vector2 direction = difference.Normalized();
		float angle = direction.Angle();
		_cooldown = System.Math.Max(0.03, attack.Cooldown);

		if (attack.MuzzleOffsets == null || attack.MuzzleOffsets.Count == 0)
			attack.Deliver(this, Source.GlobalPosition, direction);
		else
		{
			foreach (Vector2 muzzle in attack.MuzzleOffsets)
				attack.Deliver(
					this, Source.GlobalPosition + muzzle.Rotated(angle),
					direction);
		}

		AttackFired?.Invoke(attack);
		return true;
	}
	#endregion

	#region Projectiles
	// =========================================================
	// Target opposing combat areas independently from movement-body layers.
	public void EmitProjectile(
		Vector2 origin, Vector2 direction, ProjectileAttack attack)
	{
		_pool ??= GetTree().GetFirstNodeInGroup(
			"projectile_pool") as ProjectilePool;

		if (_pool == null)
		{
			GD.PushError("Weapon requires ProjectilePool.");
			return;
		}

		uint mask = Team == CombatTeam.Player
			? CombatHitbox.EnemyLayer
			: CombatHitbox.PlayerLayer;

		_pool.Fire(origin, direction, attack, mask, SourceHeight());
	}

	// =========================================================
	// Resolve terrain elevation lazily for actors created before world startup.
	private void ResolveElevation()
	{
		_elevation ??= GetTree().GetFirstNodeInGroup(
			"terrain_elevation") as TerrainElevation;
	}

	// =========================================================
	// Read the shooter's current launch elevation.
    private float SourceHeight()
    {
        ResolveElevation();
        return _elevation?.SampleWorldHeight(Source.GlobalPosition) ?? 0f;
    }
    #endregion
}
