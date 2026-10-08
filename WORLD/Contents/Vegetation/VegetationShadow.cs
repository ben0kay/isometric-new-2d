// Generates one shared soft contact-shadow texture for vegetation.
// Plants reuse the texture without redrawing or applying their wind shader.
using Godot;

public static class VegetationShadow
{
    #region Shared Resources
    private static ImageTexture _texture;
    #endregion

    #region Creation
    // =========================================================
    // Create the shared elliptical shadow only on its first request.
    public static Texture2D GetTexture()
    {
        if (_texture != null) return _texture;

        const int width = 128;
        const int height = 64;
        using Image image = Image.CreateEmpty(
            width, height, false, Image.Format.Rgba8);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float px = (x + 0.5f - width * 0.5f) / 60f;
            float py = (y + 0.5f - height * 0.5f) / 27f;
            float radius = px * px + py * py;
            float falloff = Mathf.Max(0f, 1f - radius);
            float alpha = falloff * falloff * 0.4f;
            image.SetPixel(x, y, new Color(0.025f, 0.035f, 0.045f, alpha));
        }

        _texture = ImageTexture.CreateFromImage(image);
        return _texture;
    }
    #endregion
}