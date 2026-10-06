// Owns surface effects and water fills for basins registered before terrain builds.
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
    private WaterBasinWorld _basins;
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve the prepared basin records and wait for starting terrain.
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
    // Create water presentation without changing the already-built basin terrain.
    public override void _Process(double delta)
    {
        if (!_chunks.WorldReady) return;
        SetProcess(false);

        foreach (WaterBasinWorld.Basin basin in _basins.Basins)
            CreateFill(basin);
    }

    // =========================================================
    // Resolve surface gameplay without depending on the scene root name.
    public static SurfaceWorld Find(Node context)
    {
        return context.GetTree().GetFirstNodeInGroup("surface_world")
            as SurfaceWorld;
    }
    #endregion

    #region Registration And Fill
    // =========================================================
    // Register fully constructed surface artwork.
    public void Register(SurfacePatch patch)
    {
        if (!_patches.Contains(patch)) _patches.Add(patch);
    }

    // =========================================================
    // Remove artwork from effect queries without removing basin records.
    public void Unregister(SurfacePatch patch)
    {
        _patches.Remove(patch);
    }

    // =========================================================
    // Fill an accepted basin without checking terrain eligibility again.
    private WaterPatch CreateFill(WaterBasinWorld.Basin basin)
    {
        if (GodotObject.IsInstanceValid(basin.Patch)) return basin.Patch;

        WaterPatch patch = new()
        {
            Name = $"Water_{_patches.Count}",
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
    // Preserve callers while requiring an existing prepared basin.
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
    // Convert logical positions independently from visual elevation.
    public Vector2 WorldToTile(Vector2 point)
    {
        return IsoGrid.WorldToTile(_ground.ToLocal(point), _chunks.TileSize);
    }

    // =========================================================
    // Convert generation coordinates into the world's logical plane.
    public Vector2 TileToWorld(Vector2 tile)
    {
        return _ground.ToGlobal(IsoGrid.TileToWorld(tile, _chunks.TileSize));
    }

    // =========================================================
    // Use actual rendered floor depth to align submersion with the water plane.
    public SurfaceSample Sample(Vector2 point)
    {
        Vector2 tile = WorldToTile(point);
        float movement = 1f, deepest = 0f;
        Color tint = Colors.White;
        float floorHeight = _elevation.SampleWorldHeight(point);

        foreach (SurfacePatch patch in _patches)
        {
            float influence = patch.GetInfluence(tile);
            if (influence <= 0f) continue;

            if (patch is WaterPatch water)
            {
                float depth = Mathf.Max(
                    0f, water.Basin.WaterHeight - floorHeight);
                if (depth <= 0f) continue;

                movement = Mathf.Min(movement, Mathf.Lerp(
                    1f, patch.Definition.MovementMultiplier,
                    Mathf.Clamp(depth / 8f, 0f, 1f)));

                if (depth > deepest)
                {
                    deepest = depth;
                    tint = patch.Definition.SurfaceTint;
                }
            }
            else
            {
                movement = Mathf.Min(movement, Mathf.Lerp(
                    1f, patch.Definition.MovementMultiplier, influence));
                float depth = patch.Definition.SubmersionPixels * influence;
                if (depth > deepest)
                {
                    deepest = depth;
                    tint = patch.Definition.SurfaceTint;
                }
            }
        }
        return new SurfaceSample(movement, deepest, tint);
    }

    // =========================================================
    // Keep existing debug callers compatible.
    public IEnumerable<SurfacePatch> GetDebugPatches()
    {
        return _patches;
    }
    #endregion
}