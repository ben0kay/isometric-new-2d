// Resolves aiming, muzzle positions, and per-instance attack cooldowns.
// Attack resources supply delivery behaviour without owning weapon runtime state.
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
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve this actor's components without assuming a world scene path.
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

	#region Aiming And Firing
	// =========================================================
	// Convert the visible cursor position back into logical ground coordinates.
	public bool TryFireAtCursor()
	{
		if (Attack == null || !_health.IsAlive || _cooldown > 0.0) return false;
		_elevation ??= GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

		Vector2 cursor = Source.GetGlobalMousePosition();
		Vector2 target = cursor + Vector2.Down * Attack.VisualHeight;

		if (_elevation != null)
		{
			for (int i = 0; i < 12; i++)
			{
				Vector2 next = cursor + Vector2.Down *
					(_elevation.SampleWorldHeight(target) + Attack.VisualHeight);
				if (next.DistanceSquaredTo(target) < 0.0001f) { target = next; break; }
				target = next;
			}
		}
		return TryFireAt(target);
	}

	// =========================================================
	// Fire from every configured muzzle toward a logical ground target.
	public bool TryFireAt(Vector2 groundTarget)
	{
		if (Attack == null || !_health.IsAlive || _cooldown > 0.0) return false;
		Vector2 difference = groundTarget - Source.GlobalPosition;
		if (difference.LengthSquared() < 0.0001f) return false;

		Vector2 direction = difference.Normalized();
		float angle = direction.Angle();
		_cooldown = System.Math.Max(0.03, Attack.Cooldown);

		if (Attack.MuzzleOffsets == null || Attack.MuzzleOffsets.Count == 0)
			Attack.Deliver(this, Source.GlobalPosition, direction);
		else
		{
			foreach (Vector2 muzzle in Attack.MuzzleOffsets)
				Attack.Deliver(this, Source.GlobalPosition + muzzle.Rotated(angle), direction);
		}
		return true;
	}

	// =========================================================
	// Route projectile delivery through the world's shared reusable pool.
	public void EmitProjectile(Vector2 origin, Vector2 direction, ProjectileAttack attack)
	{
		_pool ??= GetTree().GetFirstNodeInGroup("projectile_pool") as ProjectilePool;
		if (_pool == null) { GD.PushError("Weapon requires ProjectilePool."); return; }
		uint mask = Team == CombatTeam.Player ? 5u : 3u;
		_pool.Fire(origin, direction, attack, mask);
	}
	#endregion
}
