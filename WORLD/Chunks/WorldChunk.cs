// Builds one static textured mesh per terrain chunk using the baked atlas.
// Shared world-coordinate heights keep neighbouring chunk edges aligned.
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

	#region State
	private TerrainElevation _elevation;
	private ArrayMesh _mesh;

	private static readonly Vector2[] VertexOffsets =
	{
		Vector2.Zero,
		new(-0.5f, -0.5f), new(0.5f, -0.5f),
		new(0.5f, 0.5f), new(-0.5f, 0.5f)
	};

	private static readonly Vector2[] TexturePoints =
	{
		new(0.5f, 0.5f),
		new(0.5f, 0f), new(1f, 0.5f),
		new(0.5f, 1f), new(0f, 0.5f)
	};
	#endregion

	#region Lifecycle
	// =========================================================
	// Build the chunk mesh after the controller has prepared the atlas.
	public override void _Ready()
	{
		Material = PlaceholderAtlas.BakedMaterial;
		TextureFilter = TextureFilterEnum.Nearest;
		_elevation = GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;

		if (_elevation == null || PlaceholderAtlas.Texture == null)
		{
			GD.PushError("WorldChunk requires TerrainElevation and the baked atlas.");
			return;
		}

		BuildMesh();
		QueueRedraw();
	}

	// =========================================================
	// Release the chunk's unique mesh when streaming removes it.
	public override void _ExitTree()
	{
		_mesh?.Dispose();
		_mesh = null;
	}
	#endregion

	#region Mesh Generation
	// =========================================================
	// Combine every tile into one indexed surface with atlas UVs and shading.
	private void BuildMesh()
	{
		int tileCount = ChunkSize * ChunkSize;
		Vector3[] vertices = new Vector3[tileCount * 5];
		Vector2[] uvs = new Vector2[vertices.Length];
		Color[] colors = new Color[vertices.Length];
		int[] indices = new int[tileCount * 12];
		Vector2 atlasSize = PlaceholderAtlas.Texture.GetSize();
		Vector2 chunkOrigin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);
		int vertexIndex = 0, indexIndex = 0;

		for (int y = 0; y < ChunkSize; y++)
		for (int x = 0; x < ChunkSize; x++)
		{
			Vector2 localTile = new(x, y);
			Vector2 globalTile = chunkOrigin + localTile;
			uint hash = IsoGrid.Hash((int)globalTile.X, (int)globalTile.Y, Seed);
			Rect2 region = PlaceholderAtlas.TileRegion((int)(hash % 7u));

			for (int i = 0; i < 5; i++)
			{
				Vector2 sample = globalTile + VertexOffsets[i];
				Vector2 point = IsoGrid.TileToWorld(localTile + VertexOffsets[i], TileSize);
				point.Y -= _elevation.GetHeight(sample);

				vertices[vertexIndex + i] = new Vector3(point.X, point.Y, 0f);
				uvs[vertexIndex + i] =
					(region.Position + TexturePoints[i] * region.Size) / atlasSize;
				colors[vertexIndex + i] = _elevation.GetTint(sample);
			}

			for (int side = 0; side < 4; side++)
			{
				indices[indexIndex++] = vertexIndex;
				indices[indexIndex++] = vertexIndex + 1 + side;
				indices[indexIndex++] = vertexIndex + 1 + (side + 1) % 4;
			}
			vertexIndex += 5;
		}

		using Godot.Collections.Array arrays = new();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		_mesh = new ArrayMesh();
		_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}
	#endregion

	#region Drawing
	// =========================================================
	// Submit the cached mesh rather than individual tile drawing commands.
	public override void _Draw()
	{
		if (_mesh == null || PlaceholderAtlas.Texture == null) return;
		DrawMesh(_mesh, PlaceholderAtlas.Texture);
		if (ShowBoundary) DrawBoundary();
	}

	// =========================================================
	// Outline the logical chunk footprint for optional streaming debugging.
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
