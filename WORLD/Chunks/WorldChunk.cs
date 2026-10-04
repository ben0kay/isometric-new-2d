// Builds one static textured mesh per terrain chunk using the baked atlas.
// Shared world-coordinate heights keep neighbouring chunk edges aligned.
using Godot;

using System.Collections.Generic;

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
	private ArrayMesh _cliffMesh;

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
// Release both unique meshes when streaming removes the chunk.
public override void _ExitTree()
{
	_mesh?.Dispose();
	_cliffMesh?.Dispose();
	_mesh = null;
	_cliffMesh = null;
}
	#endregion

	#region Mesh Generation
	// =========================================================
// Build cached ground, chasm floor and cliff faces from shared terrain data.
private void BuildMesh()
{
	int capacity = ChunkSize * ChunkSize * 5;
	List<Vector3> vertices = new(capacity);
	List<Vector2> uvs = new(capacity);
	List<Color> colors = new(capacity);
	List<int> indices = new(ChunkSize * ChunkSize * 12);

	List<Vector3> cliffVertices = new();
	List<Color> cliffColors = new();
	List<int> cliffIndices = new();

	Vector2 atlasSize = PlaceholderAtlas.Texture.GetSize();
	Vector2 chunkOrigin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);

	// Add the dark floor first so cliff faces appear over it.
	for (int y = 0; y < ChunkSize; y++)
	for (int x = 0; x < ChunkSize; x++)
	{
		Vector2 localTile = new(x, y);
		Vector2 globalTile = chunkOrigin + localTile;
		if (!TerrainLayout.IsVoidTile((int)globalTile.X, (int)globalTile.Y)) continue;

		Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1], chunkOrigin);
		Vector2 b = GetSurfacePoint(localTile + VertexOffsets[2], chunkOrigin);
		Vector2 c = GetSurfacePoint(localTile + VertexOffsets[3], chunkOrigin);
		Vector2 d = GetSurfacePoint(localTile + VertexOffsets[4], chunkOrigin);
		Vector2 drop = Vector2.Down * TerrainLayout.CliffDepth;

		AddCliffQuad(cliffVertices, cliffColors, cliffIndices,
			a + drop, b + drop, c + drop, d + drop,
			new Color("#080d15"), new Color("#080d15"));
	}

	for (int y = 0; y < ChunkSize; y++)
	for (int x = 0; x < ChunkSize; x++)
	{
		Vector2 localTile = new(x, y);
		Vector2 globalTile = chunkOrigin + localTile;
		int tileX = (int)globalTile.X, tileY = (int)globalTile.Y;
		if (TerrainLayout.IsVoidTile(tileX, tileY)) continue;

		uint hash = IsoGrid.Hash(tileX, tileY, Seed);
		Rect2 region = PlaceholderAtlas.TileRegion((int)(hash % 7u));
		int first = vertices.Count;

		for (int i = 0; i < 5; i++)
		{
			Vector2 sample = globalTile + VertexOffsets[i];
			Vector2 point = GetSurfacePoint(localTile + VertexOffsets[i], chunkOrigin);
			vertices.Add(new Vector3(point.X, point.Y, 0f));
			uvs.Add((region.Position + TexturePoints[i] * region.Size) / atlasSize);
			colors.Add(_elevation.GetTint(sample));
		}

		for (int side = 0; side < 4; side++)
		{
			indices.Add(first);
			indices.Add(first + 1 + side);
			indices.Add(first + 1 + (side + 1) % 4);
		}

		// These two diamond edges face the camera.
		for (int side = 1; side <= 2; side++)
		{
			int neighbourX = tileX + (side == 1 ? 1 : 0);
			int neighbourY = tileY + (side == 2 ? 1 : 0);
			if (!TerrainLayout.IsVoidTile(neighbourX, neighbourY)) continue;

			Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1 + side], chunkOrigin);
			Vector2 b = GetSurfacePoint(localTile + VertexOffsets[1 + (side + 1) % 4], chunkOrigin);
			Vector2 drop = Vector2.Down * TerrainLayout.CliffDepth;
			Color top = side == 1 ? new Color("#33424d") : new Color("#25323f");

			AddCliffQuad(cliffVertices, cliffColors, cliffIndices,
				a, b, b + drop, a + drop, top, new Color("#0b111b"));
		}
	}

	if (vertices.Count > 0)
	{
		using Godot.Collections.Array arrays = new();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
		_mesh = new ArrayMesh();
		_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	if (cliffVertices.Count > 0)
	{
		using Godot.Collections.Array arrays = new();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = cliffVertices.ToArray();
		arrays[(int)Mesh.ArrayType.Color] = cliffColors.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = cliffIndices.ToArray();
		_cliffMesh = new ArrayMesh();
		_cliffMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	CreateTerrainCollision();
}

// =========================================================
// Project a tile sample onto the existing visual height surface.
private Vector2 GetSurfacePoint(Vector2 localTile, Vector2 chunkOrigin)
{
	Vector2 point = IsoGrid.TileToWorld(localTile, TileSize);
	point.Y -= _elevation.GetHeight(chunkOrigin + localTile);
	return point;
}

// =========================================================
// Append one coloured quad with a vertical shading gradient.
private static void AddCliffQuad(
	List<Vector3> vertices, List<Color> colors, List<int> indices,
	Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color top, Color bottom)
{
	int first = vertices.Count;
	vertices.Add(new Vector3(a.X, a.Y, 0f));
	vertices.Add(new Vector3(b.X, b.Y, 0f));
	vertices.Add(new Vector3(c.X, c.Y, 0f));
	vertices.Add(new Vector3(d.X, d.Y, 0f));
	colors.Add(top);
	colors.Add(top);
	colors.Add(bottom);
	colors.Add(bottom);
	indices.Add(first);
	indices.Add(first + 1);
	indices.Add(first + 2);
	indices.Add(first);
	indices.Add(first + 2);
	indices.Add(first + 3);
}

// =========================================================
// Merge this chunk's rectangular void into one solid logical collision polygon.
private void CreateTerrainCollision()
{
    Vector2 origin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);
    Rect2 chunkBounds = new(origin - Vector2.One * 0.5f, Vector2.One * ChunkSize);
    Rect2 chasm = TerrainLayout.GetTileBounds();

    Vector2 low = new(
        Mathf.Max(chunkBounds.Position.X, chasm.Position.X),
        Mathf.Max(chunkBounds.Position.Y, chasm.Position.Y));
    Vector2 high = new(
        Mathf.Min(chunkBounds.End.X, chasm.End.X),
        Mathf.Min(chunkBounds.End.Y, chasm.End.Y));
    if (high.X <= low.X || high.Y <= low.Y) return;

    Node helpers = new() { Name = "TerrainCollision" };
    AddChild(helpers);
    StaticBody2D body = new()
    {
        Name = "Chasm",
        CollisionLayer = TerrainLayout.CollisionLayer,
        CollisionMask = 0
    };
    helpers.AddChild(body);
    body.AddChild(new CollisionPolygon2D
    {
        Name = "Footprint",
        Polygon = new Vector2[]
        {
            IsoGrid.TileToWorld(low - origin, TileSize),
            IsoGrid.TileToWorld(new Vector2(high.X, low.Y) - origin, TileSize),
            IsoGrid.TileToWorld(high - origin, TileSize),
            IsoGrid.TileToWorld(new Vector2(low.X, high.Y) - origin, TileSize)
        }
    });
}
	#endregion

	#region Drawing
// =========================================================
// Submit cached chasm artwork first, then the ground surface.
public override void _Draw()
{
    if (_cliffMesh != null) DrawMesh(_cliffMesh, null);
    if (_mesh != null && PlaceholderAtlas.Texture != null)
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
