// Draws branching alien fronds with many small tapered leaflets.
// Shape and palette are selected independently and baked into reusable variants.
using Godot;

public static class FrondDrawing
{
	#region Drawing
	// =========================================================
	// Build an asymmetric crown of curved feather-like fronds.
	public static void Draw(Node2D painter, int variant)
	{
		int shape = variant % VegetationPalette.ShapeCount;
		int palette = variant / VegetationPalette.ShapeCount;
		using RandomNumberGenerator rng = new();
		rng.Seed = (ulong)(17011 + shape * 977);

		for (int stem = 0; stem < 6; stem++)
		{
			float spread = (stem - 2.5f) / 2.5f;
			Vector2 start = new(rng.RandfRange(-5, 5), -2);
			Vector2 tip = new(
				spread * rng.RandfRange(58, 79),
				-rng.RandfRange(115, 175) + Mathf.Abs(spread) * 43f);
			Vector2 control = new(
				tip.X * rng.RandfRange(0.15f, 0.45f),
				tip.Y * rng.RandfRange(0.72f, 0.92f));

			DrawFrond(painter, rng, start, control, tip,
				palette, stem < 2);
		}

		painter.DrawCircle(new Vector2(0, -3), 3f, new Color("#17252b"));
	}

	// =========================================================
	// Evaluate one curved frond's central stem.
	private static Vector2 StemPoint(
		Vector2 start, Vector2 control, Vector2 tip, float t)
	{
		float u = 1f - t;
		return start * u * u + control * 2f * u * t + tip * t * t;
	}

	// =========================================================
	// Place alternating leaflets along a curved dark stem.
	private static void DrawFrond(
		Node2D painter, RandomNumberGenerator rng,
		Vector2 start, Vector2 control, Vector2 tip,
		int palette, bool rear)
	{
		Vector2[] stem = new Vector2[15];
		for (int i = 0; i < stem.Length; i++)
			stem[i] = StemPoint(start, control, tip, i / 14f);

		painter.DrawPolyline(stem, new Color("#1a3037"), 1.7f, true);
		painter.DrawPolyline(stem, new Color("#405b64"), 0.6f, true);

		for (int pair = 0; pair < 7; pair++)
		{
			float t = 0.18f + pair * 0.105f;
			Vector2 tangent = (
				(control - start) * (1f - t) + (tip - control) * t).Normalized();
			Vector2 normal = new(-tangent.Y, tangent.X);
			float length = Mathf.Lerp(29f, 9f, pair / 6f);

			for (int side = -1; side <= 1; side += 2)
			{
				float sample = t + (side > 0 ? 0.023f : 0f);
				Vector2 root = StemPoint(start, control, tip, sample);
				Vector2 end = root
					+ normal * side * length * rng.RandfRange(0.85f, 1.1f)
					+ tangent * length * rng.RandfRange(0.35f, 0.65f);

				Color colour = VegetationPalette.GetLeaf(
					palette, rng.RandfRange(0f, 0.65f),
					pair > 4 && rng.Randf() < 0.25f);
				if (rear) colour = colour.Darkened(0.22f);

				VegetationDrawingHelpers.Leaflet(
					painter, root, end,
					Mathf.Lerp(4.7f, 1.7f, pair / 6f), colour);
			}
		}

		VegetationDrawingHelpers.Leaflet(
			painter, stem[12], tip, 2.7f,
			VegetationPalette.GetLeaf(palette, 0.2f, true));
	}
	#endregion
}
