// Temporarily draws one shader-textured rock into the baking viewport.
// All painters share a noise texture; their nodes disappear after capture.
using Godot;

public partial class RockBakePainter : Node2D
{
    #region Configuration
    public int Variant { get; set; }
    public WorldAtmosphere Atmosphere { get; set; }
    private static ImageTexture _noise;
    #endregion

    #region Lifecycle
    // =========================================================
    // Assign a bake-only material with a different texture offset per variant.
    public override void _Ready()
    {
        ShaderMaterial material = new()
        {
            Shader = GD.Load<Shader>("res://VISUALS/Drawings/Props/RockBake.gdshader")
        };
        material.SetShaderParameter("rock_noise", GetNoise());
        material.SetShaderParameter("seed_offset",
            new Vector2(Variant * 137.3f, Variant * 89.7f));
        Material = material;
        SetProcess(false);
    }

    // =========================================================
    // Draw this variant's silhouette and shaded facets.
    public override void _Draw()
    {
        RockDrawing.Draw(this, Atmosphere, Variant);
    }
    #endregion

    #region Shared Noise
    // =========================================================
    // Generate the small shared bake texture synchronously, once.
    private static ImageTexture GetNoise()
    {
        if (_noise != null) return _noise;

        using FastNoiseLite noise = new()
        {
            Seed = 73129,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.035f,
            FractalOctaves = 4
        };
        using Image image = noise.GetSeamlessImage(256, 256);
        _noise = ImageTexture.CreateFromImage(image);
        return _noise;
    }
    #endregion
}