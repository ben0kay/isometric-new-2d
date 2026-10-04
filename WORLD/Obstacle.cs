// Creates rock/crate collision independently from terrain-adjusted artwork.
// Static visuals sample their height once and reuse the shared baked atlas.
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
	// Create the collision footprint and attach terrain-adjusted artwork.
	public override async void _Ready()
	{
		AddChild(new CollisionShape2D
		{
			Name = "Footprint",
			Shape = new RectangleShape2D { Size = Footprint }
		});

		try
		{
			await PlaceholderAtlas.EnsureReady(this);
			if (!IsInsideTree()) return;

			Rect2 region = Kind == ObstacleKind.Rock
				? PlaceholderAtlas.RockRegion
				: PlaceholderAtlas.CrateRegion;

			TerrainVisual.Attach(
				this, region, new Vector2(-80, -120),
				new Vector2(Footprint.X / 96f, Height / 72f), false
			);
		}
		catch (System.Exception error)
		{
			GD.PushError($"Obstacle artwork failed: {error}");
		}
	}
	#endregion
}
