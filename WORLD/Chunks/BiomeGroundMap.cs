// Caches biome ground settings and global slope masks during chunk construction.
// Matching half-tile slope cells align steep-rock rendering with collision.
using Godot;
using System;
using System.Collections.Generic;

public partial class BiomeGroundMap : Node
{
    #region State
    private ImageTexture _settings, _shade, _main, _mountain, _slopes;
    private ShaderMaterial _material;
    private Vector2 _origin, _slopeOrigin;
    private int _size, _slopeSize;
    #endregion

    #region Lifecycle
    // =========================================================
    // Keep terrain uploads independent from frame callbacks.
    public override void _Ready()
    {
        SetProcess(false);
        SetPhysicsProcess(false);
    }

    // =========================================================
    // Release chunk-owned textures and material.
    public override void _ExitTree()
    {
        _material?.Dispose();
        _settings?.Dispose();
        _shade?.Dispose();
        _main?.Dispose();
        _mountain?.Dispose();
        _slopes?.Dispose();

        _material = null;
        _settings = _shade = _main = _mountain = _slopes = null;
    }
    #endregion

    #region Preparation
    // =========================================================
    // Generate padded biome settings and staged half-tile slope data.
    public IEnumerable<ChunkBuildStage> Prepare(WorldChunk chunk)
    {
        WorldGenerator generator = GetTree()
            .GetFirstNodeInGroup("world_generator") as WorldGenerator;

        if (generator == null)
            throw new InvalidOperationException(
                "BiomeGroundMap requires WorldGenerator.");

        TerrainSlopeWorld slopes = TerrainSlopeWorld.Ensure(this);

        _size = chunk.ChunkSize + 2;
        _origin = new Vector2(
            chunk.Coordinate.X * chunk.ChunkSize - 1,
            chunk.Coordinate.Y * chunk.ChunkSize - 1);

        _slopeSize = chunk.ChunkSize * 2 + 4;
        Vector2I firstCell = new(
            chunk.Coordinate.X * chunk.ChunkSize * 2 - 2,
            chunk.Coordinate.Y * chunk.ChunkSize * 2 - 2);
        _slopeOrigin = new Vector2(
            firstCell.X * 0.5f - 0.5f,
            firstCell.Y * 0.5f - 0.5f);

        using Image settings = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);
        using Image shade = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);
        using Image main = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgbaf);
        using Image mountain = Image.CreateEmpty(
            _size, _size, false, Image.Format.Rgba8);
        using Image slopeImage = Image.CreateEmpty(
            _slopeSize, _slopeSize, false, Image.Format.Rgbaf);

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

        for (int y = 0; y < _slopeSize; y++)
        for (int x = 0; x < _slopeSize; x++)
        {
            if ((x & 3) == 0)
                yield return ChunkBuildStage.TerrainUpload;

            TerrainSlopeWorld.SlopeSample sample = slopes.SampleCell(
                firstCell + new Vector2I(x, y));

            slopeImage.SetPixel(x, y, new Color(
                sample.Angle / 90f,
                sample.Gradient.X, sample.Gradient.Y,
                sample.Blocked ? 1f : 0f));
        }

        yield return ChunkBuildStage.TerrainUpload;
        _settings = ImageTexture.CreateFromImage(settings);
        yield return ChunkBuildStage.TerrainUpload;
        _shade = ImageTexture.CreateFromImage(shade);
        yield return ChunkBuildStage.TerrainUpload;
        _main = ImageTexture.CreateFromImage(main);
        yield return ChunkBuildStage.TerrainUpload;
        _mountain = ImageTexture.CreateFromImage(mountain);
        yield return ChunkBuildStage.TerrainUpload;
        _slopes = ImageTexture.CreateFromImage(slopeImage);
    }

    // =========================================================
    // Bind cached world data without modifying the shared source material.
    public ShaderMaterial Bind(ShaderMaterial source)
    {
        if (_settings == null || _shade == null || _main == null ||
            _mountain == null || _slopes == null)
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

        _material.SetShaderParameter("terrain_slope_map", _slopes);
        _material.SetShaderParameter("terrain_slope_origin", _slopeOrigin);
        _material.SetShaderParameter(
            "terrain_slope_size",
            Vector2.One * (_slopeSize * 0.5f));

        return _material;
    }
    #endregion
}