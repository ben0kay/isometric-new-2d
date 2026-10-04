// Shares one animated ground-fog material across streamed terrain chunks.
// Fog visuals reuse existing ground meshes and are removed with their chunks.
using Godot;

public partial class GroundFog : Node
{
    #region Configuration
    [Export] public Color CloudColor { get; set; } = new(0.52f, 0.61f, 0.65f);
    [Export] public float Opacity { get; set; } = 0.10f;
    [Export] public float Coverage { get; set; } = 0.48f;
    [Export] public Vector2 Wind { get; set; } = new(10f, -3f);
    [Export] public int NoiseSeed { get; set; } = 89127;
    #endregion

    #region State
    private ShaderMaterial _material;
    private ImageTexture _noise;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register the shared fog service before terrain chunks are created.
    public override void _EnterTree()
    {
        AddToGroup("ground_fog");
    }

    // =========================================================
    // Bake the shared cloud noise and configure one reusable shader material.
    public override void _Ready()
    {
        using FastNoiseLite noise = new()
        {
            Seed = NoiseSeed,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.018f,
            FractalOctaves = 3
        };
        using Image image = noise.GetSeamlessImage(256, 256);
        _noise = ImageTexture.CreateFromImage(image);

        ChunkController chunks = GetNode<ChunkController>("../ChunkController");
        _material = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://VISUALS/Atmosphere/Fog/GroundFog.gdshader")
        };
        _material.SetShaderParameter("fog_noise", _noise);
        _material.SetShaderParameter("fog_color", CloudColor);
        _material.SetShaderParameter("wind", Wind);
        _material.SetShaderParameter("tile_size", chunks.TileSize);
        _material.SetShaderParameter("opacity", Mathf.Clamp(Opacity, 0f, 0.3f));
        _material.SetShaderParameter("coverage", Mathf.Clamp(Coverage, 0.1f, 0.75f));
        SetProcess(false);
    }
    #endregion

    #region Chunk Visuals
    // =========================================================
    // Reuse the ground geometry as a mist layer above terrain and below objects.
    public void Attach(WorldChunk chunk, ArrayMesh mesh)
    {
        if (_material == null || mesh == null || Opacity <= 0f) return;

        chunk.AddChild(new MeshInstance2D
        {
            Name = "GroundMist",
            Mesh = mesh,
            Material = _material,
            ZAsRelative = false,
            ZIndex = -1
        });
    }
    #endregion
}