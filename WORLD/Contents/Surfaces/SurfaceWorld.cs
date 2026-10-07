// Owns placed surfaces and liquid fills for permanent terrain basins.
// Combines movement, immersion and exposure without modifying shared definitions.
using Godot;
using System.Collections.Generic;

public partial class SurfaceWorld : Node
{
    #region Configuration
    [ExportGroup("Optional Near-Spawn Basin")]
    [Export] public WaterDefinition TestWater { get; set; }
    [Export] public bool PlaceTestWater { get; set; } = true;
    [Export] public int TestAttempts { get; set; } = 64;
    #endregion

    #region Sample
    public readonly struct SurfaceSample
    {
        public readonly float MovementMultiplier, SubmersionPixels;
        public readonly Color Tint;
        public readonly LiquidDefinition ExposureLiquid;
        public readonly float DamagePerSecond;

        public SurfaceSample(
            float movement, float depth, Color tint,
            LiquidDefinition exposureLiquid = null,
            float damagePerSecond = 0f)
        {
            MovementMultiplier = movement;
            SubmersionPixels = depth;
            Tint = tint;
            ExposureLiquid = exposureLiquid;
            DamagePerSecond = damagePerSecond;
        }
    }
    #endregion

    #region State
    private readonly List<SurfacePatch> _patches = new();
    private ChunkController _chunks;
    private TerrainElevation _elevation;
    private Node2D _ground;
    private WaterBasinWorld _basins;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve prepared basins and wait for the starting terrain.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _elevation = GetNode<TerrainElevation>("../TerrainElevation");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _basins = WaterBasinWorld.Find(this);
        AddToGroup("surface_world");
        SetProcess(_basins != null);
    }

    // =========================================================
    // Create liquid artwork without changing already-built terrain.
    public override void _Process(double delta)
    {
        if (!_chunks.WorldReady) return;
        SetProcess(false);

        foreach (WaterBasinWorld.Basin basin in _basins.Basins)
            CreateFill(basin);
    }

    // =========================================================
    // Resolve the service independently from the scene root name.
    public static SurfaceWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("surface_world")
            as SurfaceWorld;
    }
    #endregion

    #region Registration And Fill
    // =========================================================
    // Register constructed surface artwork.
    public void Register(SurfacePatch patch)
    {
        if (!_patches.Contains(patch)) _patches.Add(patch);
    }

    // =========================================================
    // Remove departing artwork without removing its permanent basin.
    public void Unregister(SurfacePatch patch)
    {
        _patches.Remove(patch);
    }

    // =========================================================
    // Present liquid in a previously accepted basin.
    private WaterPatch CreateFill(WaterBasinWorld.Basin basin)
    {
        if (GodotObject.IsInstanceValid(basin.Patch)) return basin.Patch;

        WaterPatch patch = new()
        {
            Name = $"Liquid_{_patches.Count}",
            Definition = basin.Definition,
            TileCentre = basin.Centre,
            TileSize = _chunks.TileSize,
            Phase = basin.Phase,
            SurfaceHeight = basin.WaterHeight,
            World = this,
            Basin = basin
        };
        basin.Patch = patch;
        AddChild(patch);
        return patch;
    }

    // =========================================================
    // Preserve existing callers while requiring a prepared basin.
    public WaterPatch TryPlaceWater(
        WaterDefinition definition, Vector2 centre, float phase)
    {
        if (_basins == null || !_chunks.WorldReady) return null;

        foreach (WaterBasinWorld.Basin basin in _basins.Basins)
            if (basin.Definition == definition &&
                basin.Centre.DistanceSquaredTo(centre) < 0.0001f)
                return CreateFill(basin);
        return null;
    }
    #endregion

    #region Coordinates And Effects
    // =========================================================
    // Convert logical positions independently from artwork elevation.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(
            _ground.ToLocal(point), _chunks.TileSize);
    }

    // =========================================================
    // Convert generation coordinates into the logical world plane.
    public Vector2 TileToWorld(Vector2 tile)
    {
        return _ground.ToGlobal(IsoGrid.TileToWorld(tile, _chunks.TileSize));
    }

// =========================================================
// Query indexed basin immersion and combine other placed surface effects.
public SurfaceSample Sample(Vector2 point)
{
    Vector2 tile = WorldToTile(point);
    float movement = 1f, deepest = 0f, damage = 0f;
    Color tint = Colors.White;
    LiquidDefinition exposure = null;

    WaterBasinWorld.Basin basin = _basins?.GetBasinAt(tile);
    if (basin != null && basin.Fill > 0.0001f)
    {
        float floorHeight = _elevation.SampleWorldHeight(point);
        float depth = Mathf.Max(0f, basin.WaterHeight - floorHeight);

        if (depth > 0f)
        {
            LiquidDefinition liquid = basin.Definition.Liquid;
            float resistance = Mathf.Clamp(
                depth / liquid.FullResistanceDepthPixels, 0f, 1f);

            movement = Mathf.Lerp(
                1f, liquid.WadingSpeedMultiplier, resistance);
            deepest = depth;
            tint = liquid.SurfaceColour * basin.Definition.SurfaceTint;

            if (depth >= liquid.MinimumDamageDepthPixels &&
                liquid.DamagePerSecond > 0f)
            {
                damage = liquid.DamagePerSecond;
                exposure = liquid;
            }
        }
    }

    foreach (SurfacePatch patch in _patches)
    {
        if (patch is LiquidBody) continue;

        float influence = patch.GetInfluence(tile);
        if (influence <= 0f) continue;

        movement = Mathf.Min(movement, Mathf.Lerp(
            1f, patch.Definition.MovementMultiplier, influence));

        float surfaceDepth = patch.Definition.SubmersionPixels * influence;
        if (surfaceDepth > deepest)
        {
            deepest = surfaceDepth;
            tint = patch.Definition.SurfaceTint;
        }
    }

    return new SurfaceSample(
        movement, deepest, tint, exposure, damage);
}

    // =========================================================
    // Expose placed surfaces to the existing collision debug overlay.
    public IEnumerable<SurfacePatch> GetDebugPatches()
    {
        return _patches;
    }
    #endregion
}