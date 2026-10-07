// Prepares terrain one tile at a time, then uploads meshes and collision in separate stages.
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
	private WorldAtmosphere _atmosphere;
	private MeshData _surfaceData, _floorData, _cliffData;
	private readonly Dictionary<Vector2, Color> _tints = new();
	private readonly List<Vector2> _rimPoints = new();
	private GroundResourceWorld _groundResources;

	private sealed class MeshData
	{
		public readonly List<Vector3> Vertices;
		public readonly List<Vector2> Uvs;
		public readonly List<Color> Colors;
		public readonly List<int> Indices;
		// =========================================================
		// Allocate the buffers for one prepared terrain layer.
		public MeshData(int capacity)
		{
			Vertices = new(capacity); Uvs = new(capacity);
			Colors = new(capacity); Indices = new(capacity * 3);
		}
	}

	private static readonly Vector2[] VertexOffsets =
	{
		Vector2.Zero,
		new(-0.5f, -0.5f), new(0.5f, -0.5f),
		new(0.5f, 0.5f), new(-0.5f, 0.5f)
	};

	#endregion

	#region Lifecycle

// =========================================================
// Resolve shared terrain services without rebuilding or changing the ground surface.
public override void _Ready()
{
	TextureFilter = TextureFilterEnum.Nearest;
	_elevation = GetTree().GetFirstNodeInGroup("terrain_elevation")
		as TerrainElevation;
	_atmosphere = GetTree().GetFirstNodeInGroup("world_atmosphere")
		as WorldAtmosphere;

	if (_elevation == null || PlaceholderAtlas.Texture == null)
		throw new System.InvalidOperationException(
			"WorldChunk requires elevation and cached artwork.");

	_groundResources = GroundResourceWorld.Ensure(this);
	Material = _atmosphere != null
		? _atmosphere.GroundMaterial : PlaceholderAtlas.BakedMaterial;
	SetProcess(false);
}

// =========================================================
// Release chunk meshes and deposit visuals while preserving world depletion.
public override void _ExitTree()
{
	if (GodotObject.IsInstanceValid(_groundResources))
		_groundResources.Release(Coordinate);

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
// Build cached terrain with continuous ground coordinates and separate ravine layers.
public IEnumerable<ChunkBuildStage> PrepareSteps()
{
	_surfaceData = new(ChunkSize * ChunkSize * 5);
	_floorData = new(0); _cliffData = new(0);
	var vertices = _surfaceData.Vertices; var uvs = _surfaceData.Uvs;
	var colors = _surfaceData.Colors; var indices = _surfaceData.Indices;
	var floorVertices = _floorData.Vertices; var floorUvs = _floorData.Uvs;
	var floorColors = _floorData.Colors; var floorIndices = _floorData.Indices;
	var cliffVertices = _cliffData.Vertices; var cliffUvs = _cliffData.Uvs;
	var cliffColors = _cliffData.Colors; var cliffIndices = _cliffData.Indices;

	Vector2 origin = new(Coordinate.X * ChunkSize, Coordinate.Y * ChunkSize);
	Vector2 drop = Vector2.Down * ChasmFeature.CliffDepth;

	for (int y = 0; y < ChunkSize; y++)
	for (int x = 0; x < ChunkSize; x++)
	{
		yield return ChunkBuildStage.TerrainData;
		Vector2 localTile = new(x, y);
		Vector2 globalTile = origin + localTile;
		int tileX = (int)globalTile.X, tileY = (int)globalTile.Y;

		if (ChasmFeature.IsVoidTile(tileX, tileY))
		{
			Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1], origin) + drop;
			Vector2 b = GetSurfacePoint(localTile + VertexOffsets[2], origin) + drop;
			Vector2 c = GetSurfacePoint(localTile + VertexOffsets[3], origin) + drop;
			Vector2 d = GetSurfacePoint(localTile + VertexOffsets[4], origin) + drop;

			AddFogQuad(floorVertices, floorUvs, floorColors, floorIndices,
				a, b, c, d, new Color("#080d15"), new Color("#080d15"), true);
			continue;
		}

		int first = vertices.Count;
		for (int i = 0; i < 5; i++)
		{
			Vector2 sample = globalTile + VertexOffsets[i];
			Vector2 point = GetSurfacePoint(localTile + VertexOffsets[i], origin);
			vertices.Add(new Vector3(point.X, point.Y, 0f));
			uvs.Add(sample);
			colors.Add(GetSurfaceTint(sample));
		}

		for (int side = 0; side < 4; side++)
		{
			indices.Add(first);
			indices.Add(first + 1 + side);
			indices.Add(first + 1 + (side + 1) % 4);
		}

		for (int side = 0; side < 4; side++)
		{
			int nx = tileX + (side == 1 ? 1 : side == 3 ? -1 : 0);
			int ny = tileY + (side == 2 ? 1 : side == 0 ? -1 : 0);
			if (!ChasmFeature.IsVoidTile(nx, ny)) continue;
			_rimPoints.Add(GetSurfacePoint(localTile + VertexOffsets[1 + side], origin));
			_rimPoints.Add(GetSurfacePoint(localTile + VertexOffsets[1 + (side + 1) % 4], origin));
		}

		for (int side = 1; side <= 2; side++)
		{
			int neighbourX = tileX + (side == 1 ? 1 : 0);
			int neighbourY = tileY + (side == 2 ? 1 : 0);
			if (!ChasmFeature.IsVoidTile(neighbourX, neighbourY)) continue;

			Vector2 a = GetSurfacePoint(localTile + VertexOffsets[1 + side], origin);
			Vector2 b = GetSurfacePoint(localTile + VertexOffsets[1 + (side + 1) % 4], origin);
			Color top = side == 1 ? new Color("#33424d") : new Color("#25323f");

			AddFogQuad(cliffVertices, cliffUvs, cliffColors, cliffIndices,
				a, b, b + drop, a + drop, top, new Color("#0b111b"), false);
		}
	}

	_tints.Clear();

	foreach (ChunkBuildStage stage in _groundResources.Prepare(this))
	yield return stage;
}

