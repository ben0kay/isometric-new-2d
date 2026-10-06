// Draws the water-specific artwork for a reusable liquid body.
// Fill, immersion and debug contours are handled by LiquidBody.
using Godot;

public partial class WaterPatch : LiquidBody
{
    #region State
    private ShaderMaterial _material;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build one static surface quad using the existing water shader.
    public override void _Ready()
    {
        WaterDefinition water = Basin.Definition;
        ZIndex = -1;
        ZAsRelative = false;

        _material = new ShaderMaterial { Shader = water.WaterShader };
        _material.SetShaderParameter("water_color", Liquid.SurfaceColour);
        _material.SetShaderParameter("radius_tiles", water.RadiusTiles);
        _material.SetShaderParameter("phase", Phase);
        _material.SetShaderParameter("wave_speed", water.WaveSpeed);
        _material.SetShaderParameter("wave_strength", water.WaveStrength);
        _material.SetShaderParameter("opacity", water.Opacity);
        _material.SetShaderParameter("basin_depth", water.BasinDepth);
        _material.SetShaderParameter("shore_width", water.ShoreWidthTiles);

        using Image image = Image.CreateEmpty(
            1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);

        AddChild(new Polygon2D
        {
            Name = "WaterArtwork",
            Polygon = new[]
            {
                LocalPoint(new Vector2(-1.15f, -1.15f)),
                LocalPoint(new Vector2(1.15f, -1.15f)),
                LocalPoint(new Vector2(1.15f, 1.15f)),
                LocalPoint(new Vector2(-1.15f, 1.15f))
            },
            UV = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            },
            Texture = ImageTexture.CreateFromImage(image),
            Material = _material
        });

        base._Ready();
    }
    #endregion

    #region Rendering
    // =========================================================
    // Move the shader contour when the liquid level changes.
    protected override void UpdateFillArtwork()
    {
        _material?.SetShaderParameter("water_drop", Basin.WaterDrop);
    }
    #endregion
}