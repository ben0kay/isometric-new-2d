// Generates the alien berry's shared placeholder icon and world-pickup artwork.
// An assigned ItemDefinition.Icon bypasses this fallback.
using Godot;
using System;

public static class AlienBerryDrawing
{
    #region Artwork
    private static ImageTexture _texture;

    // =========================================================
    // Generate a small berry cluster once and reuse its texture.
    public static Texture2D GetTexture()
    {
        if (GodotObject.IsInstanceValid(_texture)) return _texture;

        const string artwork =
            "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'>" +
            "<ellipse cx='25' cy='39' rx='16' ry='4' fill='#14212b' opacity='.35'/>" +
            "<path d='M24 24 Q19 12 29 5' fill='none' stroke='#729c74' stroke-width='3'/>" +
            "<path d='M26 13 Q35 4 41 12 Q33 19 26 13Z' fill='#72b7a0'/>" +
            "<circle cx='17' cy='27' r='10' fill='#54317c' stroke='#30234e' stroke-width='2'/>" +
            "<circle cx='30' cy='27' r='10' fill='#754596' stroke='#30234e' stroke-width='2'/>" +
            "<circle cx='24' cy='35' r='9' fill='#9051b0' stroke='#30234e' stroke-width='2'/>" +
            "<ellipse cx='14' cy='23' rx='3' ry='2' fill='#b995d9'/>" +
            "<ellipse cx='27' cy='23' rx='3' ry='2' fill='#d3b1ec'/>" +
            "<ellipse cx='21' cy='32' rx='3' ry='2' fill='#d3b1ec'/>" +
            "</svg>";

        using Image image = new();
        Error error = image.LoadSvgFromString(artwork);
        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Alien berry artwork failed: {error}");

        _texture = ImageTexture.CreateFromImage(image);
        return _texture;
    }
    #endregion
}