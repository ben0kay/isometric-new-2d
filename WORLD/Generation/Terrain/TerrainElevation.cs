// Connects generated heights to the rendered triangle surface.
// Uses directional slope lighting to make terrain elevation easier to read.
using Godot;
using System.Collections.Generic;

public partial class TerrainElevation : Node
{
#region Configuration
[ExportGroup("Height Cache")]
[Export] public int MaxCachedHeights { get; set; } = 32768;

[ExportGroup("Surface Normals")]
[Export] public bool SlopeLightingEnabled { get; set; } = true;

[Export(PropertyHint.Range, "0,8,0.1")]
public float NormalStrength { get; set; } = 3f;
#endregion

    #region References
    private ChunkController _chunks;
    private Node2D _ground;
    private WorldGenerator _generator;


    private readonly Dictionary<Vector2, float> _heights = new();
    private readonly Queue<Vector2> _heightOrder = new();
    #endregion

    #region Lifecycle
// =========================================================
// Register elevation and initialize the shared terrain slope service.
public override void _Ready()
{
    _chunks = GetNode<ChunkController>("../ChunkController");
    _ground = GetNode<Node2D>("../../GroundChunks");
    _generator = GetNode<WorldGenerator>("../WorldGenerator");

    AddToGroup("terrain_elevation");
    TerrainSlopeWorld.Ensure(this);
    SetProcess(false);
}
    #endregion

    #region Height Sampling
    // =========================================================
    // Cache completed terrain only; temporary cold-query heights remain uncached.
    public float GetHeight(Vector2 tile)
    {
        if (_heights.TryGetValue(tile, out float height))
            return height;

        if (!_generator.TryGetHeight(tile, out height))
            return height;

        int limit = Mathf.Max(1024, MaxCachedHeights);

        while (_heights.Count >= limit)
            _heights.Remove(_heightOrder.Dequeue());

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
            Mathf.Floor(tile.X + 0.5f),
            Mathf.Floor(tile.Y + 0.5f));

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
    #endregion

    #region Slope Lighting
// =========================================================
// Convert tile-axis height differences into the actual ground-plane gradient.
private Vector2 GetGroundGradient(Vector2 tile)
{
    float alongX =
        GetHeight(tile + new Vector2(0.5f, 0f)) -
        GetHeight(tile - new Vector2(0.5f, 0f));

    float alongY =
        GetHeight(tile + new Vector2(0f, 0.5f)) -
        GetHeight(tile - new Vector2(0f, 0.5f));

    // IsoGrid projects X from tile.X - tile.Y,
    // and Y from tile.X + tile.Y.
    return new Vector2(
        (alongX - alongY) / Mathf.Max(1f, _chunks.TileSize.X),
        (alongX + alongY) / Mathf.Max(1f, _chunks.TileSize.Y));
}

// =========================================================
// Encode terrain normals for the shared runtime lighting shader.
// The method name is retained for the existing chunk-building interface.
public Color GetTint(Vector2 tile)
{
    if (!SlopeLightingEnabled)
        return new Color(0.5f, 0.5f, 1f, 1f);

    Vector2 gradient = GetGroundGradient(tile)
        * Mathf.Max(0f, NormalStrength);

    Vector3 normal = new Vector3(
        -gradient.X, -gradient.Y, 1f).Normalized();

    return new Color(
        normal.X * 0.5f + 0.5f,
        normal.Y * 0.5f + 0.5f,
        normal.Z * 0.5f + 0.5f,
        1f);
}
    #endregion
}