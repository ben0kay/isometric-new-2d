// Shared isometric coordinate conversions and deterministic generation helpers.
using Godot;

public static class IsoGrid
{
	#region Coordinates
	// =========================================================
	// Convert a tile coordinate into its ground position.
	public static Vector2 TileToWorld(Vector2 tile, Vector2 tileSize)
	{
		return new Vector2(
			(tile.X - tile.Y) * tileSize.X * 0.5f,
			(tile.X + tile.Y) * tileSize.Y * 0.5f
		);
	}

	// =========================================================
	// Convert a ground position into continuous tile coordinates.
	public static Vector2 WorldToTile(Vector2 point, Vector2 tileSize)
	{
		return new Vector2(
			point.X / tileSize.X + point.Y / tileSize.Y,
			point.Y / tileSize.Y - point.X / tileSize.X
		);
	}

	// =========================================================
	// Locate a chunk, correctly handling negative coordinates.
	public static Vector2I WorldToChunk(Vector2 point, Vector2 tileSize, int chunkSize)
	{
		Vector2 tile = WorldToTile(point, tileSize);
		return new Vector2I(
			Mathf.FloorToInt((tile.X + 0.5f) / chunkSize),
			Mathf.FloorToInt((tile.Y + 0.5f) / chunkSize)
		);
	}
	#endregion

	#region Generation
	// =========================================================
	// Hash coordinates and a seed without relying on runtime hash codes.
	public static uint Hash(int x, int y, uint seed)
	{
		unchecked
		{
			uint value = seed ^ ((uint)x * 374761393u) ^ ((uint)y * 668265263u);
			value = (value ^ (value >> 13)) * 1274126177u;
			return value ^ (value >> 16);
		}
	}
	#endregion
}
