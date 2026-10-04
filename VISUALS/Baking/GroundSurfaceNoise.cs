// Generates one shared, seamless noise texture for continuous ground materials.
// The image is generated once and sampled by all terrain chunks.
using Godot;

public static class GroundSurfaceNoise
{
    #region Configuration
    private const int TextureSize = 512;
    private const int TextureSeed = 64127;
    private static NoiseTexture2D _texture;
    #endregion

    #region Creation
    // =========================================================
    // Create the shared noise resource once and reuse it across terrain materials.
    public static NoiseTexture2D GetTexture()
    {
        if (_texture != null) return _texture;

        _texture = new NoiseTexture2D
        {
            Width = TextureSize,
            Height = TextureSize,
            Seamless = true,
            Normalize = true,
            Noise = new FastNoiseLite
            {
                Seed = TextureSeed,
                NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
                Frequency = 0.025f,
                FractalOctaves = 4
            }
        };
        return _texture;
    }
    #endregion
}