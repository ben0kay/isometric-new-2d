// Presents water inside a permanent basin and adjusts its contour as fill changes.
using Godot;

public partial class WaterPatch : SurfacePatch
{
    #region State
    public WaterBasinWorld.Basin Basin { get; set; }
    private ShaderMaterial _material;
    private float _shoreInset;
    #endregion

    #region Lifecycle
    // =========================================================
    // Create a single static quad; shader animation does not rebuild geometry.
    public override void _Ready()
    {
        WaterDefinition water = Basin.Definition;
        ZIndex = -1;
        ZAsRelative = false;

        _material = new ShaderMaterial { Shader = water.WaterShader };
        _material.SetShaderParameter("water_color", water.SurfaceTint);
        _material.SetShaderParameter("radius_tiles", water.RadiusTiles);
        _material.SetShaderParameter("phase", Phase);
        _material.SetShaderParameter("wave_speed", water.WaveSpeed);
        _material.SetShaderParameter("wave_strength", water.WaveStrength);
        _material.SetShaderParameter("opacity", water.Opacity);
        _material.SetShaderParameter("basin_depth", water.BasinDepth);
        _material.SetShaderParameter("shore_width", water.ShoreWidthTiles);

        using Image image = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
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

        SetFill(Basin.Fill);
        World.Register(this);
        SetProcess(false);
    }
    #endregion

    #region Water Level
    // =========================================================
    // Drain or refill water while preserving the basin's generated terrain.
    public void SetFill(float fill)
    {
        Basin.Fill = Mathf.Clamp(fill, 0f, 1f);
        SurfaceHeight = Basin.WaterHeight;
        _shoreInset = Basin.ShoreInsetNormalized();
        GlobalPosition = World.TileToWorld(TileCentre) +
            Vector2.Up * SurfaceHeight;

        Visible = Basin.Fill > 0.0001f;
        _material?.SetShaderParameter("water_drop", Basin.WaterDrop);
    }

    // =========================================================
    // Apply water effects only where the basin floor lies below the water level.
    public override float GetInfluence(Vector2 tile)
    {
        if (Basin.Fill <= 0.0001f) return 0f;
        return Mathf.Clamp(
            (Basin.DepthAt(tile) - Basin.WaterDrop) / 8f, 0f, 1f);
    }

    // =========================================================
    // Follow the current water contour rather than the outer basin boundary.
    public override Vector2[] GetDebugOutline()
    {
        if (Basin.Fill <= 0.0001f) return System.Array.Empty<Vector2>();

        const int segments = 48;
        Vector2[] points = new Vector2[segments];
        for (int i = 0; i < segments; i++)
        {
            float angle = Mathf.Tau * i / segments;
            float radius = Mathf.Max(
                0f, SurfaceGeometry.Edge(angle, Phase) - _shoreInset);
            points[i] = ToGlobal(LocalPoint(
                new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius));
        }
        return points;
    }
    #endregion
}