// Draws water using neutral shader detail and the basin's selected tint.
// Fill, immersion and debug contours remain handled by LiquidBody.
using Godot;

public partial class WaterPatch : LiquidBody
{
    #region State
    private ShaderMaterial _material;
    #endregion

    #region Lifecycle
    // =========================================================
    // Build a static surface quad using this basin's geometry and water tint.
    public override void _Ready()
    {
        WaterDefinition water = Basin.Definition;
        ZIndex = -1;
        ZAsRelative = false;

        _material = new ShaderMaterial { Shader = water.WaterShader };
        _material.SetShaderParameter(
            "water_color", Liquid.SurfaceColour * water.SurfaceTint);
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
    // Update the shader contour when liquid level changes.
    protected override void UpdateFillArtwork()
    {
        _material?.SetShaderParameter("water_drop", Basin.WaterDrop);
    }
    #endregion
}