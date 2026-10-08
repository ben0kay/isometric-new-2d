// Caches low-detail white silhouettes for projected sprite shadows.
// Downsampling uses alpha coverage so thin stems remain visible.
using Godot;
using System.Collections.Generic;

public static class GroundShadowTextures
{
    #region Cache
    private const int MaximumSize = 96;
    private static readonly Dictionary<string, Texture2D> Masks = new();
    private static ImageTexture _contact;
    #endregion

    #region Silhouettes
    // =========================================================
    // Read one artwork region and cache its simplified alpha silhouette.
    public static Texture2D GetSilhouette(Sprite2D sprite)
    {
        Texture2D texture = sprite.Texture;
        Rect2 region = new(Vector2.Zero, texture.GetSize());

        if (texture is AtlasTexture atlas)
        {
            // Current baked atlases use regions without margins.
            if (atlas.Margin.Size != Vector2.Zero)
            {
                GD.PushWarning("Shadow silhouettes do not support atlas margins yet.");
                return null;
            }

            texture = atlas.Atlas;
            region = atlas.Region;
            if (region.Size == Vector2.Zero)
                region.Size = texture.GetSize();
        }

        if (texture == null) return null;

        if (sprite.RegionEnabled)
        {
            region.Position += sprite.RegionRect.Position;
            region.Size = sprite.RegionRect.Size;
        }

        Vector2 frameSize = region.Size /
            new Vector2(sprite.Hframes, sprite.Vframes);

        region.Position += frameSize *
            new Vector2(sprite.FrameCoords.X, sprite.FrameCoords.Y);
        region.Size = frameSize;

        Rect2I pixels = new(
            Mathf.RoundToInt(region.Position.X),
            Mathf.RoundToInt(region.Position.Y),
            Mathf.RoundToInt(region.Size.X),
            Mathf.RoundToInt(region.Size.Y));

        string key = $"{texture.GetInstanceId()}|" +
            $"{pixels.Position.X},{pixels.Position.Y}|" +
            $"{pixels.Size.X},{pixels.Size.Y}";

        if (Masks.TryGetValue(key, out Texture2D cached))
            return cached;

        using Image source = texture.GetImage();
        if (source == null || source.IsEmpty()) return null;

        if (source.IsCompressed() && source.Decompress() != Error.Ok)
            return null;

        if (pixels.Size.X <= 0 || pixels.Size.Y <= 0 ||
            pixels.Position.X < 0 || pixels.Position.Y < 0 ||
            pixels.End.X > source.GetWidth() ||
            pixels.End.Y > source.GetHeight())
            return null;

        float reduction = Mathf.Min(1f, MaximumSize /
            (float)Mathf.Max(pixels.Size.X, pixels.Size.Y));

        int width = Mathf.Max(1, Mathf.RoundToInt(pixels.Size.X * reduction));
        int height = Mathf.Max(1, Mathf.RoundToInt(pixels.Size.Y * reduction));

        using Image mask = Image.CreateEmpty(
            width, height, false, Image.Format.Rgba8);

        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            int left = x * pixels.Size.X / width;
            int top = y * pixels.Size.Y / height;
            int right = Mathf.Min(pixels.Size.X,
                Mathf.CeilToInt((x + 1f) * pixels.Size.X / width));
            int bottom = Mathf.Min(pixels.Size.Y,
                Mathf.CeilToInt((y + 1f) * pixels.Size.Y / height));

            float alpha = 0f;

            for (int sy = top; sy < bottom; sy++)
            for (int sx = left; sx < right; sx++)
                alpha = Mathf.Max(alpha, source.GetPixel(
                    pixels.Position.X + sx, pixels.Position.Y + sy).A);

            mask.SetPixel(x, y, new Color(
                1f, 1f, 1f, alpha >= 0.15f ? 1f : 0f));
        }

        Texture2D result = ImageTexture.CreateFromImage(mask);
        Masks.Add(key, result);
        return result;
    }
    #endregion

    #region Contact
    // =========================================================
    // Share one small soft oval between all contact shadows.
    public static Texture2D GetContact()
    {
        if (_contact != null) return _contact;

        using Image image = Image.CreateEmpty(
            64, 32, false, Image.Format.Rgba8);

        for (int y = 0; y < 32; y++)
        for (int x = 0; x < 64; x++)
        {
            float px = (x + 0.5f - 32f) / 30f;
            float py = (y + 0.5f - 16f) / 14f;
            float falloff = Mathf.Max(0f, 1f - px * px - py * py);
            image.SetPixel(x, y,
                new Color(1f, 1f, 1f, falloff * falloff));
        }

        _contact = ImageTexture.CreateFromImage(image);
        return _contact;
    }
    #endregion
}