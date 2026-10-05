// Builds one static water quad; the shader supplies its boundary and animation.
using Godot;

public partial class WaterPatch : SurfacePatch
{
    #region Lifecycle
    // =========================================================
    // Position the water above terrain and below actors without changing ground meshes.
    public override void _Ready()
    {
        WaterDefinition water = (WaterDefinition)Definition;
        ZIndex = -1;
        ZAsRelative = false;
        GlobalPosition = World.TileToWorld(TileCentre) +
            Vector2.Up * SurfaceHeight;

        ShaderMaterial material = new() { Shader = water.WaterShader };
        material.SetShaderParameter("water_color", water.SurfaceTint);
        material.SetShaderParameter("radius_tiles", water.RadiusTiles);
        material.SetShaderParameter("phase", Phase);
        material.SetShaderParameter("wave_speed", water.WaveSpeed);
        material.SetShaderParameter("wave_strength", water.WaveStrength);
        material.SetShaderParameter("opacity", water.Opacity);

        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        image.Fill(Colors.White);
        ImageTexture texture = ImageTexture.CreateFromImage(image);

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
            Texture = texture,
            Material = material
        });

        World.Register(this);
        SetProcess(false);
    }
    #endregion
}