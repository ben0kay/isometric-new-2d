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
	private ArrayMesh _floorMesh;

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
// Order ravine floors below walls, then draw all walkable ground above both.
public override void _Ready()
{
	TextureFilter = TextureFilterEnum.Nearest;
	_elevation = GetTree().GetFirstNodeInGroup("terrain_elevation") as TerrainElevation;
	WorldAtmosphere atmosphere =
		GetTree().GetFirstNodeInGroup("world_atmosphere") as WorldAtmosphere;

	if (_elevation == null || PlaceholderAtlas.Texture == null)
	{
		GD.PushError("WorldChunk requires TerrainElevation and the baked atlas.");
		return;
	}

	Material = atmosphere != null
		? atmosphere.GroundMaterial : PlaceholderAtlas.BakedMaterial;
	BuildMesh();

	Material fogMaterial = atmosphere != null
		? atmosphere.FogMaterial : PlaceholderAtlas.BakedMaterial;

	if (_floorMesh != null)
	{
		AddChild(new MeshInstance2D
		{
			Name = "RavineFloor",
			Mesh = _floorMesh,
			Material = fogMaterial,
			ZAsRelative = true,
			ZIndex = -2
		});
	}

	if (_cliffMesh != null)
	{
		AddChild(new MeshInstance2D
		{
			Name = "CliffVisual",
			Mesh = _cliffMesh,
			Material = fogMaterial,
			ZAsRelative = true,
			ZIndex = -1
		});
	}

	QueueRedraw();
}

// =========================================================
// Release the chunk's unique ground, cliff and floor meshes.
public override void _ExitTree()
{
    _mesh?.Dispose();
    _cliffMesh?.Dispose();
    _floorMesh?.Dispose();
    _mesh = null;
    _cliffMesh = null;
    _floorMesh = null;
}
	#endregion

	#region Mesh Generation
// =========================================================
// Build separate ground, floor and wall meshes with explicit fog depth coordinates.
private void BuildMesh()
{
    int capacity = ChunkSize * ChunkSize * 5;
    List<Vector3> vertices = new(capacity);
    List<Vector2> uvs = new(capacity);
    List<Color> colors = new(capacity);
    List<int> indices = new(ChunkSize * ChunkSize * 12);

    List<Vector3> floorVertices = new();
    List<Vector2> floorUvs = new();
    List<Color> floorColors = new();
    List<int> floorIndices = new();

    List<Vector3> cliffVertices = new();
    List<Vector2> cliffUvs = new();
    List<Color> cliffColors = new();
    List<int> cliffIndices = new();

    Vector2 atlasSize = PlaceholderAtlas.Texture.GetSize();
    Vector2 origin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);
    Vector2 drop = Vector2.Down * TerrainLayout.CliffDepth;

    for (int y = 0; y < ChunkSize; y++)
    for (int x = 0; x < ChunkSize; x++)
    {
        Vector2 localTile = new(x, y);
        Vector2 globalTile = origin + localTile;
        int tileX = (int)globalTile.X, tileY = (int)globalTile.Y;

        if (TerrainLayout.IsVoidTile(tileX, tileY))
        {
            Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1], origin) + drop;
            Vector2 b = GetSurfacePoint(localTile + VertexOffsets[2], origin) + drop;
            Vector2 c = GetSurfacePoint(localTile + VertexOffsets[3], origin) + drop;
            Vector2 d = GetSurfacePoint(localTile + VertexOffsets[4], origin) + drop;

            AddFogQuad(floorVertices, floorUvs, floorColors, floorIndices,
                a, b, c, d, new Color("#080d15"), new Color("#080d15"), true);
            continue;
        }

        uint hash = IsoGrid.Hash(tileX, tileY, Seed);
        Rect2 region = PlaceholderAtlas.TileRegion((int)(hash % 7u));
        int first = vertices.Count;

        for (int i = 0; i < 5; i++)
        {
            Vector2 sample = globalTile + VertexOffsets[i];
            Vector2 point = GetSurfacePoint(localTile + VertexOffsets[i], origin);
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

        for (int side = 1; side <= 2; side++)
        {
            int neighbourX = tileX + (side == 1 ? 1 : 0);
            int neighbourY = tileY + (side == 2 ? 1 : 0);
            if (!TerrainLayout.IsVoidTile(neighbourX, neighbourY)) continue;

            Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1 + side], origin);
            Vector2 b = GetSurfacePoint(localTile + VertexOffsets[1 + (side + 1) % 4], origin);
            Color top = side == 1 ? new Color("#33424d") : new Color("#25323f");

            AddFogQuad(cliffVertices, cliffUvs, cliffColors, cliffIndices,
                a, b, b + drop, a + drop, top, new Color("#0b111b"), false);
        }
    }

    _mesh = CreateCachedMesh(vertices, uvs, colors, indices);
    _floorMesh = CreateCachedMesh(floorVertices, floorUvs, floorColors, floorIndices);
    _cliffMesh = CreateCachedMesh(cliffVertices, cliffUvs, cliffColors, cliffIndices);
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
// Merge consecutive void tiles into solid row polygons matching the ravine.
private void CreateTerrainCollision()
{
	int originX = Coordinate.X * ChunkSize;
	int originY = Coordinate.Y * ChunkSize;
	Node helpers = null;
	StaticBody2D body = null;
	int shapeCount = 0;

	for (int y = 0; y < ChunkSize; y++)
	{
		int x = 0;
		while (x < ChunkSize)
		{
			if (!TerrainLayout.IsVoidTile(originX + x, originY + y))
			{
				x++;
				continue;
			}

			int first = x;
			while (x < ChunkSize &&
				TerrainLayout.IsVoidTile(originX + x, originY + y))
				x++;

			// Create helpers only for chunks containing void terrain.
			if (body == null)
			{
				helpers = new Node { Name = "TerrainCollision" };
				AddChild(helpers);
				body = new StaticBody2D
				{
					Name = "Ravine",
					CollisionLayer = TerrainLayout.CollisionLayer,
					CollisionMask = 0
				};
				helpers.AddChild(body);

				// Plain Node helpers interrupt transform inheritance.
				body.GlobalTransform = GlobalTransform;
			}

			float left = first - 0.5f, right = x - 0.5f;
			float top = y - 0.5f, bottom = y + 0.5f;
			body.AddChild(new CollisionPolygon2D
			{
				Name = $"Run_{shapeCount++}",
				Polygon = new Vector2[]
				{
					IsoGrid.TileToWorld(new(left, top), TileSize),
					IsoGrid.TileToWorld(new(right, top), TileSize),
					IsoGrid.TileToWorld(new(right, bottom), TileSize),
					IsoGrid.TileToWorld(new(left, bottom), TileSize)
				}
			});
		}
	}
}

