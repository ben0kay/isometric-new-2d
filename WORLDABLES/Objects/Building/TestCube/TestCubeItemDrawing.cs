// Generates one shared inventory icon for the test cube.
// An imported ItemDefinition.Icon overrides this temporary drawing.
using Godot;
using System;

public static class TestCubeItemDrawing
{
    #region State
    private static ImageTexture _texture;
    #endregion

    // =========================================================
    // Generate the cube icon once and reuse it across inventory slots.
    public static Texture2D GetTexture()
    {
        if (GodotObject.IsInstanceValid(_texture)) return _texture;

        const string artwork = """
            <svg xmlns="http://www.w3.org/2000/svg"
                 width="48" height="48" viewBox="0 0 48 48">
              <path d="M5 16 24 6 43 16 24 26Z"
                    fill="#9bb7b8" stroke="#20333d" stroke-width="2"/>
              <path d="M5 16 24 26 24 43 5 33Z"
                    fill="#587880" stroke="#20333d" stroke-width="2"/>
              <path d="M24 26 43 16 43 33 24 43Z"
                    fill="#344d58" stroke="#20333d" stroke-width="2"/>
            </svg>
            """;

        using Image image = new();
        Error error = image.LoadSvgFromString(artwork);

        if (error != Error.Ok)
            throw new InvalidOperationException(
                $"Unable to generate test cube icon: {error}");

        _texture = ImageTexture.CreateFromImage(image);
        return _texture;
    }
}