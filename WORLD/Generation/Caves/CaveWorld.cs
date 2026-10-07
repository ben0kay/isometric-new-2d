// Builds the first cave floor, boundary walls and object root.
// Procedural generation and streaming will replace this fixed test layout later.
using Godot;
using System.Collections.Generic;

public partial class CaveWorld : Node2D
{
    #region Configuration
    public float DepthPixels { get; set; } = 160f;
    public float TunnelLengthTiles { get; set; } = 14f;
    #endregion

    #region State
    public Node2D Root { get; private set; }
    public Node2D Objects { get; private set; }
    public CaveTerrainElevation Elevation { get; private set; }
    public CaveEntrance Entrance { get; private set; }
    public Vector2 TileSize { get; private set; }
    public float RimHeight { get; private set; }

    private readonly HashSet<Vector2I> _floor = new();
    private ShaderMaterial _groundMaterial;
    private ImageTexture _white;
    #endregion

    #region Construction
    // =========================================================
    // Build a self-contained cave at the selected surface opening.
    public void Build(Vector2 origin, Vector2 tileSize, float rimHeight)
    {
        TileSize = tileSize;
        RimHeight = rimHeight;

        Root = new Node2D { Name = "CaveLayer" };
        AddChild(Root);
        Root.GlobalPosition = origin;

        Objects = new Node2D
        {
            Name = "Objects",
            YSortEnabled = true
        };
        Root.AddChild(Objects);

        Elevation = new CaveTerrainElevation
        {
            Name = "Elevation",
            World = this
        };
        AddChild(Elevation);

        _groundMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(
                "res://WORLD/Generation/Caves/Rendering/CaveGround.gdshader")
        };

        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        _white = ImageTexture.CreateFromImage(image);

        Root.AddChild(new Polygon2D
        {
            Name = "UndergroundBackground",
            ZIndex = -4,
            ZAsRelative = false,
            Color = new Color("#080d12"),
            Polygon = new[]
            {
                new Vector2(-20000, -20000), new Vector2(20000, -20000),
                new Vector2(20000, 20000), new Vector2(-20000, 20000)
            }
        });

        for (int x = 0; x <= 28; x++)
        for (int y = -6; y <= 6; y++)
            if ((x <= 16 && Mathf.Abs(y) <= 1) || x >= 17)
                _floor.Add(new Vector2I(x, y));

        foreach (Vector2I tile in _floor)
            BuildFloor(tile);

        foreach (Vector2I tile in _floor)
            BuildEdges(tile);

        PackedScene entranceScene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");
        Entrance = entranceScene.Instantiate<CaveEntrance>();
        Entrance.World = this;
        AddChild(Entrance);
        Entrance.GlobalPosition = origin;
        Entrance.QueueRedraw();
    }

    // =========================================================
    // Build one floor quad with matching cave-coordinate shader UVs.
    private void BuildFloor(Vector2I tile)
    {
        Vector2 centre = new(tile.X, tile.Y);
        Vector2[] corners =
        {
            centre + new Vector2(-0.5f, -0.5f),
            centre + new Vector2(0.5f, -0.5f),
            centre + new Vector2(0.5f, 0.5f),
            centre + new Vector2(-0.5f, 0.5f)
        };

        Vector2[] polygon = new Vector2[4];
        for (int i = 0; i < 4; i++)
            polygon[i] = VisiblePoint(corners[i]);

        Root.AddChild(new Polygon2D
        {
            Name = $"Floor_{tile.X}_{tile.Y}",
            ZIndex = -3,
            ZAsRelative = false,
            Polygon = polygon,
            UV = corners,
            Texture = _white,
            Material = _groundMaterial
        });
    }

    // =========================================================
    // Close each exposed edge, leaving only the entrance mouth open.
    private void BuildEdges(Vector2I tile)
    {
        Vector2 centre = new(tile.X, tile.Y);

        if (!_floor.Contains(tile + new Vector2I(-1, 0)) && tile.X != 0)
            BuildWall(centre + new Vector2(-0.5f, -0.5f),
                centre + new Vector2(-0.5f, 0.5f));

        if (!_floor.Contains(tile + new Vector2I(1, 0)))
            BuildWall(centre + new Vector2(0.5f, -0.5f),
                centre + new Vector2(0.5f, 0.5f));

        if (!_floor.Contains(tile + new Vector2I(0, -1)))
            BuildWall(centre + new Vector2(-0.5f, -0.5f),
                centre + new Vector2(0.5f, -0.5f));

        if (!_floor.Contains(tile + new Vector2I(0, 1)))
            BuildWall(centre + new Vector2(-0.5f, 0.5f),
                centre + new Vector2(0.5f, 0.5f));
    }

    // =========================================================
    // Keep physical wall positions on the logical plane and draw raised faces.
    private void BuildWall(Vector2 a, Vector2 b)
    {
        Vector2 logicalA = IsoGrid.TileToWorld(a, TileSize);
        Vector2 logicalB = IsoGrid.TileToWorld(b, TileSize);

        StaticBody2D body = new()
        {
            CollisionLayer = 1,
            CollisionMask = 0
        };
        Root.AddChild(body);
        body.AddChild(new CollisionShape2D
        {
            Shape = new SegmentShape2D
            {
                A = logicalA,
                B = logicalB
            }
        });

        Vector2 visibleA = VisiblePoint(a);
        Vector2 visibleB = VisiblePoint(b);

        Root.AddChild(new Polygon2D
        {
            ZIndex = -1,
            ZAsRelative = false,
            Color = new Color("#303b45"),
            Polygon = new[]
            {
                visibleA, visibleB,
                visibleB + Vector2.Up * 48f,
                visibleA + Vector2.Up * 48f
            }
        });
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Convert logical world positions into this cave's local tile coordinates.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(Root.ToLocal(point), TileSize);
    }

    // =========================================================
    // Project one cave floor point using the same height sampler as artwork.
    private Vector2 VisiblePoint(Vector2 tile)
    {
        Vector2 logical = IsoGrid.TileToWorld(tile, TileSize);
        float height = Elevation.SampleWorldHeight(Root.ToGlobal(logical));
        return logical + Vector2.Up * height;
    }
    #endregion
}