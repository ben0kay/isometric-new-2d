// Owns the cave layer, shared drawing resources and procedural streamer.
// Entrance-relative cave coordinates remain separate from surface terrain.
using Godot;

public partial class CaveWorld : Node2D
{
    #region State
    public CaveGenerationSettings Settings { get; private set; }
    public CaveGenerator Generator { get; private set; }
    public CaveChunkController Streaming { get; private set; }

    public Node2D Root { get; private set; }
    public Node2D Objects { get; private set; }
    public CaveTerrainElevation Elevation { get; private set; }
    public CaveEntrance Entrance { get; private set; }
    public Vector2 TileSize { get; private set; }
    public float RimHeight { get; private set; }

    public float DepthPixels => Settings.DepthPixels;
    public float TunnelLengthTiles => Settings.EntranceTunnelLengthTiles;
    public ShaderMaterial GroundMaterial { get; private set; }
    public ImageTexture WhiteTexture { get; private set; }
    #endregion

    #region Construction
    // =========================================================
    // Create the cave service without generating the entire network at once.
    public void Build(
        Vector2 origin, Vector2 tileSize, float rimHeight,
        uint worldSeed, CaveGenerationSettings settings)
    {
        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();
        Generator = new CaveGenerator(Settings, worldSeed);
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

        GroundMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>(
                "res://WORLD/Generation/Caves/Rendering/CaveGround.gdshader")
        };

        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        WhiteTexture = ImageTexture.CreateFromImage(image);

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

        PackedScene entranceScene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");
        Entrance = entranceScene.Instantiate<CaveEntrance>();
        Entrance.World = this;
        AddChild(Entrance);
        Entrance.GlobalPosition = origin;
        Entrance.QueueRedraw();

        Streaming = new CaveChunkController { Name = "CaveStreaming" };
        AddChild(Streaming);
        Streaming.Configure(this);
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Convert logical world positions into entrance-relative cave tiles.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(Root.ToLocal(point), TileSize);
    }

    // =========================================================
    // Project mesh vertices using the generator's shared height function.
    public Vector2 VisiblePoint(Vector2 tile)
    {
        Vector2 logical = IsoGrid.TileToWorld(tile, TileSize);
        return logical + Vector2.Up *
            Generator.VertexHeight(tile.X, RimHeight);
    }
    #endregion
}