// Controls arrow-key movement while a separate visual follows terrain height.
// The body retains logical ground coordinates for collision and camera movement.
using Godot;

public partial class Player : CharacterBody2D
{
	#region Configuration
	[Export] public float MoveSpeed { get; set; } = 240f;
	#endregion

	#region State
	private TerrainVisual _visual;
	private float _facing = 1f;
	#endregion

	#region Lifecycle
	// =========================================================
	// Prepare flat-world movement and attach the shared baked player artwork.
	public override async void _Ready()
	{
		MotionMode = MotionModeEnum.Floating;
		SetPhysicsProcess(false);

		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (!IsInsideTree()) return;

			_visual = TerrainVisual.Attach(
				this, PlaceholderAtlas.PlayerRegion,
				new Vector2(-32, -72), Vector2.One, true
			);
			SetPhysicsProcess(true);
		}
		catch (System.Exception error)
		{
			GD.PushError($"Player artwork failed: {error}");
		}
	}

	// =========================================================
	// Move using arrow keys and mirror artwork when horizontal facing changes.
	public override void _PhysicsProcess(double delta)
	{
		Vector2 direction = new(
			(Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f),
			(Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f) -
			(Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f)
		);
		Velocity = direction.Normalized() * MoveSpeed;
		MoveAndSlide();

		if (direction.X == 0f) return;
		float nextFacing = direction.X > 0f ? 1f : -1f;
		if (nextFacing == _facing) return;

		_facing = nextFacing;
		_visual.Scale = new Vector2(_facing, 1f);
	}
	#endregion
}
