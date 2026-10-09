// Owns one independent underground world, its terrain and runtime services.
// Only the surface-connected layer owns surface entrance metadata.
using Godot;
using System.Collections.Generic;

public partial class CaveWorld : Node2D
{
    #region State
    public string LayerId { get; set; } = "";
    public WorldLayerDefinition Definition { get; private set; }
    public CaveGenerationSettings Settings { get; private set; }
    public uint WorldSeed { get; private set; }
    public CaveGenerator Generator { get; private set; }
    public CaveChunkController Streaming { get; private set; }

    public Node2D Root { get; private set; }
    public Node2D Objects { get; private set; }
    public CaveTerrainElevation Elevation { get; private set; }
    public Vector2 TileSize { get; private set; }
        public float FloorElevation { get; private set; }

    public ShaderMaterial GroundMaterial { get; private set; }
    public ImageTexture WhiteTexture { get; private set; }

    private WorldLayerMember _objectsMember;
    private readonly List<WorldLayerConnection> _connections = new();
    public IReadOnlyList<WorldLayerConnection> Connections => _connections;
    private readonly List<WorldLayerConnection> _departures = new();
    public IReadOnlyList<WorldLayerConnection> Departures => _departures;
    public bool Active { get; private set; }
        public CaveEntrancePlanner Planner { get; set; }
    #endregion

    #region Construction
    // =========================================================
    // Keep world ownership free of per-frame processing.
    public override void _EnterTree()
    {
        SetProcess(false);
    }

    // =========================================================
    // Build one shared cave network at the configured underground elevation.
    public void Build(
        Vector2 origin, Vector2 tileSize,
        uint worldSeed, CaveGenerationSettings settings)
    {
        Definition = WorldConfig.Find(this).GetLayerCatalog().Get(LayerId);
        if (Definition.Kind != WorldLayerKind.Underground)
            throw new System.InvalidOperationException(
                $"Layer '{LayerId}' cannot use cave generation.");

        Settings = (CaveGenerationSettings)settings.Duplicate();
        Settings.Validate();
        TileSize = tileSize;
        FloorElevation = Definition.FloorElevation;
        WorldSeed = worldSeed;

        if (!float.IsFinite(FloorElevation) || FloorElevation >= 0f)
            throw new System.InvalidOperationException(
                "The underground layer floor elevation must be finite and below zero.");

                Generator = new CaveGenerator(
            Settings, worldSeed, this, FloorElevation);

        Root = new Node2D { Name = "CaveLayer" };
        AddChild(Root);
        Root.GlobalPosition = origin;
        WorldLayerMember.Attach(Root, LayerId);

        Objects = new Node2D { Name = "Objects", YSortEnabled = true };
        Root.AddChild(Objects);
        _objectsMember = WorldLayerMember.Attach(Objects, LayerId);

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

        Streaming = new CaveChunkController { Name = "CaveStreaming" };
        AddChild(Streaming);
        Streaming.Configure(this);
        AddChild(new WorldNavigation { Name = "Navigation", Cave = this });
    }
    #endregion

    #region Activation
    // =========================================================
    // Separate chunk work from drawing and object processing on inactive depths.
    public void SetActive(bool active)
    {
        Active = active;
        Root.Visible = active;
        Root.Modulate = Colors.White;
        _objectsMember.SetActive(active);
        Streaming.SetActive(active);
    }
    // =========================================================
    // Reveal a paused departure layer without restoring its collisions or processing.
    public void SetPreview(float opacity)
    {
        if (Active) return;
        bool visible = opacity > 0.001f;
        if (Root.Visible != visible) Root.Visible = visible;
        Color colour = Root.Modulate;
        if (Mathf.IsEqualApprox(colour.A, opacity)) return;
        colour.A = opacity;
        Root.Modulate = colour;
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
    public WorldLayerConnection NearestConnection(Vector2 point)
    {
        WorldLayerConnection best = null;
        float distance = float.MaxValue;

        foreach (WorldLayerConnection hole in _connections)
        {
            float candidate = point.DistanceSquaredTo(hole.UpperPosition);
            if (candidate >= distance) continue;
            distance = candidate;
            best = hole;
        }

        return best;
    }

    // =========================================================
    // Identify an entrance ramp without using the player's screen direction.
    public WorldLayerConnection TransitionAt(Vector2 tile)
    {
        foreach (WorldLayerConnection hole in _connections)
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
    WorldLayerRuntime runtime = WorldLayerRuntime.Find(context);
    if (runtime == null) return false;
    CaveWorld world = runtime.SurfaceUnderground;

    float objectRadius = footprint.Length() * 0.5f + padding.Length();
    foreach (WorldLayerConnection hole in world.Connections)
    {
        if (hole.UpperLayer != WorldLayerId.Surface) continue;
        float radius = hole.ClearRadius + objectRadius;
        float squared = radius * radius;
        if (point.DistanceSquaredTo(hole.UpperPosition) < squared ||
            point.DistanceSquaredTo(hole.OutsidePosition(world.TileSize)) < squared)
            return true;
    }
    return false;
}
    #endregion

        #region Streamed Entrances
    // =========================================================
    // Register one prepared entrance before nearby terrain and objects generate.
    public void AttachConnection(WorldLayerConnection hole)
    {
        if (_connections.Contains(hole)) return;
        _connections.Add(hole);

        PackedScene scene = GD.Load<PackedScene>(
            "res://WORLD/Generation/Caves/CaveEntrance.tscn");

        CaveEntrance marker = scene.Instantiate<CaveEntrance>();
        marker.Name = $"Hole_{hole.Id}";
        marker.World = this;
        marker.Connection = hole;
        hole.Marker = marker;

        AddChild(marker);
        marker.GlobalPosition = hole.UpperPosition;
        marker.QueueRedraw();
    }

    // =========================================================
    // Release distant cached entrance artwork; its seed can recreate it later.
    public void DetachConnection(WorldLayerConnection hole)
    {
        _connections.Remove(hole);

        if (GodotObject.IsInstanceValid(hole.Marker))
            hole.Marker.QueueFree();

        hole.Marker = null;
    }

    // =========================================================
    // Index the upper approach independently from lower ramp ownership.
    public void AttachDeparture(WorldLayerConnection connection)
    {
        if (!_departures.Contains(connection)) _departures.Add(connection);
    }

    // =========================================================
    // Release an explicitly removed upper approach.
    public void DetachDeparture(WorldLayerConnection connection)
    {
        _departures.Remove(connection);
    }

    // =========================================================
    // Restrict terrain sampling to mouths near the requested cave coordinate.
    public IEnumerable<WorldLayerConnection> NearbyConnections(Vector2 tile)
    {
        if (Planner != null)
            foreach (WorldLayerConnection connection in Planner.Nearby(tile))
                yield return connection;
        foreach (WorldLayerConnection connection in _connections)
            if (connection.UpperLayer != WorldLayerId.Surface &&
                connection.SampleArea.HasPoint(tile))
                yield return connection;
    }
    #endregion
}
