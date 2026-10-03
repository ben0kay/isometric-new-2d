// Creates a solid ground footprint and draws a placeholder rock or sci-fi crate.
// Position represents the obstacle's base for world Y-sorting.
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
	// Add a rectangular collision footprint centred on the base.
	public override void _Ready()
	{
		AddChild(new CollisionShape2D
		{
			Name = "Footprint",
			Shape = new RectangleShape2D { Size = Footprint }
		});
	}
	#endregion

	#region Drawing
	// =========================================================
	// Draw the ground shadow and selected obstacle visual.
	public override void _Draw()
	{
		DrawSetTransform(Vector2.Zero, 0f, new Vector2(1f, 0.5f));
		DrawCircle(Vector2.Zero, Footprint.X * 0.65f, new Color(0f, 0f, 0f, 0.3f));
		DrawSetTransform(Vector2.Zero);

		if (Kind == ObstacleKind.Rock) DrawRock();
		else DrawCrate();
	}

	// =========================================================
	// Draw an angular rock with a shaded face and mineral streak.
	private void DrawRock()
	{
		float w = Footprint.X * 0.65f, d = Footprint.Y * 0.5f;
		Vector2[] outline =
		{
			new(-w, -d), new(-w * 0.75f, -Height),
			new(-w * 0.15f, -Height - 16), new(w * 0.65f, -Height + 4),
			new(w, -d), new(w * 0.55f, d), new(-w * 0.55f, d)
		};
		DrawColoredPolygon(outline, new Color("#47505c"));
		DrawColoredPolygon(new Vector2[]
		{
			new(-w, -d), new(-w * 0.75f, -Height),
			new(-w * 0.15f, -Height - 16), new(w * 0.1f, -d * 0.4f),
			new(-w * 0.55f, d)
		}, new Color("#5c6876"));
		DrawPolyline(new Vector2[]
		{
			new(-w * 0.4f, -Height * 0.8f),
			new(-w * 0.1f, -Height * 0.5f),
			new(w * 0.3f, -Height * 0.35f)
		}, new Color("#71b5c4"), 3f, true);
	}

	// =========================================================
	// Draw a raised crate with distinct top and side panels.
	private void DrawCrate()
	{
		float w = Footprint.X * 0.5f, d = Footprint.Y * 0.5f;
		Vector2 a = new(-w, -Height), b = new(0, -Height - d);
		Vector2 c = new(w, -Height), e = new(0, -Height + d);

		DrawColoredPolygon(new Vector2[] { a, e, new(0, d), new(-w, 0) }, new Color("#344756"));
		DrawColoredPolygon(new Vector2[] { e, c, new(w, 0), new(0, d) }, new Color("#263541"));
		DrawColoredPolygon(new Vector2[] { a, b, c, e }, new Color("#61798a"));
		DrawPolyline(new Vector2[] { a, b, c, e, a }, new Color("#8297a5"), 2f, true);
		DrawLine(new Vector2(w * 0.25f, -Height * 0.45f), new Vector2(w * 0.75f, -Height * 0.65f), new Color("#76e2e7"), 3f);
	}
	#endregion
}
