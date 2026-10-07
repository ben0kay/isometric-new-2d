// Caches blended biome ground settings and mountain surface masks per chunk.
// Padded sampling keeps filtered textures continuous across chunk boundaries.
using Godot;
using System;
using System.Collections.Generic;

public partial class BiomeGroundMap : Node
{
    #region State
    private ImageTexture _settings, _shade, _main, _mountain;
    private ShaderMaterial _material;
    private Vector2 _origin;
    private int _size;
    #endregion

    #region Lifecycle
    // =========================================================
    // Generate data during chunk construction rather than frame callbacks.
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
        _mountain?.Dispose();

        _material = null;
        _settings = _shade = _main = _mountain = null;
    }
    #endregion

    #region Preparation
    // =========================================================
    // Prepare padded ground textures one row per staged generation step.
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
        using Image mountain = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgba8);

        for (int y = 0; y < _size; y++)
        {
            yield return ChunkBuildStage.TerrainUpload;

            for (int x = 0; x < _size; x++)
            {
                Vector2 tile = _origin + new Vector2(x, y);

                generator.SampleGround(
                    tile, out Color settingsValue,
                    out Color shadeValue, out Color mainValue);

                settings.SetPixel(x, y, settingsValue);
                shade.SetPixel(x, y, shadeValue);
                main.SetPixel(x, y, mainValue);
                mountain.SetPixel(
                    x, y, generator.SampleMountainGround(tile));
            }
        }

        yield return ChunkBuildStage.TerrainUpload;
        _settings = ImageTexture.CreateFromImage(settings);
        yield return ChunkBuildStage.TerrainUpload;
        _shade = ImageTexture.CreateFromImage(shade);
        yield return ChunkBuildStage.TerrainUpload;
        _main = ImageTexture.CreateFromImage(main);
        yield return ChunkBuildStage.TerrainUpload;
        _mountain = ImageTexture.CreateFromImage(mountain);
    }

    // =========================================================
    // Bind chunk data without modifying the shared atmosphere material.
    public ShaderMaterial Bind(ShaderMaterial source)
    {
        if (_settings == null || _shade == null ||
            _main == null || _mountain == null)
            throw new InvalidOperationException(
                "Prepare biome ground maps before binding them.");

        _material = (ShaderMaterial)source.Duplicate();
        _material.SetShaderParameter("biome_ground_enabled", true);
        _material.SetShaderParameter("biome_settings_map", _settings);
        _material.SetShaderParameter("biome_shade_map", _shade);
        _material.SetShaderParameter("biome_main_map", _main);
        _material.SetShaderParameter("biome_mountain_map", _mountain);
        _material.SetShaderParameter("biome_map_origin", _origin);
        _material.SetShaderParameter(
            "biome_map_size", new Vector2(_size, _size));

        return _material;
    }
    #endregion
}