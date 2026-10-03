// Controls arrow movement and draws the shared baked player texture.
// The node origin represents the player's feet for collision and Y-sorting.
using Godot;

public partial class Player : CharacterBody2D
{
	#region Configuration
	[Export] public float MoveSpeed { get; set; } = 240f;
	private float _facing = 1f;
	#endregion

	#region Lifecycle
	// =========================================================
	// Configure flat-world movement and wait for the shared artwork bake.
	public override async void _Ready()
	{
		MotionMode = MotionModeEnum.Floating;
		Material = PlaceholderAtlas.BakedMaterial;
		TextureFilter = TextureFilterEnum.Nearest;
		SetPhysicsProcess(false);

		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (!IsInsideTree()) return;
			QueueRedraw();
			SetPhysicsProcess(true);
		}
		catch (System.Exception error)
		{
			GD.PushError($"Player artwork failed: {error}");
		}
	}

	// =========================================================
	// Move using arrow keys and redraw only when horizontal facing changes.
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
	// Draw the baked character and mirror its artwork for left-facing movement.
	public override void _Draw()
	{
		if (PlaceholderAtlas.Texture == null) return;
		DrawSetTransform(Vector2.Zero, 0f, new Vector2(_facing, 1f));
		DrawTextureRectRegion(
			PlaceholderAtlas.Texture,
			new Rect2(-32, -72, 64, 96),
			PlaceholderAtlas.PlayerRegion
		);
		DrawSetTransform(Vector2.Zero);
	}
	#endregion
}
