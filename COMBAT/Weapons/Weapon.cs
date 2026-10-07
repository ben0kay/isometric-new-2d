// Resolves weapon aiming, cooldowns and projectile delivery.
// Actor shots recheck cover immediately before firing.
using Godot;

public partial class Weapon : Node
{
	#region Configuration
	[Export] public AttackDefinition Attack { get; set; }
	[Export] public CombatTeam Team { get; set; } = CombatTeam.Player;
	#endregion

	#region State
	public CharacterBody2D Source { get; private set; }
	public event System.Action<AttackDefinition> AttackFired;

	private Health _health;
	private ProjectilePool _pool;
	private double _cooldown;
	private float _shotHeight = -1f;

	private readonly PhysicsRayQueryParameters2D _coverQuery = new();
	private readonly Godot.Collections.Array<Rid> _coverExcluded = new();
	#endregion

	#region Lifecycle
	// =========================================================
	// Resolve the owning actor and its combat health.
	public override void _Ready()
	{
		Source = GetParent().GetParent<CharacterBody2D>();
		_health = GetNode<Health>("../Health");
	}

// =========================================================
// Release the cover query and clear its exclusion list.
public override void _ExitTree()
{
	_coverQuery.Dispose();
	_coverExcluded.Clear();
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
	// Preserve cursor aiming for projectiles and terrain-aware tools.
	public bool TryFireAtCursor()
	{
		if (Attack == null || !_health.IsAlive || _cooldown > 0.0)
			return false;

		Vector2 cursor = Source.GetGlobalMousePosition();

		if (Attack is ProjectileAttack)
			return TryFireAt(
				cursor + Vector2.Down * (SourceHeight() + Attack.VisualHeight));

		Vector2 target = cursor + Vector2.Down * Attack.VisualHeight;

		for (int i = 0; i < 12; i++)
		{
			Vector2 next = cursor + Vector2.Down *
				(WorldLayerController.HeightFor(Source, target) +
					Attack.VisualHeight);

			if (next.DistanceSquaredTo(target) < 0.0001f)
			{
				target = next;
				break;
			}
			target = next;
		}

		return TryFireAt(target);
	}

	// =========================================================
	// Require same-layer cover clearance before firing at an actor.
	public bool TryFireAtActor(Node2D target)
	{
		if (Attack == null ||
			!GodotObject.IsInstanceValid(target) ||
			target.IsQueuedForDeletion() ||
			!_health.IsAlive || _cooldown > 0.0 ||
			!WorldLayerMember.Same(Source, target))
			return false;

		float height = CombatCover.HeightFor(target);
		WorldLayer layer = WorldLayerMember.For(Source);

		// Awareness uses actor ground positions rather than sprite overlap.
		if (CombatCover.FindHit(
			Source.GetWorld2D().DirectSpaceState,
			_coverQuery, _coverExcluded,
			Source.GlobalPosition, target.GlobalPosition,
			height, layer).Count > 0)
			return false;

		Vector2 logicalTarget = target.GlobalPosition;

		if (Attack is ProjectileAttack)
		{
			CombatHitbox hitbox = CombatHitbox.Find(target);

			if (hitbox != null)
			{
				hitbox.Refresh();
				if (!hitbox.CanReceiveProjectile) return false;

				logicalTarget = hitbox.AimWorldPosition +
					Vector2.Down * (SourceHeight() + Attack.VisualHeight);
			}

			// Also check the actual projectile trajectory after visual aiming.
			if (CombatCover.FindHit(
				Source.GetWorld2D().DirectSpaceState,
				_coverQuery, _coverExcluded,
				Source.GlobalPosition, logicalTarget,
				height, layer).Count > 0)
				return false;
		}

		float previousHeight = _shotHeight;
		_shotHeight = height;

		try
		{
			return TryFireAt(logicalTarget);
		}
		finally
		{
			_shotHeight = previousHeight;
		}
	}

	// =========================================================
	// Deliver the selected attack and notify presentation.
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
			foreach (Vector2 muzzle in attack.MuzzleOffsets)
				attack.Deliver(
					this,
					Source.GlobalPosition + muzzle.Rotated(angle),
					direction);

		AttackFired?.Invoke(attack);
		return true;
	}
	#endregion

	#region Projectiles
	// =========================================================
	// Capture the shot's cover height and world layer for pooled projectiles.
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
            ? CombatHitbox.EnemyLayer : CombatHitbox.PlayerLayer;

        float height = _shotHeight > 0f
            ? _shotHeight : CombatCover.HeightFor(Source);

        _pool.Fire(
            origin, direction, attack, mask, SourceHeight(),
            height, WorldLayerMember.For(Source));
    }

// =========================================================
// Include airborne height when aiming and launching projectiles.
private float SourceHeight()
{
    float terrainHeight = WorldLayerController.HeightFor(
        Source, Source.GlobalPosition);
    float jumpHeight = Source is Player player
        ? player.JumpHeight : 0f;

    return terrainHeight + jumpHeight;
}
	#endregion
}