// =========================================================
// Upload existing terrain and bind biome colours plus harvestable deposits.
public IEnumerable<ChunkBuildStage> UploadSteps()
{
	BiomeGroundMap groundMap = new() { Name = "BiomeGround" };
	AddChild(groundMap);

	foreach (ChunkBuildStage stage in groundMap.Prepare(this))
		yield return stage;

	yield return ChunkBuildStage.TerrainUpload;
	_mesh = Upload(_surfaceData);
	_surfaceData = null;

	if (_atmosphere?.GroundMaterial != null)
	{
		ShaderMaterial biomeMaterial =
			groundMap.Bind(_atmosphere.GroundMaterial);

		Material = _groundResources.BindMaterial(this, biomeMaterial);
	}

	yield return ChunkBuildStage.TerrainUpload;
	_floorMesh = Upload(_floorData);
	_floorData = null;

	yield return ChunkBuildStage.TerrainUpload;
	_cliffMesh = Upload(_cliffData);
	_cliffData = null;

	yield return ChunkBuildStage.TerrainUpload;
	Material fog = _atmosphere != null
		? _atmosphere.FogMaterial : PlaceholderAtlas.BakedMaterial;

	if (_floorMesh != null)
		AddChild(new MeshInstance2D
		{
			Name = "RavineFloor",
			Mesh = _floorMesh,
			Material = fog,
			ZIndex = -2
		});

	yield return ChunkBuildStage.TerrainUpload;

	if (_cliffMesh != null)
		AddChild(new MeshInstance2D
		{
			Name = "CliffVisual",
			Mesh = _cliffMesh,
			Material = fog,
			ZIndex = -1
		});

	foreach (ChunkBuildStage stage in CreateTerrainCollisionSteps())
		yield return stage;

	yield return ChunkBuildStage.TerrainUpload;

	GroundFog fogService = GetTree()
		.GetFirstNodeInGroup("ground_fog") as GroundFog;

	if (_mesh != null) fogService?.Attach(this, _mesh);
	QueueRedraw();
}

// =========================================================
// Reuse a shared vertex tint instead of resampling the same corner for adjacent tiles.
private Color GetSurfaceTint(Vector2 tile)
{
	if (_tints.TryGetValue(tile, out Color color)) return color;
	color = _elevation.GetTint(tile); _tints.Add(tile, color);
	return color;
}

// =========================================================
// Upload a completed layer; this individual engine call cannot be time-sliced.
private static ArrayMesh Upload(MeshData data)
{
	return CreateCachedMesh(data.Vertices, data.Uvs, data.Colors, data.Indices);
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
// Build staged collision for chasms and globally classified steep terrain.
private IEnumerable<ChunkBuildStage> CreateTerrainCollisionSteps()
{
	TerrainSlopeWorld slopes = TerrainSlopeWorld.Ensure(this);
	int resolution = ChunkSize * 2;
	int firstX = Coordinate.X * resolution;
	int firstY = Coordinate.Y * resolution;

	bool[] blocked = new bool[resolution];
	Node helpers = null;
	StaticBody2D body = null;
	int shapeCount = 0;

	for (int y = 0; y < resolution; y++)
	{
		for (int x = 0; x < resolution; x++)
		{
			yield return ChunkBuildStage.TerrainCollision;
			Vector2I cell = new(firstX + x, firstY + y);
			Vector2 centre = TerrainSlopeWorld.CellCentre(cell);

			blocked[x] = ChasmFeature.IsVoidTile(
				Mathf.FloorToInt(centre.X + 0.5f),
				Mathf.FloorToInt(centre.Y + 0.5f)) ||
				slopes.SampleCell(cell).Blocked;
		}

		int column = 0;
		while (column < resolution)
		{
			if (!blocked[column])
			{
				column++;
				continue;
			}

			int first = column;
			while (column < resolution && blocked[column])
				column++;

			if (body == null)
			{
				helpers = new Node { Name = "TerrainCollision" };
				AddChild(helpers);

				body = new StaticBody2D
				{
					Name = "BlockedTerrain",
					CollisionLayer = ChasmFeature.CollisionLayer,
					CollisionMask = 0
				};
				helpers.AddChild(body);
				body.GlobalTransform = GlobalTransform;
			}

			float left = first * 0.5f - 0.5f;
			float right = column * 0.5f - 0.5f;
			float top = y * 0.5f - 0.5f;
			float bottom = top + 0.5f;

			yield return ChunkBuildStage.TerrainCollision;
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
	Color rim = new("#425662");
	for (int i = 0; i < _rimPoints.Count; i += 2)
		DrawLine(_rimPoints[i], _rimPoints[i + 1], rim, 1.5f, false);
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
