// Controls the placeholder player: arrow movement, collision, and facing.
// The node origin represents the player's feet for world Y-sorting.
using Godot;

public partial class Player : CharacterBody2D
{
	#region Configuration
	[Export] public float MoveSpeed { get; set; } = 240f;
	private float _facing = 1f;
	#endregion

	#region Lifecycle
	// =========================================================
	// Configure movement for a flat world without gravity.
	public override void _Ready()
	{
		MotionMode = MotionModeEnum.Floating;
	}

	// =========================================================
	// Move using arrow keys and redraw only when facing changes.
	public override void _PhysicsProcess(double delta)
	{
		Vector2 direction = new(
			(Input.IsPhysicalKeyPressed(Key.Right) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.Left) ? 1f : 0f),
			(Input.IsPhysicalKeyPressed(Key.Down) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(Key.Up) ? 1f : 0f)
		);
		Velocity = direction.Normalized() * MoveSpeed;
		MoveAndSlide();

		if (direction.X == 0f) return;
		float nextFacing = direction.X > 0f ? 1f : -1f;
		if (nextFacing == _facing) return;
		_facing = nextFacing;
		QueueRedraw();
	}
	#endregion

	#region Drawing
	// =========================================================
	// Draw a compact sci-fi character above its ground position.
	public override void _Draw()
	{
		DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.45f));
		DrawCircle(Vector2.Zero, 19f, new Color(0f, 0f, 0f, 0.35f));
		DrawSetTransform(Vector2.Zero);

		DrawLine(new Vector2(-7, -15), new Vector2(-7, -3), new Color("#24313e"), 7f);
		DrawLine(new Vector2(7, -15), new Vector2(7, -3), new Color("#24313e"), 7f);
		DrawRect(new Rect2(-13, -39, 26, 27), new Color("#465d70"));
		DrawRect(new Rect2(-13, -39, 26, 27), new Color("#8398a6"), false, 2f);
		DrawCircle(new Vector2(0, -46), 12f, new Color("#708697"));
		DrawRect(new Rect2(-8 + _facing * 4, -50, 12, 7), new Color("#77e5ee"));
		DrawLine(new Vector2(_facing * 13, -31), new Vector2(_facing * 23, -24), new Color("#354b5c"), 6f);
		DrawRect(new Rect2(-5, -34, 10, 4), new Color("#77e5ee"));
	}
	#endregion
}
