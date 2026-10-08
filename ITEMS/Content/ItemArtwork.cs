// Builds item definitions and converts primitive artwork into inventory textures.
// Content catalogs retain the definitions, so artwork is generated once per catalog.
using Godot;
using System;

public static class ItemArtwork
{
    // =========================================================
    // Create an item with its inventory settings and optional imported icon.
    public static ItemDefinition Create(
        string id, string name, string shortName,
        int stack, float weight, float volume, Color tint,
        string artwork, Texture2D importedIcon = null)
    {
        return new ItemDefinition
        {
            Id = id,
            DisplayName = name,
            ShortName = shortName,
            MaxStack = stack,
            WeightKg = weight,
            VolumeLitres = volume,
            Tint = tint,
            Icon = importedIcon ?? Texture(artwork)
        };
    }

    // =========================================================
    // Convert artwork inside a transparent 48-pixel canvas into a texture.
    public static Texture2D Texture(string artwork)
    {
        using Image image = new();
        Error error = image.LoadSvgFromString(
            "<svg xmlns='http://www.w3.org/2000/svg' width='48' height='48'>" +
            artwork + "</svg>");

        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Item artwork could not be generated: {error}");

        return ImageTexture.CreateFromImage(image);
    }

    // =========================================================
    // Supply the existing generic placeholder for items without custom artwork.
    public static Texture2D Fallback()
    {
        return Texture(
            "<path d='M7 26 L13 10 L29 7 L41 21 L34 38 L18 41Z' fill='#8a7261'/>" +
            "<path d='M13 10 L29 7 L41 21 L24 24Z' fill='#c0a187'/>" +
            "<path d='M12 25 L19 18 L25 26 L19 34Z' fill='#cf8c58'/>");
    }
}