// Prewarms and reuses projectile scene instances instead of freeing every shot.
// Projectile roots share WorldObjects so their logical positions remain Y-sorted.
using Godot;
using System.Collections.Generic;

public partial class ProjectilePool : Node
{
	#region Configuration
	[Export] public int Prewarm { get; set; } = 32;
	[Export] public int Capacity { get; set; } = 1024;
	#endregion

	#region State
	private readonly Stack<Projectile> _available = new();
	private readonly PackedScene _scene =
		GD.Load<PackedScene>("res://COMBAT/Projectiles/Projectile.tscn");
	private Node2D _objects;
	private int _created;
	#endregion

	#region Lifecycle
	// =========================================================
	// Register the pool and prepare a small reserve before combat starts.
	public override void _Ready()
	{
		AddToGroup("projectile_pool");
		_objects = GetNode<Node2D>("../../WorldObjects");
		Capacity = System.Math.Max(1, Capacity);

		for (int i = 0; i < System.Math.Clamp(Prewarm, 0, Capacity); i++)
			_available.Push(CreateProjectile());
	}

	// =========================================================
	// Create one initially inactive projectile under the shared visual root.
	private Projectile CreateProjectile()
	{
		Projectile projectile = _scene.Instantiate<Projectile>();
		projectile.Name = $"Projectile_{_created++}";
		_objects.AddChild(projectile);
		return projectile;
	}
	#endregion

	#region Pool Operations
// =========================================================
// Launch pooled shots with captured elevation, cover height and layer.
public bool Fire(
    Vector2 origin, Vector2 direction, ProjectileAttack attack,
    uint mask, float sourceHeight,
    float coverHeight = CombatCover.DefaultHeight,
    WorldLayer layer = WorldLayer.Surface)
{
    if (_available.Count == 0 && _created >= Capacity) return false;

    Projectile projectile = _available.Count > 0
        ? _available.Pop() : CreateProjectile();

    projectile.Launch(
        this, origin, direction, attack, mask, sourceHeight,
        coverHeight, layer);
    return true;
}

	// =========================================================
	// Return a deactivated projectile to the available reserve.
	public void Recycle(Projectile projectile)
	{
		_available.Push(projectile);
	}
	#endregion
}
