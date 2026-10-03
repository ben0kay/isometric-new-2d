// Draws a static terrain chunk using the shared baked placeholder atlas.
// Global tile hashes preserve the same variation when a chunk reloads.
using Godot;

public partial class WorldChunk : Node2D
{
	#region Configuration
	public Vector2I Coordinate { get; set; }
	public int ChunkSize { get; set; } = 16;
	public Vector2 TileSize { get; set; } = new(128, 64);
	public uint Seed { get; set; } = 64;
	public bool ShowBoundary { get; set; }
	#endregion

	#region Lifecycle
	// =========================================================
	// Use the shared material and nearest filtering for crisp tile edges.
	public override void _Ready()
	{
		Material = PlaceholderAtlas.BakedMaterial;
		TextureFilter = TextureFilterEnum.Nearest;
	}
	#endregion

	#region Drawing
	// =========================================================
	// Submit one textured rectangle per tile using the same atlas texture.
	public override void _Draw()
	{
		if (PlaceholderAtlas.Texture == null) return;
		Vector2 half = TileSize * 0.5f;

		for (int y = 0; y < ChunkSize; y++)
		for (int x = 0; x < ChunkSize; x++)
		{
			uint hash = IsoGrid.Hash(Coordinate.X * ChunkSize + x, Coordinate.Y * ChunkSize + y, Seed);
			Vector2 centre = IsoGrid.TileToWorld(new Vector2(x, y), TileSize);
			DrawTextureRectRegion(
				PlaceholderAtlas.Texture,
				new Rect2(centre - half, TileSize),
				PlaceholderAtlas.TileRegion((int)(hash % 7u))
			);
		}

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
