// Owns surface queries and places one validated test body of water near spawn.
// Future world generation can call TryPlaceWater with its own profile and coordinates.
using Godot;
using System.Collections.Generic;

public partial class SurfaceWorld : Node
{
    #region Configuration
    [ExportGroup("First-Pass Test")]
    [Export] public WaterDefinition TestWater { get; set; }
    [Export] public bool PlaceTestWater { get; set; } = true;
    [Export] public int TestAttempts { get; set; } = 64;
    #endregion

    #region Sample
    public readonly struct SurfaceSample
    {
        public readonly float MovementMultiplier, SubmersionPixels;
        public readonly Color Tint;

        public SurfaceSample(float movement, float depth, Color tint)
        {
            MovementMultiplier = movement;
            SubmersionPixels = depth;
            Tint = tint;
        }
    }
    #endregion

    #region State
    private readonly List<SurfacePatch> _patches = new();
    private ChunkController _chunks;
    private TerrainElevation _elevation;
    private Node2D _ground;
    private Player _player;
    private Vector2 _testOrigin;
    private int _attempt;
    private bool _searchStarted;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the world service without inserting terrain or collision changes.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _elevation = GetNode<TerrainElevation>("../TerrainElevation");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _player = GetNode<Player>("../../WorldObjects/Player");
        AddToGroup("surface_world");

        TestWater?.Validate();
        SetProcess(PlaceTestWater && TestWater != null);
    }

    // =========================================================
    // Search deterministic nearby positions after the starting terrain is ready.
    public override void _Process(double delta)
    {
        if (!_chunks.WorldReady) return;

        if (!_searchStarted)
        {
            _searchStarted = true;
            _testOrigin = WorldToTile(_player.GlobalPosition);
        }

        if (_attempt >= Mathf.Clamp(TestAttempts, 1, 256))
        {
            GD.PushWarning(
                "Water test: no valid flat location found. " +
                "Check Water.tres size/MaximumHeightVariation.");
            SetProcess(false);
            return;
        }

        float minimumDistance = Mathf.Max(
            TestWater.RadiusTiles.X, TestWater.RadiusTiles.Y) * 1.1f + 3f;
        float angle = _attempt * 2.399963f;
        float distance = minimumDistance + Mathf.Sqrt(_attempt) * 0.8f;
        Vector2 centre = _testOrigin +
            new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        _attempt++;

        WaterPatch patch = TryPlaceWater(TestWater, centre, 1.7f);
        if (patch == null) return;

        GD.Print($"Water test placed at tile {centre}, " +
            $"world {TileToWorld(centre)}. Radius: {TestWater.RadiusTiles}");
        SetProcess(false);
    }
    #endregion

    #region Registration
    // =========================================================
    // Resolve the service without requiring callers to know the world scene name.
    public static SurfaceWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("surface_world")
            as SurfaceWorld;
    }

    // =========================================================
    // Register only fully constructed patches.
    public void Register(SurfacePatch patch)
    {
        if (!_patches.Contains(patch)) _patches.Add(patch);
    }

    // =========================================================
    // Remove patches when their owning nodes leave the world.
    public void Unregister(SurfacePatch patch)
    {
        _patches.Remove(patch);
    }
    #endregion

    #region Placement
    // =========================================================
    // Validate a complete water footprint before creating any artwork.
    public WaterPatch TryPlaceWater(
        WaterDefinition definition, Vector2 centre, float phase)
    {
        if (definition == null || !_chunks.WorldReady) return null;
        definition.Validate();

        float extent = Mathf.Max(
            definition.RadiusTiles.X, definition.RadiusTiles.Y) * 1.1f;
        float checkedRadius = extent + definition.ClearanceTiles;

        int firstChunk = -(_chunks.WorldChunksPerAxis / 2);
        float minimum = firstChunk * _chunks.ChunkSize - 0.5f;
        float maximum = minimum +
            _chunks.WorldChunksPerAxis * _chunks.ChunkSize;

        if (centre.X - checkedRadius < minimum ||
            centre.Y - checkedRadius < minimum ||
            centre.X + checkedRadius > maximum ||
            centre.Y + checkedRadius > maximum)
            return null;

        foreach (SurfacePatch existing in _patches)
        {
            float otherExtent = Mathf.Max(
                existing.Definition.RadiusTiles.X,
                existing.Definition.RadiusTiles.Y) * 1.1f;
            float separation = extent + otherExtent;
            if (centre.DistanceSquaredTo(existing.TileCentre) <
                separation * separation)
                return null;
        }

        if (!SurfaceGeometry.IsFlat(
            _elevation, centre, checkedRadius,
            definition.MaximumHeightVariation))
            return null;

        WaterPatch patch = new()
        {
            Name = $"Water_{_patches.Count}",
            Definition = definition,
            TileCentre = centre,
            TileSize = _chunks.TileSize,
            Phase = phase,
            SurfaceHeight = _elevation.GetHeight(centre),
            World = this
        };
        AddChild(patch);
        return patch;
    }
    #endregion

    #region Coordinates And Effects
    // =========================================================
    // Convert logical world coordinates using the existing ground transform.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(_ground.ToLocal(point), _chunks.TileSize);
    }

    // =========================================================
    // Convert tile coordinates without applying visual elevation.
    public Vector2 TileToWorld(Vector2 tile)
    {
        return _ground.ToGlobal(IsoGrid.TileToWorld(tile, _chunks.TileSize));
    }

    // =========================================================
    // Combine overlaps without multiplying the same slowdown repeatedly.
    public SurfaceSample Sample(Vector2 point)
    {
        Vector2 tile = WorldToTile(point);
        float movement = 1f, depth = 0f;
        Color tint = Colors.White;

        foreach (SurfacePatch patch in _patches)
        {
            float influence = patch.GetInfluence(tile);
            if (influence <= 0f) continue;

            movement = Mathf.Min(movement, Mathf.Lerp(
                1f, patch.Definition.MovementMultiplier, influence));
            float candidateDepth = patch.Definition.SubmersionPixels * influence;
            if (candidateDepth > depth)
            {
                depth = candidateDepth;
                tint = patch.Definition.SurfaceTint;
            }
        }
        return new SurfaceSample(movement, depth, tint);
    }

    // =========================================================
    // Supply surface boundaries to the existing debug overlay.
    public IEnumerable<SurfacePatch> GetDebugPatches()
    {
        return _patches;
    }
    #endregion
}