// Connects generated heights to the rendered triangle surface.
// Uses directional slope lighting to make terrain elevation easier to read.
using Godot;
using System.Collections.Generic;

public partial class TerrainElevation : Node
{
    #region Configuration
    [ExportGroup("Height Cache")]
    [Export] public int MaxCachedHeights { get; set; } = 32768;

    [ExportGroup("Slope Lighting")]
    [Export] public bool SlopeLightingEnabled { get; set; } = true;

    [Export(PropertyHint.Range, "0,8,0.1")]
    public float NormalStrength { get; set; } = 3f;

    [Export(PropertyHint.Range, "0,1,0.01")]
    public float AmbientLight { get; set; } = 0.45f;

    [Export(PropertyHint.Range, "0.1,2,0.05")]
    public float SunElevation { get; set; } = 0.65f;

    [Export(PropertyHint.Range, "0.1,1.5,0.01")]
    public float FlatBrightness { get; set; } = 0.85f;
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
    // Resolve generation and register the visual elevation service.
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
    // Reuse exact-coordinate heights with a bounded FIFO cache.
    public float GetHeight(Vector2 tile)
    {
        if (_heights.TryGetValue(tile, out float height))
            return height;

        height = _generator.GetHeight(tile);
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
    // Light slopes by orientation while keeping flat ground brightness consistent.
    public Color GetTint(Vector2 tile)
    {
        float brightness = Mathf.Max(0.1f, FlatBrightness);

        if (!SlopeLightingEnabled)
            return new Color(brightness, brightness, brightness, 1f);

        _atmosphere ??= GetTree().GetFirstNodeInGroup(
            "world_atmosphere") as WorldAtmosphere;

        Vector2 direction = _atmosphere?.LightDirection
            ?? new Vector2(-1f, -0.7f).Normalized();

        Vector2 gradient = GetGroundGradient(tile)
            * Mathf.Max(0f, NormalStrength);

        Vector3 normal = new Vector3(
            -gradient.X, -gradient.Y, 1f).Normalized();

        Vector3 light = new Vector3(
            direction.X, direction.Y,
            Mathf.Max(0.1f, SunElevation)).Normalized();

        float ambient = Mathf.Clamp(AmbientLight, 0f, 1f);
        float diffuse = Mathf.Max(0f, normal.Dot(light));

        float flatLight = ambient + (1f - ambient) * light.Z;
        float slopeLight = ambient + (1f - ambient) * diffuse;

        float shade = brightness * Mathf.Clamp(
            slopeLight / Mathf.Max(0.001f, flatLight), 0.55f, 1.5f);

        // The existing ground shader already applies the sunlight colour.
        return new Color(shade, shade, shade, 1f);
    }
    #endregion
}