// Draws static dark asphalt-like terrain with grain, angled seams, and perimeter lights.
using Godot;

public partial class Terrain : Node2D
{
	#region Configuration
	[Export] public Vector2 WorldSize { get; set; } = new(2048, 1536);
	[Export] public uint TextureSeed { get; set; } = 64;
	[Export] public int GrainCount { get; set; } = 3200;
	#endregion

	#region Drawing
	// =========================================================
	// Draw the ground, seeded surface grain, seams, and perimeter.
	public override void _Draw()
	{
		Rect2 bounds = new(-WorldSize * 0.5f, WorldSize);
		using RandomNumberGenerator rng = new();
		rng.Seed = TextureSeed;
		DrawRect(bounds, new Color("#171d24"));

		for (int i = 0; i < GrainCount; i++)
		{
			Vector2 point = new(
				rng.RandfRange(bounds.Position.X, bounds.End.X),
				rng.RandfRange(bounds.Position.Y, bounds.End.Y)
			);
			float shade = rng.RandfRange(0.10f, 0.17f);
			DrawRect(new Rect2(point, new Vector2(2, 1)), new Color(shade, shade + 0.015f, shade + 0.025f));
		}

		// Clip the diagonal seams to the ground rectangle.
		float span = WorldSize.X + WorldSize.Y * 2f;
		for (float x = -span; x <= span; x += 256f)
		{
			DrawSeam(bounds, x, 2f);
			DrawSeam(bounds, x, -2f);
		}

		DrawRect(bounds.Grow(-16f), new Color("#38535e"), false, 2f);
		for (float x = bounds.Position.X + 128f; x < bounds.End.X - 64f; x += 256f)
		{
			DrawLine(new Vector2(x, bounds.Position.Y + 16), new Vector2(x + 48, bounds.Position.Y + 16), new Color("#63c4d1"), 3f);
			DrawLine(new Vector2(x, bounds.End.Y - 16), new Vector2(x + 48, bounds.End.Y - 16), new Color("#63c4d1"), 3f);
		}
	}

	// =========================================================
	// Draw one angled seam clipped within the terrain edges.
	private void DrawSeam(Rect2 bounds, float startX, float slope)
	{
		float yMin = bounds.Position.Y;
		float yMax = bounds.End.Y;
		float atLeft = yMin + (bounds.Position.X - startX) / slope;
		float atRight = yMin + (bounds.End.X - startX) / slope;
		float fromY = Mathf.Max(yMin, Mathf.Min(atLeft, atRight));
		float toY = Mathf.Min(yMax, Mathf.Max(atLeft, atRight));
		if (fromY >= toY) return;

		DrawLine(
			new Vector2(startX + slope * (fromY - yMin), fromY),
			new Vector2(startX + slope * (toY - yMin), toY),
			new Color("#242e38"), 1f
		);
	}
	#endregion
}
