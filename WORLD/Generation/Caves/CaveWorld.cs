// Owns one shared cave network and its paired surface holes.
// All entrances use the same cave root, generator and chunk streamer.
using Godot;
using System.Collections.Generic;

public partial class CaveWorld : Node2D
{
    #region State
    public string LayerId { get; set; } = WorldLayerId.Underground1;
    public WorldLayerDefinition Definition { get; private set; }
    public CaveGenerationSettings Settings { get; private set; }
    public CaveGenerator Generator { get; private set; }
    public CaveChunkController Streaming { get; private set; }

    public Node2D Root { get; private set; }
    public Node2D Objects { get; private set; }
    public CaveTerrainElevation Elevation { get; private set; }
    public Vector2 TileSize { get; private set; }
    public float RimHeight { get; private set; }
        public float FloorElevation { get; private set; }

        public float DepthPixels => RimHeight - FloorElevation;
    public float TunnelLengthTiles => Settings.EntranceTunnelLengthTiles;
    public ShaderMaterial GroundMaterial { get; private set; }
    public ImageTexture WhiteTexture { get; private set; }

    private readonly List<CaveHole> _holes = new();
    public IReadOnlyList<CaveHole> Holes => _holes;
    public CaveEntrance Entrance => _holes.Count > 0 ? _holes[0].Marker : null;
        public CaveEntrancePlanner Planner { get; set; }
    #endregion

    #region Construction
    // =========================================================
    // Register the optional cave service for shared placement checks.
    public override void _EnterTree()
    {
        AddToGroup("cave_world");
    }

    // =========================================================
    // Build one shared cave network at the configured underground elevation.
    public void Build(
        Vector2 origin, Vector2 tileSize, float rimHeight,
        uint worldSeed, CaveGenerationSettings settings,
        IReadOnlyList<CaveHole> holes)
    {
        Definition = WorldConfig.Find(this).GetLayerCatalog().Get(LayerId);
        if (Definition.Kind != WorldLayerKind.Underground)
            throw new System.InvalidOperationException(
                $"Layer '{LayerId}' cannot use cave generation.");

        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();
        TileSize = tileSize;
        RimHeight = rimHeight;
        FloorElevation = WorldConfig.Find(this).CaveFloorElevation;

        if (!float.IsFinite(FloorElevation) || FloorElevation >= 0f)
            throw new System.InvalidOperationException(
                "CaveFloorElevation must be finite and below zero.");

        foreach (CaveHole hole in holes)
        {
            if (hole.RimHeight <= FloorElevation)
                throw new System.InvalidOperationException(
                    $"Cave floor must be below entrance '{hole.Id}'. " +
                    "Lower CaveFloorElevation or raise that surface biome.");
        }

        _holes.Clear();
        foreach (CaveHole hole in holes)
            _holes.Add(hole);

                Generator = new CaveGenerator(
            Settings, worldSeed, this, FloorElevation);

        Root = new Node2D { Name = "CaveLayer" };
        AddChild(Root);
        Root.GlobalPosition = origin;

        Objects = new Node2D { Name = "Objects", YSortEnabled = true };
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

        using Image image = Image.CreateEmpty(
            1, 1, false, Image.Format.Rgba8);
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
                new Vector2(-1000000, -1000000),
                new Vector2(1000000, -1000000),
                new Vector2(1000000, 1000000),
                new Vector2(-1000000, 1000000)
            }
        });

        PackedScene scene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");

        foreach (CaveHole hole in _holes)
        {
            CaveEntrance marker = scene.Instantiate<CaveEntrance>();
            marker.Name = $"Hole_{hole.Id}";
            marker.World = this;
            marker.Hole = hole;
            hole.Marker = marker;
            AddChild(marker);
            marker.GlobalPosition = hole.SurfacePosition;
            marker.QueueRedraw();
        }

        Streaming = new CaveChunkController { Name = "CaveStreaming" };
        AddChild(Streaming);
        Streaming.Configure(this);
    }
    #endregion

    #region Coordinates
    // =========================================================
    // Convert logical world positions into the shared network coordinates.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(Root.ToLocal(point), TileSize);
    }

    // =========================================================
    // Convert shared cave coordinates back into logical world positions.
    public Vector2 TileToWorld(Vector2 tile)
    {
        return Root.ToGlobal(IsoGrid.TileToWorld(tile, TileSize));
    }

    // =========================================================
    // Project mesh vertices using the same entrance-aware height field.
    public Vector2 VisiblePoint(Vector2 tile)
    {
        return IsoGrid.TileToWorld(tile, TileSize) +
            Vector2.Up * Generator.VertexHeight(tile);
    }

    // =========================================================
    // Find the closest surface hole for underground preloading while above ground.
    public CaveHole NearestSurfaceHole(Vector2 point)
    {
        CaveHole best = null;
        float distance = float.MaxValue;

        foreach (CaveHole hole in _holes)
        {
            float candidate = point.DistanceSquaredTo(hole.SurfacePosition);
            if (candidate >= distance) continue;
            distance = candidate;
            best = hole;
        }

        return best;
    }

    // =========================================================
    // Identify an entrance ramp without using the player's screen direction.
    public CaveHole TransitionAt(Vector2 tile)
    {
        foreach (CaveHole hole in _holes)
        {
            Vector2 local = hole.Coordinates(tile);
            if (local.X >= -1.5f &&
                local.X <= hole.TunnelLength + 6f &&
                Mathf.Abs(local.Y) < 2.5f)
                return hole;
        }
        return null;
    }

        // =========================================================
    // Read only the surface biome above a logical cave location.
    public BiomeDefinition GetSurfaceBiome(Vector2 caveTile)
    {
        WorldGenerator surface = GetTree().GetFirstNodeInGroup(
            "world_generator") as WorldGenerator;
        Node2D ground = surface.GetNode<Node2D>("../../GroundChunks");

        Vector2 surfaceTile = IsoGrid.WorldToTile(
            ground.ToLocal(TileToWorld(caveTile)), TileSize);
        return surface.GetBiome(surfaceTile);
    }
    #endregion

    #region Surface Reservations
