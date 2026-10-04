// Connects generated terrain heights to the rendered triangle surface.
// Keeps moving artwork aligned with terrain and applies subtle slope shading.
using Godot;
using System.Collections.Generic;

public partial class TerrainElevation : Node
{
    #region Configuration
    [Export] public int MaxCachedHeights { get; set; } = 32768;
    [Export] public float ShadeStrength { get; set; } = 0.02f;
    #endregion

    #region References
    private ChunkController _chunks;
    private Node2D _ground;
    private WorldGenerator _generator;
    private WorldAtmosphere _atmosphere;
    private readonly Dictionary<Vector2, float> _heights = new();
    private readonly Queue<Vector2> _heightOrder = new();
    #endregion

    #region Lifecycle
    // =========================================================
    // Resolve shared generation and register the visual elevation service.
    public override void _Ready()
    {
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");
        _generator = GetNode<WorldGenerator>("../WorldGenerator");
        AddToGroup("terrain_elevation");
        SetProcess(false);
    }
    #endregion

    #region Height Sampling
    // =========================================================
    // Reuse exact-coordinate heights with a bounded FIFO cache; restart after generator edits.
    public float GetHeight(Vector2 tile)
    {
        if (_heights.TryGetValue(tile, out float height)) return height;
        height = _generator.GetHeight(tile);
        int limit = Mathf.Max(1024, MaxCachedHeights);
        while (_heights.Count >= limit) _heights.Remove(_heightOrder.Dequeue());
        _heights.Add(tile, height);
        _heightOrder.Enqueue(tile);
        return height;
    }

    // =========================================================
    // Interpolate the exact four-triangle surface used by each rendered tile.
    public float SampleWorldHeight(Vector2 globalPoint)
    {
        Vector2 tile = IsoGrid.WorldToTile(
            _ground.ToLocal(globalPoint), _chunks.TileSize);
        Vector2 centre = new(
            Mathf.Floor(tile.X + 0.5f), Mathf.Floor(tile.Y + 0.5f));
        Vector2 local = tile - centre;
        float u = local.X, v = local.Y;
        float height = GetHeight(centre)
            * (1f - 2f * Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)));

        if (Mathf.Abs(u) > Mathf.Abs(v))
        {
            if (u > 0f)
                return height
                    + GetHeight(centre + new Vector2(0.5f, -0.5f)) * (u - v)
                    + GetHeight(centre + new Vector2(0.5f, 0.5f)) * (u + v);

            return height
                + GetHeight(centre + new Vector2(-0.5f, 0.5f)) * (-u + v)
                + GetHeight(centre + new Vector2(-0.5f, -0.5f)) * (-u - v);
        }

        if (v > 0f)
            return height
                + GetHeight(centre + new Vector2(0.5f, 0.5f)) * (v + u)
                + GetHeight(centre + new Vector2(-0.5f, 0.5f)) * (v - u);

        return height
            + GetHeight(centre + new Vector2(-0.5f, -0.5f)) * (-v - u)
            + GetHeight(centre + new Vector2(0.5f, -0.5f)) * (-v + u);
    }

    // =========================================================
    // Shade broad terrain slopes while keeping ground surface detail restrained.
    public Color GetTint(Vector2 tile)
    {
        _atmosphere ??= GetTree().GetFirstNodeInGroup(
            "world_atmosphere") as WorldAtmosphere;
        Vector2 light = _atmosphere?.LightDirection
            ?? new Vector2(-1f, -0.7f).Normalized();
        Vector2 tileDirection = IsoGrid.WorldToTile(
            light, _chunks.TileSize).Normalized();

        float height = GetHeight(tile);
        float slope = GetHeight(tile - tileDirection)
            - GetHeight(tile + tileDirection);
        float altitude = height / _generator.HeightRange;
        float shade = Mathf.Clamp(
            0.7f + altitude * 0.3f + slope * ShadeStrength, 0.45f, 1.15f);
        Color sun = _atmosphere?.SunTint ?? Colors.White;
        return new Color(shade * sun.R, shade * sun.G, shade * sun.B, 1f);
    }
    #endregion
}
