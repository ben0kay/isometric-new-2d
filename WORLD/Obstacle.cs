// Uses shared baked rock/crate artwork with an independent collision footprint.
// The node origin remains the obstacle base for world Y-sorting.
using Godot;

public partial class Obstacle : StaticBody2D
{
	#region Configuration
	public enum ObstacleKind { Rock, Crate }

	[Export] public ObstacleKind Kind { get; set; } = ObstacleKind.Rock;
	[Export] public Vector2 Footprint { get; set; } = new(96, 48);
	[Export] public float Height { get; set; } = 72f;
	#endregion

	#region Lifecycle
	// =========================================================
	// Create collision and ensure the shared atlas is available.
	public override async void _Ready()
	{
		AddChild(new CollisionShape2D
		{
			Name = "Footprint",
			Shape = new RectangleShape2D { Size = Footprint }
		});
		Material = PlaceholderAtlas.BakedMaterial;
		TextureFilter = TextureFilterEnum.Nearest;

		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (IsInsideTree()) QueueRedraw();
		}
		catch (System.Exception error)
		{
			GD.PushError($"Obstacle artwork failed: {error}");
		}
	}
	#endregion

	#region Drawing
	// =========================================================
	// Draw one baked visual with its base aligned to the node origin.
	public override void _Draw()
	{
		if (PlaceholderAtlas.Texture == null) return;
		Vector2 scale = new(Footprint.X / 96f, Height / 72f);
		Rect2 destination = new(
			new Vector2(-80f * scale.X, -120f * scale.Y),
			new Vector2(160f * scale.X, 160f * scale.Y)
		);
		DrawTextureRectRegion(
			PlaceholderAtlas.Texture, destination,
			Kind == ObstacleKind.Rock ? PlaceholderAtlas.RockRegion : PlaceholderAtlas.CrateRegion
		);
	}
	#endregion
}