// =========================================================
// Reserve both the surface mouth and its outside landing before props spawn.
public static bool IsHoleReserved(
    Node context, Vector2 point, Vector2 footprint, Vector2 padding)
{
    CaveWorld world = context.GetTree().GetFirstNodeInGroup(
        "cave_world") as CaveWorld;

    if (world == null) return false;

    float objectRadius =
        footprint.Length() * 0.5f + padding.Length();

    foreach (CaveHole hole in world.Holes)
    {
        float radius = hole.SurfaceClearRadius + objectRadius;
        float squared = radius * radius;

        if (point.DistanceSquaredTo(hole.SurfacePosition) < squared ||
            point.DistanceSquaredTo(
                hole.OutsidePosition(world.TileSize)) < squared)
        {
            return true;
        }
    }

    return false;
}
    #endregion

        #region Streamed Entrances
    // =========================================================
    // Register one prepared entrance before nearby terrain and objects generate.
    public void AddHole(CaveHole hole)
    {
        if (_holes.Contains(hole)) return;
        _holes.Add(hole);

        PackedScene scene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");

        CaveEntrance marker = scene.Instantiate<CaveEntrance>();
        marker.Name = $"Hole_{hole.Id}";
        marker.World = this;
        marker.Hole = hole;
        hole.Marker = marker;

        AddChild(marker);
        marker.GlobalPosition = hole.SurfacePosition;
        marker.QueueRedraw();
    }

    // =========================================================
    // Release distant cached entrance artwork; its seed can recreate it later.
    public void RemoveHole(CaveHole hole)
    {
        _holes.Remove(hole);

        if (GodotObject.IsInstanceValid(hole.Marker))
            hole.Marker.QueueFree();

        hole.Marker = null;
    }

    // =========================================================
    // Restrict terrain sampling to mouths near the requested cave coordinate.
    public IEnumerable<CaveHole> NearbyHoles(Vector2 tile)
    {
        return Planner.Nearby(tile);
    }
    #endregion
}