// Caches smoothly blended biome ground settings in small per-chunk textures.
// A padded border keeps texture filtering continuous across neighbouring chunks.
using Godot;
using System;
using System.Collections.Generic;

public partial class BiomeGroundMap : Node
{
    #region State
    private ImageTexture _settings;
    private ImageTexture _shade;
    private ImageTexture _main;
    private ShaderMaterial _material;
    private Vector2 _origin;
    private int _size;
    #endregion

    #region Lifecycle
    // =========================================================
    // Disable callbacks; ground data is generated only during chunk construction.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release resources when the owning chunk retires.
    public override void _ExitTree()
    {
        _material?.Dispose();
        _settings?.Dispose();
        _shade?.Dispose();
        _main?.Dispose();
        _material = null;
        _settings = null;
        _shade = null;
        _main = null;
    }
    #endregion

    #region Preparation
    // =========================================================
    // Prepare one row per staged generation step, including neighbour padding.
    public IEnumerable<ChunkBuildStage> Prepare(WorldChunk chunk)
    {
        WorldGenerator generator = GetTree()
            .GetFirstNodeInGroup("world_generator") as WorldGenerator;

        if (generator == null)
            throw new InvalidOperationException(
                "BiomeGroundMap requires WorldGenerator.");

        _size = chunk.ChunkSize + 2;
        _origin = new Vector2(
            chunk.Coordinate.X * chunk.ChunkSize - 1,
            chunk.Coordinate.Y * chunk.ChunkSize - 1);

        using Image settings = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);
        using Image shade = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);
        using Image main = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);

        for (int y = 0; y < _size; y++)
        {
            yield return ChunkBuildStage.TerrainUpload;

            for (int x = 0; x < _size; x++)
            {
                generator.SampleGround(
                    _origin + new Vector2(x, y),
                    out Color settingsValue,
                    out Color shadeValue,
                    out Color mainValue);

                settings.SetPixel(x, y, settingsValue);
                shade.SetPixel(x, y, shadeValue);
                main.SetPixel(x, y, mainValue);
            }
        }

        yield return ChunkBuildStage.TerrainUpload;
        _settings = ImageTexture.CreateFromImage(settings);
        yield return ChunkBuildStage.TerrainUpload;
        _shade = ImageTexture.CreateFromImage(shade);
        yield return ChunkBuildStage.TerrainUpload;
        _main = ImageTexture.CreateFromImage(main);
    }

    // =========================================================
    // Bind cached biome maps without modifying the shared atmosphere material.
    public ShaderMaterial Bind(ShaderMaterial source)
    {
        if (_settings == null || _shade == null || _main == null)
            throw new InvalidOperationException(
                "Prepare biome ground maps before binding them.");

        _material = (ShaderMaterial)source.Duplicate();
        _material.SetShaderParameter("biome_ground_enabled", true);
        _material.SetShaderParameter("biome_settings_map", _settings);
        _material.SetShaderParameter("biome_shade_map", _shade);
        _material.SetShaderParameter("biome_main_map", _main);
        _material.SetShaderParameter("biome_map_origin", _origin);
        _material.SetShaderParameter(
            "biome_map_size", new Vector2(_size, _size));

        return _material;
    }
    #endregion
}