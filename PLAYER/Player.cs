// Controls arrow movement, terrain-adjusted artwork, and mouse-triggered firing.
// Health, weapon timing, and respawning live in separate components.
using Godot;

public partial class Player : CharacterBody2D
{
	#region Configuration
	[Export] public float MoveSpeed { get; set; } = 240f;
	[Export] public VisualDefinition VisualOverride { get; set; }
	#endregion

	#region State
	private TerrainVisual _visual;
	private Weapon _weapon;
	private Health _health;
	private float _facing = 1f;
	#endregion

	#region Lifecycle
// =========================================================
// Resolve combat components and attach custom or baked player artwork.
public override async void _Ready()
{
	MotionMode = MotionModeEnum.Floating;
	_weapon = GetNode<Weapon>("Systems/Weapon");
	_health = GetNode<Health>("Systems/Health");
	SetPhysicsProcess(false);

	try
	{
		await PlaceholderAtlas.EnsureReady(this);
		if (!IsInsideTree() || IsQueuedForDeletion()) return;

		_visual = TerrainVisual.Attach(
			this, PlaceholderAtlas.PlayerRegion,
			new Vector2(-32, -72), Vector2.One, true, VisualOverride);

		SetPhysicsProcess(true);
	}
	catch (System.Exception error)
	{
		GD.PushError($"Player initialization failed: {error}");
	}
}

	// =========================================================
	// Move, advance weapon timing, and fire while left mouse is held.
	public override void _PhysicsProcess(double delta)
	{
		if (!_health.IsAlive) { Velocity = Vector2.Zero; return; }
		_weapon.Tick(delta);

		Vector2 direction = new(
			(Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f),
			(Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f)
		);
		Velocity = direction.Normalized() * MoveSpeed;
		MoveAndSlide();

		bool firing = Input.IsMouseButtonPressed(MouseButton.Left);
		if (firing) _weapon.TryFireAtCursor();

		float horizontal = firing
			? GetGlobalMousePosition().X - GlobalPosition.X : direction.X;
		if (Mathf.Abs(horizontal) < 0.001f) return;
		float nextFacing = horizontal > 0f ? 1f : -1f;
		if (nextFacing == _facing) return;
		_facing = nextFacing;
		_visual.Scale = new Vector2(_facing, 1f);
	}
	#endregion
}
