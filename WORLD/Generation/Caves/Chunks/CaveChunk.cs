// Builds one cave chunk under resumable work.
// Floor and wall artwork are batched; collision follows the same floor samples.
using Godot;
using System.Collections.Generic;

public partial class CaveChunk : Node2D
{
    #region State
    public Vector2I Coordinate { get; private set; }
    public bool Ready { get; private set; }

    private CaveWorld _world;
    private int _size;
    private bool[] _floor;
    private StaticBody2D _body;
    private WorldLayerMember _member;
    #endregion

    #region Construction
    // =========================================================
    // Prepare ownership before any geometry or collision is built.
    public void Configure(CaveWorld world, Vector2I coordinate)
    {
        _world = world;
        Coordinate = coordinate;
        _size = world.Settings.ChunkSize;
        _floor = new bool[_size * _size];
        Visible = false;
        _member = WorldLayerMember.Attach(this, world.LayerId);
    }

// =========================================================
// Sample a floor halo once, then reuse it for geometry and neighbouring walls.
public IEnumerable<int> BuildSteps()
{
    foreach (int step in PrepareMetadata())
        yield return step;

    int stride = _size + 2;
    bool[] samples = new bool[stride * stride];
    Vector2I origin = Coordinate * _size;

    for (int y = -1; y <= _size; y++)
    for (int x = -1; x <= _size; x++)
    {
        samples[(x + 1) + (y + 1) * stride] =
            _world.Generator.IsFloor(origin + new Vector2I(x, y));
        yield return 0;
    }

    List<Vector3> floorVertices = new();
    List<Vector2> floorUV = new();
    List<Vector3> wallVertices = new();
    List<Color> wallColours = new();

    _body = new StaticBody2D
    {
        Name = "Walls",
        CollisionLayer = 0,
        CollisionMask = 0
    };
    AddChild(_body);

    for (int y = 0; y < _size; y++)
    for (int x = 0; x < _size; x++)
    {
        Vector2I tile = origin + new Vector2I(x, y);
        int sample = (x + 1) + (y + 1) * stride;
        bool floor = samples[sample];
        _floor[x + y * _size] = floor;

        if (floor)
        {
            Vector2 centre = new(tile.X, tile.Y);
            Vector2 a = centre + new Vector2(-0.5f, -0.5f);
            Vector2 b = centre + new Vector2(0.5f, -0.5f);
            Vector2 c = centre + new Vector2(0.5f, 0.5f);
            Vector2 d = centre + new Vector2(-0.5f, 0.5f);

            AddFloor(a, b, c, d, floorVertices, floorUV);

            Vector2I neighbour = tile + Vector2I.Left;
            if (!samples[sample - 1] &&
                !_world.Generator.IsMouthEdge(tile, neighbour))
                AddWall(a, d, wallVertices, wallColours);

            neighbour = tile + Vector2I.Right;
            if (!samples[sample + 1] &&
                !_world.Generator.IsMouthEdge(tile, neighbour))
                AddWall(b, c, wallVertices, wallColours);

            neighbour = tile + Vector2I.Up;
            if (!samples[sample - stride] &&
                !_world.Generator.IsMouthEdge(tile, neighbour))
                AddWall(a, b, wallVertices, wallColours);

            neighbour = tile + Vector2I.Down;
            if (!samples[sample + stride] &&
                !_world.Generator.IsMouthEdge(tile, neighbour))
                AddWall(d, c, wallVertices, wallColours);
        }

        yield return 0;
    }

    if (floorVertices.Count > 0)
    {
        AddChild(new MeshInstance2D
        {
            Name = "FloorDrawing",
            ZIndex = -3,
            ZAsRelative = false,
            Mesh = MakeMesh(floorVertices, floorUV, null),
            Texture = _world.WhiteTexture,
            Material = _world.GroundMaterial
        });
    }
    yield return 0;

    if (wallVertices.Count > 0)
    {
        AddChild(new MeshInstance2D
        {
            Name = "WallDrawing",
            ZIndex = -1,
            ZAsRelative = false,
            Mesh = MakeMesh(wallVertices, null, wallColours)
        });
    }
    yield return 0;
}

    // =========================================================
    // Publish complete geometry and apply the current layer's activation state.
    public void Finish(bool active)
    {
        _body.CollisionLayer = 1;
        Visible = true;
        Ready = true;
        SetActive(active);
    }

    // =========================================================
    // Pause this chunk independently of other streamed chunks.
    public void SetActive(bool active)
    {
        if (!Ready) return;
        _member.SetActive(active);
        _member.SetOpacity(active ? 1f : 0f);
    }

    // =========================================================
    // Read already-built floor data for movement checks.
    public bool HasFloor(Vector2I tile)
    {
        Vector2I local = tile - Coordinate * _size;
        return Ready && local.X >= 0 && local.Y >= 0 &&
            local.X < _size && local.Y < _size &&
            _floor[local.X + local.Y * _size];
    }
    #endregion

    #region Geometry
    // =========================================================
    // Append two triangles with absolute tile UVs across chunk boundaries.
    private void AddFloor(
        Vector2 a, Vector2 b, Vector2 c, Vector2 d,
        List<Vector3> vertices, List<Vector2> uv)
    {
        foreach (Vector2 point in new[] { a, b, c, a, c, d })
        {
            Vector2 visible = _world.VisiblePoint(point);
            vertices.Add(new Vector3(visible.X, visible.Y, 0f));
            uv.Add(point);
        }
    }

    // =========================================================
    // Draw exposed wall faces and keep their collision on the logical plane.
    private void AddWall(
        Vector2 a, Vector2 b,
        List<Vector3> vertices, List<Color> colours)
    {
        _body.AddChild(new CollisionShape2D
        {
            Shape = new SegmentShape2D
            {
                A = IsoGrid.TileToWorld(a, _world.TileSize),
                B = IsoGrid.TileToWorld(b, _world.TileSize)
            }
        });

        Vector2 va = _world.VisiblePoint(a);
        Vector2 vb = _world.VisiblePoint(b);
        Vector2 topA = va + Vector2.Up * 48f;
        Vector2 topB = vb + Vector2.Up * 48f;
        Color colour = new("#303b45");

        foreach (Vector2 point in new[] { va, vb, topB, va, topB, topA })
        {
            vertices.Add(new Vector3(point.X, point.Y, 0f));
            colours.Add(colour);
        }
    }

    // =========================================================
    // Upload a single triangle surface for one batched drawing.
    private static ArrayMesh MakeMesh(
        List<Vector3> vertices, List<Vector2> uv, List<Color> colours)
    {
        Godot.Collections.Array arrays = new();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();

        if (uv != null)
            arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray();
        if (colours != null)
            arrays[(int)Mesh.ArrayType.Color] = colours.ToArray();

        ArrayMesh mesh = new();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
    #endregion

    // =========================================================
    // Hold nearby entrance decisions until this cave chunk is removed.
    private IEnumerable<int> PrepareMetadata()
    {
        InfiniteWorldGeneration generation =
            InfiniteWorldGeneration.Find(this);

        if (generation == null)
            throw new System.InvalidOperationException(
                "Cave chunks require InfiniteWorldGeneration.");

        Rect2 area = new(
            new Vector2(Coordinate.X * _size - 0.5f,
                Coordinate.Y * _size - 0.5f),
            Vector2.One * _size);

        GenerationMetadataLease.Attach(this, generation, area, _world.LayerId);

        foreach (int step in generation.PrepareArea(area, _world.LayerId))
            yield return step;
    }
}
