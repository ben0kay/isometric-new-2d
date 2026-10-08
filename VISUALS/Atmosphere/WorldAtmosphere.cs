// Shares fixed sunlight settings, cached obstacle shadows and one ravine fog material.
// Static artwork is generated at load time; fog animation runs in the shader.
using Godot;

public partial class WorldAtmosphere : Node
{
    #region Configuration
    [Export] public Vector2 SunDirection { get; set; } = new(-1f, -0.7f);
    [Export] public Color SunTint { get; set; } = new(1f, 0.96f, 0.87f);
    [Export] public float FaceAmbient { get; set; } = 0.65f;
    [Export] public float FaceSunStrength { get; set; } = 0.55f;
    [Export] public float ShadowLength { get; set; } = 1.8f;
    [Export] public float ShadowOpacity { get; set; } = 0.38f;
    [Export] public Color FogColor { get; set; } = new("#294f6d");
    [Export] public float FogDensity { get; set; } = 0.65f;
    [Export] public float FogNoiseScale { get; set; } = 0.012f;
    [Export] public Vector2 FogDrift { get; set; } = new(3f, -1f);
    [Export] public float GroundSunStrength { get; set; } = 2.2f;
    #endregion

    #region State
    public ShaderMaterial FogMaterial { get; private set; }
    public Vector2 LightDirection => SunDirection.LengthSquared() > 0.0001f
        ? SunDirection.Normalized() : new Vector2(-1f, -0.7f).Normalized();

    private TerrainElevation _elevation;
    private ChunkController _chunks;
    private Node2D _ground;
    public ShaderMaterial GroundMaterial { get; private set; }
private NoiseTexture2D _mistTexture;
    #endregion

    #region Lifecycle
    // =========================================================
    // Register before scene children begin building their artwork.
    public override void _EnterTree()
    {
        AddToGroup("world_atmosphere");
    }

// =========================================================
// Prepare shared noise textures, continuous ground materials and ravine mist.
public override void _Ready()
{
    _elevation = GetNode<TerrainElevation>("../TerrainElevation");
    _chunks = GetNode<ChunkController>("../ChunkController");
    _ground = GetNode<Node2D>("../../GroundChunks");

    _mistTexture = new NoiseTexture2D
    {
        Width = 256,
        Height = 256,
        Seamless = true,
        Normalize = true,
        Noise = new FastNoiseLite
        {
            Seed = 64,
            NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth,
            Frequency = 0.018f,
            FractalOctaves = 3
        }
    };

    FogMaterial = new ShaderMaterial
    {
        Shader = GD.Load<Shader>("res://VISUALS/Atmosphere/RavineFog.gdshader")
    };
    FogMaterial.SetShaderParameter("mist_texture", _mistTexture);
    FogMaterial.SetShaderParameter("fog_color", FogColor);
    FogMaterial.SetShaderParameter("fog_density", Mathf.Clamp(FogDensity, 0f, 1f));
    FogMaterial.SetShaderParameter("noise_scale", Mathf.Max(0.0001f, FogNoiseScale));
    FogMaterial.SetShaderParameter("drift", FogDrift);

    GroundMaterial = new ShaderMaterial
    {
        Shader = GD.Load<Shader>("res://VISUALS/Atmosphere/GroundSun.gdshader")
    };
    GroundMaterial.SetShaderParameter("ground_noise", GroundSurfaceNoise.GetTexture());
    GroundMaterial.SetShaderParameter("mist_texture", _mistTexture);
    GroundMaterial.SetShaderParameter("sun_direction", LightDirection);
    GroundMaterial.SetShaderParameter("sun_color", SunTint);
    GroundMaterial.SetShaderParameter("sun_strength", Mathf.Max(0f, GroundSunStrength));
}
    #endregion

    #region Sunlight
    // =========================================================
    // Tint a baked face according to its approximate screen-space orientation.
    public Color ShadeFace(Color baseColor, Vector2 outwardNormal)
    {
        float facing = Mathf.Max(0f, outwardNormal.Normalized().Dot(LightDirection));
        float intensity = Mathf.Max(0f, FaceAmbient + facing * FaceSunStrength);
        return new Color(
            baseColor.R * SunTint.R * intensity,
            baseColor.G * SunTint.G * intensity,
            baseColor.B * SunTint.B * intensity,
            baseColor.A);
    }
    #endregion

       #region Shadows
    // =========================================================
    // Preserve generated rock and crate shadow footprints.
    public void CreateObstacleShadow(Obstacle obstacle)
    {
        float width = obstacle.Footprint.X * 0.48f;
        float depth = obstacle.Footprint.Y * 0.4f;

        Vector2[] footprint;

        if (obstacle.Kind == Obstacle.ObstacleKind.Rock)
        {
            footprint = new Vector2[12];

            for (int i = 0; i < footprint.Length; i++)
            {
                float angle = Mathf.Tau * i / footprint.Length;

                footprint[i] = new Vector2(
                    Mathf.Cos(angle) * width,
                    Mathf.Sin(angle) * depth);
            }
        }
        else
        {
            footprint = new Vector2[]
            {
                new(-width, -depth),
                new(width, -depth),
                new(width, depth),
                new(-width, depth)
            };
        }

        CreateObstacleShadow(obstacle, footprint);
    }

    // =========================================================
    // Project a supplied ground footprint away from the sunlight.
    // Footprint points are local to the obstacle, before terrain height adjustment.
    public void CreateObstacleShadow(
        Obstacle obstacle, Vector2[] footprint)
    {
        if (footprint == null || footprint.Length < 3 ||
            obstacle.GetNodeOrNull<Polygon2D>("SunShadow") != null)
            return;

        float length = obstacle.Height * Mathf.Max(0f, ShadowLength);
        float opacity = Mathf.Clamp(ShadowOpacity, 0f, 1f);

        if (length < 1f || opacity <= 0f) return;

        Vector2 direction = -LightDirection;
        Vector2 extension = direction * length;

        Vector2[] candidates = new Vector2[footprint.Length * 2];

        for (int i = 0; i < footprint.Length; i++)
        {
            candidates[i] = footprint[i];

            // Buildings keep their full projected width.
            float taper = obstacle is PlacedObject ? 1f : 0.7f;
            candidates[i + footprint.Length] =
                extension + footprint[i] * taper;
        }

        Vector2[] hull = Geometry2D.ConvexHull(candidates);

        if (hull.Length > 1 &&
            hull[0].IsEqualApprox(hull[hull.Length - 1]))
            System.Array.Resize(ref hull, hull.Length - 1);

        if (hull.Length < 3) return;

        Vector2[] points = new Vector2[hull.Length];
        Color[] colors = new Color[hull.Length];

        for (int i = 0; i < hull.Length; i++)
        {
            Vector2 globalPoint = obstacle.ToGlobal(hull[i]);

            if (!ChasmFeature.HasGroundClearance(
                _ground.ToLocal(globalPoint), _chunks.TileSize))
                return;

            Vector2 visualPoint = globalPoint;
            visualPoint.Y -= _elevation.SampleWorldHeight(globalPoint);

            points[i] = obstacle.ToLocal(visualPoint);

            float fraction = Mathf.Clamp(
                hull[i].Dot(direction) / length, 0f, 1f);

            colors[i] = new Color(
                0.015f, 0.025f, 0.04f,
                opacity * (1f - fraction * 0.65f));
        }

        obstacle.AddChild(new Polygon2D
        {
            Name = "SunShadow",
            Polygon = points,
            VertexColors = colors,
            Color = Colors.White,
            ZAsRelative = false,
            ZIndex = -1
        });
    }
    #endregion
}