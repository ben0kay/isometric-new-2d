// Generates deterministic visual terrain elevation and gradient shading.
// Surface sampling matches the chunk mesh so artwork stays on the ground.
using Godot;

public partial class TerrainElevation : Node
{
    #region Configuration
    [Export] public float MaxHeight { get; set; } = 16f;
    [Export] public float FeatureSize { get; set; } = 12f;
    [Export] public float ShadeStrength { get; set; } = 0.04f;
    #endregion

    #region References
    private ChunkController _chunks;
    private Node2D _ground;
private WorldAtmosphere _atmosphere;

    #endregion

    #region Lifecycle
    // =========================================================
    // Register the shared service and resolve world generation settings.
    public override void _Ready()
    {
        MaxHeight = Mathf.Clamp(MaxHeight, 0f, 64f);
        FeatureSize = Mathf.Max(4f, FeatureSize);
        _chunks = GetNode<ChunkController>("../ChunkController");
        _ground = GetNode<Node2D>("../../GroundChunks");
        AddToGroup("terrain_elevation");
    }
    #endregion

    #region Height Sampling
    // =========================================================
    // Combine broad hills with a smaller layer of terrain variation.
    public float GetHeight(Vector2 tile)
    {
        Vector2 sample = tile / FeatureSize;
        float broad = ValueNoise(sample);
        float detail = ValueNoise(sample * 2f + new Vector2(37f, -19f));
        return MaxHeight * (broad * 0.8f + detail * 0.2f);
    }

    // =========================================================
    // Sample the exact triangle surface beneath a logical world position.
    public float SampleWorldHeight(Vector2 globalPoint)
    {
        Vector2 tile = IsoGrid.WorldToTile(_ground.ToLocal(globalPoint), _chunks.TileSize);
        Vector2 centre = new(
            Mathf.Floor(tile.X + 0.5f),
            Mathf.Floor(tile.Y + 0.5f)
        );
        Vector2 local = tile - centre;
        float u = local.X, v = local.Y;
        float height = GetHeight(centre) * (1f - 2f * Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)));

        // Each tile contains four triangles sharing its centre vertex.
        if (Mathf.Abs(u) > Mathf.Abs(v))
        {
            if (u > 0f)
                return height +
                    GetHeight(centre + new Vector2(0.5f, -0.5f)) * (u - v) +
                    GetHeight(centre + new Vector2(0.5f, 0.5f)) * (u + v);

            return height +
                GetHeight(centre + new Vector2(-0.5f, 0.5f)) * (-u + v) +
                GetHeight(centre + new Vector2(-0.5f, -0.5f)) * (-u - v);
        }

        if (v > 0f)
            return height +
                GetHeight(centre + new Vector2(0.5f, 0.5f)) * (v + u) +
                GetHeight(centre + new Vector2(-0.5f, 0.5f)) * (v - u);

        return height +
            GetHeight(centre + new Vector2(-0.5f, -0.5f)) * (-v - u) +
            GetHeight(centre + new Vector2(0.5f, -0.5f)) * (-v + u);
    }

    // =========================================================
// Shade terrain slopes toward the shared sun direction with a subtle warm tint.
public Color GetTint(Vector2 tile)
{
    _atmosphere ??= GetTree().GetFirstNodeInGroup("world_atmosphere") as WorldAtmosphere;
    Vector2 light = _atmosphere?.LightDirection ?? new Vector2(-1f, -0.7f).Normalized();
    Vector2 tileDirection = IsoGrid.WorldToTile(light, _chunks.TileSize).Normalized();

    float height = GetHeight(tile);
    float slope = GetHeight(tile - tileDirection) - GetHeight(tile + tileDirection);
    float altitude = MaxHeight > 0f ? height / MaxHeight : 0.5f;
    float shade = Mathf.Clamp(
        0.7f + altitude * 0.3f + slope * ShadeStrength, 0.45f, 1.15f);

    Color sun = _atmosphere?.SunTint ?? Colors.White;
    return new Color(shade * sun.R, shade * sun.G, shade * sun.B, 1f);
}
    #endregion

    #region Generation
    // =========================================================
    // Interpolate seeded lattice values without allocations or stored noise maps.
    private float ValueNoise(Vector2 point)
    {
        int x = Mathf.FloorToInt(point.X), y = Mathf.FloorToInt(point.Y);
        float u = point.X - x, v = point.Y - y;
        u = u * u * (3f - 2f * u);
        v = v * v * (3f - 2f * v);

        float a = (IsoGrid.Hash(x, y, _chunks.WorldSeed) & 65535u) / 65535f;
        float b = (IsoGrid.Hash(x + 1, y, _chunks.WorldSeed) & 65535u) / 65535f;
        float c = (IsoGrid.Hash(x, y + 1, _chunks.WorldSeed) & 65535u) / 65535f;
        float d = (IsoGrid.Hash(x + 1, y + 1, _chunks.WorldSeed) & 65535u) / 65535f;
        return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
    }
    #endregion
}