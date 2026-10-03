// Draws one static chunk of diamond terrain tiles.
// Uses global tile coordinates so terrain remains consistent when reloaded.
using Godot;

public partial class WorldChunk : Node2D
{
	#region Configuration
	public Vector2I Coordinate { get; set; }
	public int ChunkSize { get; set; } = 16;
	public Vector2 TileSize { get; set; } = new(128, 64);
	public uint Seed { get; set; } = 64;
	public bool ShowBoundary { get; set; }

	private static readonly Color SeamColor = new("#252e37");
	private static readonly Color GrainColor = new(0.32f, 0.38f, 0.43f, 0.16f);
	#endregion

	#region Drawing
	// =========================================================
	// Draw seeded tile variations, faint seams, and surface grain.
	public override void _Draw()
	{
		float w = TileSize.X * 0.5f, h = TileSize.Y * 0.5f;
		Vector2[] diamond = { new(0, -h), new(w, 0), new(0, h), new(-w, 0) };
		Vector2[] edges = { new(-w, 0), new(0, -h), new(w, 0) };

		for (int y = 0; y < ChunkSize; y++)
		for (int x = 0; x < ChunkSize; x++)
		{
			int tileX = Coordinate.X * ChunkSize + x;
			int tileY = Coordinate.Y * ChunkSize + y;
			uint hash = IsoGrid.Hash(tileX, tileY, Seed);
			float shade = 0.085f + (hash % 7u) * 0.003f;

			DrawSetTransform(IsoGrid.TileToWorld(new Vector2(x, y), TileSize));
			DrawColoredPolygon(diamond, new Color(shade, shade + 0.018f, shade + 0.035f));
			DrawPolyline(edges, SeamColor, 1f);

			for (int grain = 0; grain < 6; grain++)
			{
				hash = IsoGrid.Hash(tileX, tileY, hash + (uint)grain + 1u);
				float gx = ((hash & 255u) / 255f - 0.5f) * w;
				float gy = (((hash >> 8) & 255u) / 255f - 0.5f) * h;
				DrawRect(new Rect2(gx, gy, 2, 1), GrainColor);
			}
		}

		DrawSetTransform(Vector2.Zero);
		if (ShowBoundary) DrawBoundary();
	}

	// =========================================================
	// Outline the chunk footprint for optional streaming debugging.
	private void DrawBoundary()
	{
		float low = -0.5f, high = ChunkSize - 0.5f;
		Vector2 a = IsoGrid.TileToWorld(new(low, low), TileSize);
		Vector2 b = IsoGrid.TileToWorld(new(high, low), TileSize);
		Vector2 c = IsoGrid.TileToWorld(new(high, high), TileSize);
		Vector2 d = IsoGrid.TileToWorld(new(low, high), TileSize);
		DrawPolyline(new Vector2[] { a, b, c, d, a }, new Color("#3c8491"), 2f);
	}
	#endregion
}