// =========================================================
// Append coloured geometry with depth zero at the rim and one at the bottom.
private static void AddFogQuad(
    List<Vector3> vertices, List<Vector2> uvs,
    List<Color> colors, List<int> indices,
    Vector2 a, Vector2 b, Vector2 c, Vector2 d,
    Color top, Color bottom, bool floor)
{
    int first = vertices.Count;
    vertices.Add(new Vector3(a.X, a.Y, 0f));
    vertices.Add(new Vector3(b.X, b.Y, 0f));
    vertices.Add(new Vector3(c.X, c.Y, 0f));
    vertices.Add(new Vector3(d.X, d.Y, 0f));

    float upperDepth = floor ? 1f : 0f;
    uvs.Add(new Vector2(0f, upperDepth));
    uvs.Add(new Vector2(1f, upperDepth));
    uvs.Add(new Vector2(1f, 1f));
    uvs.Add(new Vector2(0f, 1f));

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
// Upload one indexed mesh once and skip chunks with no geometry for this layer.
private static ArrayMesh CreateCachedMesh(
    List<Vector3> vertices, List<Vector2> uvs,
    List<Color> colors, List<int> indices)
{
    if (vertices.Count == 0) return null;

    using Godot.Collections.Array arrays = new();
    arrays.Resize((int)Mesh.ArrayType.Max);
    arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
    arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
    arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
    arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

    ArrayMesh mesh = new();
    mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
    return mesh;
}
	#endregion

	#region Drawing

// =========================================================
// Draw cached ground and outline exposed ravine edges with a narrow rock rim.
public override void _Draw()
{
	if (_mesh != null && PlaceholderAtlas.Texture != null)
		DrawMesh(_mesh, PlaceholderAtlas.Texture);

	DrawRavineRim();
	if (ShowBoundary) DrawBoundary();
}

// =========================================================
// Highlight ground-to-void boundaries, including foreground lips hiding cliff walls.
private void DrawRavineRim()
{
	if (_elevation == null) return;

	Vector2 origin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);
	Color rim = new("#425662");

	for (int y = 0; y < ChunkSize; y++)
	for (int x = 0; x < ChunkSize; x++)
	{
		int globalX = Coordinate.X * ChunkSize + x;
		int globalY = Coordinate.Y * ChunkSize + y;
		if (TerrainLayout.IsVoidTile(globalX, globalY)) continue;

		Vector2 localTile = new(x, y);
		for (int side = 0; side < 4; side++)
		{
			int neighbourX = globalX;
			int neighbourY = globalY;

			switch (side)
			{
				case 0: neighbourY--; break;
				case 1: neighbourX++; break;
				case 2: neighbourY++; break;
				case 3: neighbourX--; break;
			}

			if (!TerrainLayout.IsVoidTile(neighbourX, neighbourY)) continue;

			Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1 + side], origin);
			Vector2 b = GetSurfacePoint(localTile + VertexOffsets[1 + (side + 1) % 4], origin);
			DrawLine(a, b, rim, 1.5f, false);
		}
	}
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
